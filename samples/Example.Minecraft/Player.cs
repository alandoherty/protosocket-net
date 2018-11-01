using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Example.Minecraft
{
    /// <summary>
    /// Represents a player.
    /// </summary>
    public class Player
    {
        #region Fields
        private string _name;
        private World _world;
        private ClassicConnection _connection;
        private bool _spawned;
        private float _x = 128;
        private float _y = 40;
        private float _z = 128;
        private float _yaw;
        private sbyte _id;
        private float _pitch;
        #endregion

        #region Properties
        public float X {
            get {
                return _x;
            }
        }

        public float Y {
            get {
                return _y;
            }
        }

        public float Z {
            get {
                return _z;
            }
        }

        public float Yaw {
            get {
                return _yaw;
            }
        }

        public float Pitch {
            get {
                return _pitch;
            }
        }

        /// <summary>
        /// Gets the player ID.
        /// </summary>
        public sbyte ID {
            get {
                return _id;
            }
        }

        /// <summary>
        /// Gets if the player is spawned.
        /// </summary>
        public bool Spawned {
            get {
                return _spawned;
            }
        }

        /// <summary>
        /// Gets the world the player is in.
        /// </summary>
        public World World {
            get {
                return _world;
            }
        }

        /// <summary>
        /// Gets the connection.
        /// </summary>
        public ClassicConnection Connection {
            get {
                return _connection;
            }
        }

        /// <summary>
        /// Gets the player name.
        /// </summary>
        public string Name {
            get {
                return _name;
            }
        }
        #endregion

        #region Methods
        /// <summary>
        /// Spawns the player.
        /// </summary>
        public async void Spawn() {
            if (_spawned)
                return;

            _spawned = true;

            // send packet
            List<Task> sendTasks = new List<Task>();

            foreach (Player p in _world.Players) {
                ClassicPacket spawnPacket = new ClassicPacket();
                spawnPacket.Id = PacketId.SpawnPlayer;

                PacketWriter spawnWriter = new PacketWriter();
                spawnWriter.WriteSByte(this == p ? (sbyte)-1 : ID);
                spawnWriter.WriteString(Name);
                spawnWriter.WriteShort((short)(X * 32));
                spawnWriter.WriteShort((short)(Y * 32));
                spawnWriter.WriteShort((short)(Z * 32));
                spawnWriter.WriteByte((byte)((Yaw / 360) * 255));
                spawnWriter.WriteByte((byte)((Pitch / 360) * 255));
                spawnPacket.Payload = spawnWriter.ToArray();

                sendTasks.Add(p.Connection.SendAsync(spawnPacket));
            }

            try {
                await Task.WhenAll(sendTasks);
            } catch (Exception) { }
        }

        /// <summary>
        /// Spawns the player to the provided player.
        /// </summary>
        /// <param name="player"></param>
        public async void Spawn(Player player) {
            ClassicPacket spawnPacket = new ClassicPacket();
            spawnPacket.Id = PacketId.SpawnPlayer;

            PacketWriter spawnWriter = new PacketWriter();
            spawnWriter.WriteSByte(this == player ? (sbyte)-1 : ID);
            spawnWriter.WriteString(Name);
            spawnWriter.WriteShort((short)(X * 32));
            spawnWriter.WriteShort((short)(Y * 32));
            spawnWriter.WriteShort((short)(Z * 32));
            spawnWriter.WriteByte((byte)((Yaw / 360) * 255));
            spawnWriter.WriteByte((byte)((Pitch / 360) * 255));
            spawnPacket.Payload = spawnWriter.ToArray();

            try {
                await player.Connection.SendAsync(spawnPacket);
            } catch (Exception) { }
        }

        /// <summary>
        /// Despawns the player to the provided player.
        /// </summary>
        /// <param name="player"></param>
        public async void Despawn(Player player) {
            ClassicPacket spawnPacket = new ClassicPacket();
            spawnPacket.Id = PacketId.DespawnPlayer;

            PacketWriter spawnWriter = new PacketWriter();
            spawnWriter.WriteSByte(this == player ? (sbyte)-1 : ID);

            try {
                await player.Connection.SendAsync(spawnPacket);
            } catch (Exception) { }
        }

        /// <summary>
        /// Despawns the player.
        /// </summary>
        public async void Despawn() {
            if (!_spawned)
                return;

            _spawned = false;

            // send packet
            List<Task> sendTasks = new List<Task>();

            foreach (Player p in _world.Players) {
                ClassicPacket spawnPacket = new ClassicPacket();
                spawnPacket.Id = PacketId.DespawnPlayer;

                PacketWriter spawnWriter = new PacketWriter();
                spawnWriter.WriteSByte(this == p ? (sbyte)-1 : ID);

                sendTasks.Add(p.Connection.SendAsync(spawnPacket));
            }

            try {
                await Task.WhenAll(sendTasks);
            } catch (Exception) { }
        }

        public void SetUserLevel(byte level) {

        }

        public void Teleport(float x, float y, float z) {
            UpdatePosition(x, y, z, _yaw, _pitch, true);
        }

        public async void Message(sbyte id, string msg) {
            ClassicPacket packet = new ClassicPacket();
            packet.Id = PacketId.Message;

            PacketWriter writer = new PacketWriter();
            writer.WriteSByte(id);
            writer.WriteString(msg);
            packet.Payload = writer.ToArray();

            try {
                await _connection.SendAsync(packet);
            } catch (Exception) {
            }
        }

        public async void Kick(string reason) {
            // validate reason length
            if (reason.Length > 64)
                throw new ArgumentException("The reason cannot be longer than 64 characters");

            // send disconnect packet
            ClassicPacket packet = new ClassicPacket();
            packet.Id = PacketId.DisconnectPlayer;

            PacketWriter writer = new PacketWriter();
            writer.WriteString(reason);
            packet.Payload = writer.ToArray();

            await _connection.SendAsync(packet);

            // close connection
            _connection.Dispose();

            // remove from world
            if (_id > -1)
                _world.RemovePlayer(this);
        }

        public async void UpdatePosition(float x, float y, float z, float yaw, float pitch, bool updateSelf) {
            // update values
            float newX = Math.Clamp(x, 0, _world.Width);
            float newY = Math.Clamp(y, 0, _world.Height);
            float newZ = Math.Clamp(z, 0, _world.Depth);
            float newYaw = Math.Clamp(yaw, 0, 360);
            float newPitch = Math.Clamp(pitch, 0, 360);

            // calculate delta
            float deltaX = newX - _x;
            float deltaY = newY - _y;
            float deltaZ = newZ - _z;
            float deltaYaw = newYaw - _yaw;
            float deltaPitch = newPitch - _pitch;

            if (deltaX + deltaY + deltaZ + deltaYaw + deltaPitch == 0)
                return;

            // set values
            _x = newX;
            _y = newY;
            _z = newZ;
            _yaw = newYaw;
            _pitch = newPitch;

            // update other players
            List<Task> updateTasks = new List<Task>();

            foreach (Player p in _world.Players) {
                if (updateSelf || p != this) {
                    // determine what packet we need
                    if ((deltaX < -4 || deltaX > 4) || (deltaY < -4 || deltaY> 4 ) || (deltaZ < -4 || deltaZ > 4)) {
                        ClassicPacket posPacket = new ClassicPacket();
                        posPacket.Id = PacketId.PositionAngle;

                        PacketWriter writer = new PacketWriter();
                        writer.WriteSByte(ID);
                        writer.WriteShort((short)(_x * 32));
                        writer.WriteShort((short)(_y * 32));
                        writer.WriteShort((short)(_z * 32));
                        writer.WriteByte((byte)((_yaw / 360) * 255));
                        writer.WriteByte((byte)((_pitch / 360) * 255));
                        posPacket.Payload = writer.ToArray();

                        updateTasks.Add(p.Connection.SendAsync(posPacket));
                    } else {
                        ClassicPacket posUpdatePacket = new ClassicPacket();
                        posUpdatePacket.Id = PacketId.PositionAngleUpdate;

                        PacketWriter writer = new PacketWriter();
                        writer.WriteSByte(ID);;
                        writer.WriteSByte((sbyte)(deltaX * 32));
                        writer.WriteSByte((sbyte)(deltaY * 32));
                        writer.WriteSByte((sbyte)(deltaZ * 32));
                        writer.WriteByte((byte)((_yaw / 360) * 255));
                        writer.WriteByte((byte)((_pitch / 360) * 255));
                        posUpdatePacket.Payload = writer.ToArray();

                        updateTasks.Add(p.Connection.SendAsync(posUpdatePacket));
                    }
                }
            }

            try {
                await Task.WhenAll(updateTasks);
            } catch (Exception) { }
        }

        public async void ConnLoop() {
            while (true) {
                ClassicPacket packet = null;

                try {
                    packet = await _connection.ReceiveAsync();
                } catch(Exception) {
                    return;
                }

                switch(packet.Id) {
                    case PacketId.PositionAngle:
                        PacketReader posAngReader = new PacketReader(packet.Payload);
                        posAngReader.ReadByte();

                        // update position
                        UpdatePosition(posAngReader.ReadShort() / 32.0f, posAngReader.ReadShort() / 32.0f, posAngReader.ReadShort() / 32.0f, (posAngReader.ReadByte() / 255.0f) * 360.0f, (posAngReader.ReadByte() / 255.0f) * 360.0f, false);
                        break;

                    case PacketId.Message:
                        PacketReader msgReader = new PacketReader(packet.Payload);
                        msgReader.ReadByte();
                        string msgText = msgReader.ReadString();

                        if (msgText.StartsWith('/')) {
                            if (msgText.StartsWith("/clear", StringComparison.CurrentCultureIgnoreCase)) {
                                _world.ClearText();
                            } else if (msgText.StartsWith("/text ", StringComparison.CurrentCultureIgnoreCase)) {
                                _world.ClearText();
                                _world.DrawText(msgText.Substring(6));
                            } else if (msgText.StartsWith("/texta ", StringComparison.CurrentCultureIgnoreCase)) {
                                _world.ClearText();
                                _world.DrawTextAnimated(msgText.Substring(7));
                            } else if (msgText.StartsWith("/load", StringComparison.CurrentCultureIgnoreCase)) {
                                _world.Load();
                            } else if (msgText.StartsWith("/save", StringComparison.CurrentCultureIgnoreCase)) {
                                _world.Save();
                            } else {
                                Message(-1, "Unknown command");
                            }
                        } else {
                            foreach (Player p in _world.Players)
                                p.Message(p == this ? (sbyte)-1 : ID, string.Format("{0}: {1}", Name, msgText));
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
                            _world.SetBlock(askX, askY, askZ, 0);
                        } else {
                            _world.SetBlock(askX, askY, askZ, askBlockType);
                        }
                        break;

                    default:
                        Console.WriteLine("Unknown packet: {0}", packet.Id.ToString());
                        break;
                }
            }
        }
        #endregion

        #region Constructors
        public Player(World world, sbyte id, ClassicConnection connection, string name) {
            _world = world;
            _name = name;
            _id = id;
            _connection = connection;
        }
        #endregion
    }
}
