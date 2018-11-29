using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Example.Minecraft.Net.Packets;
using ProtoSocket;
using ProtoSocket.Extensions;

namespace Example.Minecraft.Net
{
    class ClassicCoder : IProtocolCoder<ClassicPacket>
    {
        /// <summary>
        /// Defines the packet sizes.
        /// </summary>
        private static readonly Dictionary<PacketId, int> PacketSizes = new Dictionary<PacketId, int>() {
            { PacketId.Identification, 130 },
            { PacketId.Ping, 0 },
            { PacketId.LevelInitialize, 0 },
            { PacketId.LevelDataChunk, 1027 },
            { PacketId.LevelFinalize, 0 },
            { PacketId.AskBlock, 8 },
            { PacketId.SetBlock, 7 },
            { PacketId.SpawnPlayer, 73 },
            { PacketId.PositionAngle, 9 },
            { PacketId.Message, 65 }
        };

        private ReadState _state;
        private byte _packetId;
        
        public async Task<ClassicPacket> ReadAsync(Stream stream, CoderContext<ClassicPacket> ctx, CancellationToken cancellationToken) {
            // read packet id
            byte[] idBuffer = new byte[1];

            if (await stream.ReadAsync(idBuffer, 0, 1).ConfigureAwait(false) == 0)
                return null;

            // validate packet id
            PacketId packetId = (PacketId)idBuffer[0];

            if (!Enum.IsDefined(typeof(PacketId), packetId))
                throw new InvalidDataException("The packet identifier is not supported");

            // packet handler
            int packetSize = PacketSizes[packetId];

            // read packet payload
            byte[] packetPayload = new byte[packetSize];

            if (await stream.ReadBlockAsync(packetPayload, 0, packetSize, cancellationToken).ConfigureAwait(false) == 0)
                return null;

            // create packet
            return new ClassicPacket() {
                Payload = packetPayload,
                Id = packetId
            };
        }

        public void Reset() {
            _state = ReadState.PacketId;
        }

        /// <summary>
        /// Defines the internal read states.
        /// </summary>
        enum ReadState
        {
            PacketId,
            Payload
        }

        public async Task WriteAsync(Stream stream, ClassicPacket frame, CoderContext<ClassicPacket> ctx, CancellationToken cancellationToken) {
            await stream.WriteAsync(new byte[] { (byte)frame.Id }, 0, 1).ConfigureAwait(false);
            await stream.WriteAsync(frame.Payload, 0, frame.Payload.Length).ConfigureAwait(false);
        }

        public bool Read(PipeReader reader, out IEnumerable<ClassicPacket> frames) {
            while(reader.TryRead(out ReadResult result)) {
                // get the sequence buffer
                ReadOnlySequence<byte> buffer = result.Buffer;

                if (_state == ReadState.PacketId) {
                }

                break;
            }

            frames = Enumerable.Empty<ClassicPacket>();
            return false;
        }
    }
}
