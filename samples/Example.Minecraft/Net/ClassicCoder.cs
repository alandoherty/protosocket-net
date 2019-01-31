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
        private PacketId _packetId;
        private int _packetLength;

        /// <summary>
        /// Resets the coder state.
        /// </summary>
        public void Reset() {
            _state = ReadState.PacketId;
            _packetLength = 0;
        }

        /// <summary>
        /// Defines the internal read states.
        /// </summary>
        enum ReadState
        {
            PacketId,
            Payload
        }

        /// <summary>
        /// Writes the frame to the buffer asyncronously.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <param name="frame">The frame.</param>
        /// <param name="ctx">The coder context.</param>
        /// <returns></returns>
        public void Write(Stream stream, ClassicPacket frame, CoderContext<ClassicPacket> ctx) {
            stream.WriteByte((byte)frame.Id);
            stream.Write(frame.Payload, 0, frame.Payload.Length);
        }

        public bool Read(PipeReader reader, CoderContext<ClassicPacket> ctx, out ClassicPacket frame) {
            if (reader.TryRead(out ReadResult result) && !result.IsCompleted) {
                // get the sequence buffer
                ReadOnlySequence<byte> buffer = result.Buffer;

                try {
                    while (buffer.Length > 0) {
                        if (_state == ReadState.PacketId) {
                            // read in the packet id and setup the payload state
                            _packetId = (PacketId)buffer.First.Span[0];
                            _packetLength = PacketSizes[_packetId];
                            _state = ReadState.Payload;

                            // increment buffer
                            buffer = buffer.Slice(1);
                        } else if (_state == ReadState.Payload) {
                            if (buffer.Length >= _packetLength) {
                                // to array
                                byte[] packetPayload = buffer.Slice(0, _packetLength).ToArray();
                                
                                // increment the amount we were able to copy in
                                buffer = buffer.Slice(_packetLength);

                                // output the frames
                                frame = new ClassicPacket() { Id = _packetId, Payload = packetPayload };

                                // reset the state
                                Reset();
                                return true;
                            } else {
                                break;
                            }
                        }
                    }
                } finally {
                    reader.AdvanceTo(buffer.GetPosition(0), buffer.End);
                }
            }

            // we didn't find a frame
            frame = default;
            return false;
        }
    }
}
