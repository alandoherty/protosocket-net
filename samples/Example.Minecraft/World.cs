using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Text;
using SixLabors.Primitives;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Example.Minecraft
{
    /// <summary>
    /// Represents a Minecraft classic world.
    /// </summary>
    public class World
    {
        #region Fields
        private Dictionary<sbyte, Player> _players = new Dictionary<sbyte, Player>();
        private byte[] _level = new byte[(256 * 256) * 64];
        #endregion

        #region Properties
        public Player[] Players {
            get {
                return _players.Values.ToArray();
            }
        }

        public int Width {
            get {
                return 256;
            }
        }

        public int Height {
            get {
                return 64;
            }
        }

        public int Depth {
            get {
                return 256;
            }
        }
        #endregion

        #region Methods
        public Player FindPlayer(string name) {
            foreach (Player p in _players.Values) {
                if (p.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase))
                    return p;
            }

            return null;
        }

        public Player FindPlayer(sbyte id) {
            foreach (Player p in _players.Values) {
                if (p.ID == id)
                    return p;
            }

            return null;
        }

        public Player FindPlayer(ClassicConnection connection) {
            foreach(Player p in _players.Values) {
                if (p.Connection == connection)
                    return p;
            }

            return null;
        }

        public sbyte NextPlayerID() {
            for (sbyte i = 0; i < sbyte.MaxValue; i++) {
                if (!_players.ContainsKey(i))
                    return i;
            }

            return -1;
        }

        public byte[] GetCompressedLevel() {
            using (MemoryStream ms = new MemoryStream()) {
                using (GZipStream stream = new GZipStream(ms, CompressionLevel.Fastest)) {
                    // write block count
                    byte[] blockCountBytes = BitConverter.GetBytes(_level.Length);
                    Array.Reverse(blockCountBytes);
                    stream.Write(blockCountBytes, 0, 4);

                    // write raw level data
                    lock (_level) {
                        stream.Write(_level, 0, _level.Length);
                    }

                    stream.Flush();
                }

                return ms.ToArray();
            }
        }

        public void ClearText() {
            for (int x = 0; x < 128; x++) {
                for (int y = 0; y < 18; y++) {
                    SetBlock(64, ((18 - y) + 34), ((128 - x) + 64), 0x00);
                    SetBlock(65, ((18 - y) + 34), ((128- x) + 64), 0x00);
                }
            }
        }

        public void DrawText(string text) {
            // draw text
            Image<Rgba32> image = new Image<Rgba32>(128, 18);
            image.Mutate(delegate (IImageProcessingContext<Rgba32> ctx) {
                ctx.DrawText(
                    new TextGraphicsOptions() {
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    text, SystemFonts.CreateFont("Courier New", 16), Rgba32.Black, new PointF(image.Width / 2, image.Height / 2)
                );
            });

            for (int x = 0; x < image.Width; x++) {
                for (int y = 0; y < image.Height; y++) {
                    if (image[x, y].A != 0) {
                        SetBlock(64, ((image.Height - y) + 34), ((image.Width - x) + 64), 0x07);
                        SetBlock(65, ((image.Height - y) + 34), ((image.Width - x) + 64), 0x07);
                    }
                }
            }
        }

        public async void DrawTextAnimated(string text) {
            // draw text
            Image<Rgba32> image = new Image<Rgba32>(128, 18);
            image.Mutate(delegate (IImageProcessingContext<Rgba32> ctx) {
                ctx.DrawText(
                    new TextGraphicsOptions() {
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    text, SystemFonts.CreateFont("Courier New", 16), Rgba32.Black, new PointF(image.Width / 2, image.Height / 2)
                );
            });

            List<ValueTuple<int, int, int>> blockChanges = new List<ValueTuple<int, int, int>>();

            for (int x = 0; x < image.Width; x++) {
                for (int y = 0; y < image.Height; y++) {
                    if (image[x, y].A != 0) {
                        blockChanges.Add(new ValueTuple<int, int, int>(64, ((image.Height - y) + 34), ((image.Width - x) + 64)));
                        blockChanges.Add(new ValueTuple<int, int, int>(65, ((image.Height - y) + 34), ((image.Width - x) + 64)));
                    }
                }
            }

            Random random = new Random();

            while(blockChanges.Count > 0) {
                for (int i = 0; (i < 10) && blockChanges.Count > 0; i++) {
                    // get a change
                    int blockIndex = random.Next(0, blockChanges.Count);
                    ValueTuple<int, int, int> blockChange = blockChanges[blockIndex];
                    blockChanges.RemoveAt(blockIndex);

                    // apply it
                    SetBlock(blockChange.Item1, blockChange.Item2, blockChange.Item3, 0x07);
                }

                // wait 10ms
                await Task.Delay(10);
            }
        }

        public async void SetBlock(int x, int y, int z, byte blockId) {
            // validate coordinates
            if (x < 0 || x > 256)
                throw new ArgumentOutOfRangeException("The x-coordinate cannot be lower than zero or higher than 256");
            if (y < 0 || y > 64)
                throw new ArgumentOutOfRangeException("The y-coordinate cannot be lower than zero or higher than 64");
            if (z < 0 || z > 256)
                throw new ArgumentOutOfRangeException("The z-coordinate cannot be lower than zero or higher than 256");

            // set in level
            lock (_level) {
                _level[x + Depth * (z + Width * y)] = blockId;
            }

            // update players
            foreach(Player p in Players) {
                ClassicPacket setBlockPacket = new ClassicPacket();
                setBlockPacket.Id = PacketId.SetBlock;

                PacketWriter setBlockWriter = new PacketWriter();
                setBlockWriter.WriteShort((short)x);
                setBlockWriter.WriteShort((short)y);
                setBlockWriter.WriteShort((short)z);
                setBlockWriter.WriteByte(blockId);
                setBlockPacket.Payload = setBlockWriter.ToArray();

                await p.Connection.SendAsync(setBlockPacket);
            }
        }

        public void MessageAll(string msg) {
            foreach (Player p in Players)
                p.Message(-1, msg);
        }

        public async void Save() {
            using (MemoryStream ms = new MemoryStream()) {
                // write player locations
                BinaryWriter playerWriter = new BinaryWriter(ms);
                List<string> playerSaved = new List<string>();

                foreach (Player p in Players) {
                    if (playerSaved.Contains(p.Name, StringComparer.CurrentCultureIgnoreCase))
                        continue;

                    playerWriter.Write((byte)1);
                    playerWriter.Write(p.Name);
                    playerWriter.Write(p.X);
                    playerWriter.Write(p.Y);
                    playerWriter.Write(p.Z);
                    playerWriter.Write(p.Yaw);
                    playerWriter.Write(p.Pitch);

                    playerSaved.Add(p.Name);
                }

                playerWriter.Write((byte)0);

                // write level data
                using (GZipStream stream = new GZipStream(ms, CompressionLevel.Optimal)) {
                    await stream.WriteAsync(_level, 0, _level.Length);
                    await stream.FlushAsync();
                }

                await File.WriteAllBytesAsync("world.dat", ms.ToArray());
            }
        }

        public async void Load() {
            using (MemoryStream ms = new MemoryStream(await File.ReadAllBytesAsync("world.dat"))) {
                // read player locations
                BinaryReader playerReader = new BinaryReader(ms);

                while(playerReader.ReadByte() == 1) {
                    Player p = FindPlayer(playerReader.ReadString());

                    if (p != null) {
                        p.UpdatePosition(playerReader.ReadSingle(), playerReader.ReadSingle(), playerReader.ReadSingle(), playerReader.ReadSingle(), playerReader.ReadSingle(), true);
                    }
                }

                // read level data
                byte[] buffer = new byte[_level.Length];

                using (GZipStream stream = new GZipStream(ms, CompressionMode.Decompress)) {
                    int nRead = 0;

                    while ((nRead = stream.Read(buffer, nRead, buffer.Length - nRead)) > 0) {
                    }
                }

                _level = buffer;
            }
        }

        public byte GetBlock(int x, int y, int z, byte blockId) {
            // validate coordinates
            if (x < 0 || x > 256)
                throw new ArgumentOutOfRangeException("The x-coordinate cannot be lower than zero or higher than 256");
            if (y < 0 || y > 64)
                throw new ArgumentOutOfRangeException("The y-coordinate cannot be lower than zero or higher than 64");
            if (z < 0 || z > 256)
                throw new ArgumentOutOfRangeException("The z-coordinate cannot be lower than zero or higher than 256");

            return _level[x + Depth * (z + Width * y)];
        }
        #endregion

        #region Methods
        /// <summary>
        /// Adds a player to the world.
        /// </summary>
        /// <param name="player">The player.</param>
        public void AddPlayer(Player player) {
            _players.Add(player.ID, player);
        }

        /// <summary>
        /// Adds a player to the world.
        /// </summary>
        /// <param name="player">The player.</param>
        public void RemovePlayer(Player player) {
            // despawn
            if (player.Spawned)
                player.Despawn();

            // remove
            _players.Remove(player.ID);
        }
        #endregion

        #region Constructors
        /// <summary>
        /// Creates a new world.
        /// </summary>
        public World() {
            // add grass layer
            for (int x = 0; x < 256; x++) {
                for (int z = 0; z < 256; z++) {
                    for (int y = 0; y < 64; y++) {
                        if (y == 32) {
                            _level[x + Depth * (z + Width * y)] = 0x2;
                        } else if (y < 32) {
                            _level[x + Depth * (z + Width * y)] = 0x1;
                        }
                    }
                }
            }

            // draw initial text
            DrawText("WIFIPLUG (C)");
        }
        #endregion
    }
}
