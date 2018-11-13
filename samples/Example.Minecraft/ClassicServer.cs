using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using ProtoSocket;

namespace Example.Minecraft
{
    /// <summary>
    /// Provides functionality for a classic server.
    /// </summary>
    public class ClassicServer : ProtocolServer<ClassicConnection, ClassicPacket>
    {
        #region Fields
        private World _world;
        #endregion

        #region Methods
        protected override void OnDisconnected(object sender, PeerDisconnectedEventArgs<ClassicPacket> e) {
            // find player
            Player player = _world.FindPlayer((ClassicConnection)e.Peer);

            if (player == null)
                return;

            // remove player
            _world.RemovePlayer(player);

            foreach(Player p in _world.Players) {
                p.Message(-1, player.Name + " disconnected");
            }

            Console.WriteLine(e.Peer.RemoteEndPoint + " disconnected due to " + e.Peer.CloseReason);
        }

        protected async override void OnConnected(object sender, PeerConnectedEventArgs<ClassicPacket> e) {
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
            } catch(Exception) {
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
            sbyte playerId = _world.NextPlayerID();

            // create player
            Player player = new Player(_world, _world.NextPlayerID(), (ClassicConnection)e.Peer, username);
            
            if (playerId == -1) {
                player.Kick("Maximum players reached");
                return;
            }

            // get level
            byte[] compressedLevel = _world.GetCompressedLevel();

            // send initialize packet
            ClassicPacket initPacket = new ClassicPacket();
            initPacket.Id = PacketId.LevelInitialize;
            initPacket.Payload = new byte[0];
            await e.Peer.SendAsync(initPacket);

            // build the data
            int bytesSent = 0;

            while(bytesSent < compressedLevel.Length) {
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
            finishWriter.WriteShort((short)_world.Width);
            finishWriter.WriteShort((short)_world.Height);
            finishWriter.WriteShort((short)_world.Depth);
            finishPacket.Payload = finishWriter.ToArray();
            await e.Peer.SendAsync(finishPacket);

            // add player and spawn
            _world.AddPlayer(player);
            player.ConnLoop();
            player.Spawn();

            // announce
            foreach (Player p in _world.Players) {
                if (p != player)
                    p.Message(-1, player.Name + " connected");
            }

            // spawn existing players
            foreach(Player p in _world.Players) {
                p.Spawn(player);
            }
        }
        #endregion

        #region Constructors
        public ClassicServer(World world, Uri uri) : base (new ClassicCoder()) {
            _world = world;
            Configure(uri);
        }
        #endregion
    }
}
