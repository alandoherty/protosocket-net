using Example.Minecraft.Net;
using Example.Minecraft.Net.Packets;
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
        public void Spawn() {
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

                p.Connection.Queue(spawnPacket);
            }
        }

        /// <summary>
        /// Spawns the player to the provided player.
        /// </summary>
        /// <param name="player"></param>
        public void Spawn(Player player) {
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

            player.Connection.Queue(spawnPacket);
        }

        /// <summary>
        /// Despawns the player to the provided player.
        /// </summary>
        /// <param name="player"></param>
        public void Despawn(Player player) {
            ClassicPacket spawnPacket = new ClassicPacket();
            spawnPacket.Id = PacketId.DespawnPlayer;

            PacketWriter spawnWriter = new PacketWriter();
            spawnWriter.WriteSByte(this == player ? (sbyte)-1 : ID);
            
            player.Connection.Queue(spawnPacket);
        }

        /// <summary>
        /// Despawns the player.
        /// </summary>
        public void Despawn() {
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

                p.Connection.Queue(spawnPacket);
            }
        }

        public void SetUserLevel(byte level) {

        }

        public void Teleport(float x, float y, float z) {
            UpdatePosition(x, y, z, _yaw, _pitch, true);
        }

        public void Message(sbyte id, string msg) {
            ClassicPacket packet = new ClassicPacket();
            packet.Id = PacketId.Message;

            PacketWriter writer = new PacketWriter();
            writer.WriteSByte(id);
            writer.WriteString(msg);
            packet.Payload = writer.ToArray();
            
            _connection.Queue(packet);
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

            await _connection.SendAsync(packet).ConfigureAwait(false);

            // remove from world
            if (_id > -1)
                _world.RemovePlayer(this);
        }

        public void UpdatePosition(float x, float y, float z, float yaw, float pitch, bool updateSelf) {
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

                        p.Connection.Queue(posPacket);
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

                        p.Connection.Queue(posUpdatePacket);
                    }
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
