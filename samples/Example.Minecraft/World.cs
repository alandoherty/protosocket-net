using Example.Minecraft.Net;
using Example.Minecraft.Net.Packets;
using ProtoSocket;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Primitives;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
        private ClassicServer _server = null;
        private ConcurrentQueue<(Player, ClassicPacket)> _incomingPackets = new ConcurrentQueue<(Player, ClassicPacket)>();
        private DateTimeOffset _nextPing = DateTimeOffset.UtcNow;

        private byte[] _level = new byte[(256 * 256) * 64];
        #endregion

        #region Properties
        /// <summary>
        /// Gets or sets the tick rate (per second).
        /// </summary>
        public double TickRate {
            get; set;
        } = 20;

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
                    text, SystemFonts.CreateFont("Comic Sans MS", 16), Rgba32.Black, new PointF(image.Width / 2, image.Height / 2)
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
                    text, SystemFonts.CreateFont("Comic Sans MS", 16), Rgba32.Black, new PointF(image.Width / 2, image.Height / 2)
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

        public void SetBlock(int x, int y, int z, byte blockId) {
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

                p.Connection.Queue(setBlockPacket);
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

        /// <summary>
        /// Runs the world game loop.
        /// </summary>
        /// <returns></returns>
        public async Task RunAsync() {
            // create a stopwatch to keep track of the tick times and also log we started
            Stopwatch tickStopwatch = new Stopwatch();
            Console.WriteLine($"Running Example.Minecraft at {_server.Endpoint}");

            // get tick rate
            int tickMs = (int)(1000 / TickRate);

            while (true) {
                tickStopwatch.Restart();

                // process all inbound packets
                (Player, ClassicPacket) incomingPacket = default((Player, ClassicPacket));

                while (_incomingPackets.TryDequeue(out incomingPacket)) {
                    ClassicPacket packet = incomingPacket.Item2;
                    Player player = incomingPacket.Item1;

                    switch (packet.Id) {
                        case PacketId.PositionAngle:
                            PacketReader posAngReader = new PacketReader(packet.Payload);
                            posAngReader.ReadByte();

                            // update position
                            player.UpdatePosition(posAngReader.ReadShort() / 32.0f, posAngReader.ReadShort() / 32.0f, posAngReader.ReadShort() / 32.0f, (posAngReader.ReadByte() / 255.0f) * 360.0f, (posAngReader.ReadByte() / 255.0f) * 360.0f, false);
                            break;

                        case PacketId.Message:
                            PacketReader msgReader = new PacketReader(packet.Payload);
                            msgReader.ReadByte();
                            string msgText = msgReader.ReadString();

                            if (msgText.StartsWith('/')) {
                                if (msgText.StartsWith("/clear", StringComparison.CurrentCultureIgnoreCase)) {
                                    ClearText();
                                } else if (msgText.StartsWith("/text ", StringComparison.CurrentCultureIgnoreCase)) {
                                    ClearText();
                                    DrawText(msgText.Substring(6));
                                } else if (msgText.StartsWith("/texta ", StringComparison.CurrentCultureIgnoreCase)) {
                                    ClearText();
                                    DrawTextAnimated(msgText.Substring(7));
                                } else if (msgText.StartsWith("/load", StringComparison.CurrentCultureIgnoreCase)) {
                                    Load();
                                } else if (msgText.StartsWith("/save", StringComparison.CurrentCultureIgnoreCase)) {
                                    Save();
                                } else {
                                    player.Message(-1, "Unknown command");
                                }
                            } else {
                                foreach (Player p in Players)
                                    p.Message(p == player ? (sbyte)-1 : player.ID, string.Format("{0}: {1}", player.Name, msgText));
                            }

                            break;

                        case PacketId.AskBlock:
                            PacketReader askReader = new PacketReader(packet.Payload);
                            int askX = askReader.ReadShort();
                            int askY = askReader.ReadShort();
                            int askZ = askReader.ReadShort();
                            byte askMode = askReader.ReadByte();
                            byte askBlockType = askReader.ReadByte();

                            if (askMode == 0) {
                                SetBlock(askX, askY, askZ, 0);
                            } else {
                                SetBlock(askX, askY, askZ, askBlockType);
                            }
                            break;

                        default:
                            Console.WriteLine("Unknown packet: {0}", packet.Id.ToString());
                            break;
                    }
                }

                // send all outbound packets
                List<Task> sendTasks = new List<Task>();

                foreach (ClassicConnection connection in _server.Connections) {
                    if (connection.IsConnected) {
                        // queue ping if it's due
                        if (_nextPing <= DateTime.UtcNow)
                            connection.Queue(new ClassicPacket() { Id = PacketId.Ping, Payload = new byte[0] });

                        // send all queued frames
                        sendTasks.Add(connection.SendAsync());
                    }
                }

                await Task.WhenAll(sendTasks);

                // set next ping
                if (_nextPing <= DateTime.UtcNow)
                    _nextPing = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);

                // wait up to tick rate
                if (tickStopwatch.ElapsedMilliseconds > tickMs)
                    Console.WriteLine($"Server behind, took {tickStopwatch.ElapsedMilliseconds - tickMs}ms longer to process tick");
                else
                    await Task.Delay(tickMs - (int)tickStopwatch.ElapsedMilliseconds);
            }
        }
        #endregion

        #region Event Handlers
        private void OnDisconnected(object sender, PeerDisconnectedEventArgs<ClassicPacket> e) {
            // find player
            Player player = FindPlayer((ClassicConnection)e.Peer);

            if (player == null)
                return;

            // remove player
            RemovePlayer(player);

            foreach (Player p in Players) {
                p.Message(-1, player.Name + " disconnected");
            }

            Console.WriteLine(e.Peer.RemoteEndPoint + " disconnected due to " + e.Peer.CloseReason);
        }

        private async void OnConnected(object sender, PeerConnectedEventArgs<ClassicPacket> e) {
            // set tcp options
            e.Peer.NoDelay = true;
            e.Peer.KeepAlive = true;

            // receive identification
            ClassicPacket packet = await e.Peer.ReceiveAsync();

            if (packet.Id != PacketId.Identification) {
                e.Peer.Dispose();
                return;
            }

            // read packet
            PacketReader reader = new PacketReader(packet.Payload);
            string username = null;

            try {
                reader.ReadByte();
                username = reader.ReadString();
            } catch (Exception) {
                e.Peer.Dispose();
                return;
            }

            // respond
            ClassicPacket response = new ClassicPacket();
            response.Id = PacketId.Identification;

            PacketWriter writer = new PacketWriter();
            writer.WriteByte(0x07);
            writer.WriteString("Example.Minecraft");
            writer.WriteString("An example protocol server for Minecraft Classic");
            writer.WriteByte(0x00);
            response.Payload = writer.ToArray();

            await e.Peer.SendAsync(response);

            // check max players
            sbyte playerId = NextPlayerID();

            // create player
            Player player = new Player(this, NextPlayerID(), (ClassicConnection)e.Peer, username);

            if (playerId == -1) {
                player.Kick("Maximum players reached");
                return;
            }

            // set player
            e.Peer.Userdata = player;

            // get level
            byte[] compressedLevel = GetCompressedLevel();

            // send initialize packet
            ClassicPacket initPacket = new ClassicPacket();
            initPacket.Id = PacketId.LevelInitialize;
            initPacket.Payload = new byte[0];
            await e.Peer.SendAsync(initPacket);

            // build the data
            int bytesSent = 0;

            while (bytesSent < compressedLevel.Length) {
                byte[] levelChunk = new byte[1024];
                int chunkSize = Math.Min(1024, compressedLevel.Length - bytesSent);
                Buffer.BlockCopy(compressedLevel, bytesSent, levelChunk, 0, chunkSize);

                bytesSent += chunkSize;

                // send data apcket
                ClassicPacket dataPacket = new ClassicPacket();
                dataPacket.Id = PacketId.LevelDataChunk;

                PacketWriter dataWriter = new PacketWriter();
                dataWriter.WriteShort((short)chunkSize);
                dataWriter.WriteByteArray(levelChunk);
                dataWriter.WriteByte((byte)(((float)bytesSent / compressedLevel.Length) * 100.0f));
                dataPacket.Payload = dataWriter.ToArray();

                await e.Peer.SendAsync(dataPacket);
            }

            // send finish packet
            ClassicPacket finishPacket = new ClassicPacket();
            finishPacket.Id = PacketId.LevelFinalize;

            PacketWriter finishWriter = new PacketWriter();
            finishWriter.WriteShort((short)Width);
            finishWriter.WriteShort((short)Height);
            finishWriter.WriteShort((short)Depth);
            finishPacket.Payload = finishWriter.ToArray();
            await e.Peer.SendAsync(finishPacket);

            e.Peer.Subscribe(new PacketSubscriber() { Player = player, World = this });

            // add player and spawn
            AddPlayer(player);
            player.Spawn();

            // announce
            foreach (Player p in Players) {
                if (p != player)
                    p.Message(-1, player.Name + " connected");
            }

            // spawn existing players
            foreach (Player p in Players) {
                p.Spawn(player);
            }
        }
        #endregion

        class PacketSubscriber : IObserver<ClassicPacket>
        {
            public Player Player { get; set; }
            public World World { get; set; }

            public void OnCompleted() {
            }

            public void OnError(Exception error) {
            }

            public void OnNext(ClassicPacket value) {
                World._incomingPackets.Enqueue((Player, value));
            }
        }

        #region Constructors
        /// <summary>
        /// Creates a new world.
        /// </summary>
        public World(ClassicServer server) {
            _server = server;
            _server.Disconnected += OnDisconnected;
            _server.Connected += OnConnected;

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
