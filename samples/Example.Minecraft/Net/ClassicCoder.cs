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
        private PacketId _packetId;
        private byte[] _packetPayload;
        private int _packetPayloadOffset;

        /// <summary>
        /// Resets the coder state.
        /// </summary>
        public void Reset() {
            _state = ReadState.PacketId;
            _packetPayloadOffset = 0;
            _packetPayload = null;
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
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        public async Task WriteAsync(Stream stream, ClassicPacket frame, CoderContext<ClassicPacket> ctx, CancellationToken cancellationToken) {
            // rent a combined buffer
            int combinedLength = frame.Payload.Length + 1;
            byte[] combinedBuffer = ArrayPool<byte>.Shared.Rent(combinedLength);

            try {
                // copy data into the buffer
                combinedBuffer[0] = (byte)frame.Id;
                Buffer.BlockCopy(frame.Payload, 0, combinedBuffer, 1, frame.Payload.Length);

                // write asyncronously
                await stream.WriteAsync(combinedBuffer, 0, combinedLength, cancellationToken).ConfigureAwait(false);
            } finally {
                ArrayPool<byte>.Shared.Return(combinedBuffer);
            }
        }

        public bool Read(PipeReader reader, out IEnumerable<ClassicPacket> frames) {
            while(reader.TryRead(out ReadResult result)) {
                // check if the pipe is completed
                if (result.IsCompleted)
                    break;

                // get the sequence buffer
                ReadOnlySequence<byte> buffer = result.Buffer;

                if (_state == ReadState.PacketId) {
                    // read in the packet id and setup the payload state
                    _packetId = (PacketId)buffer.First.Span[0];
                    _packetPayload = new byte[PacketSizes[_packetId]];
                    _state = ReadState.Payload;

                    // advance by one byte
                    reader.AdvanceTo(buffer.GetPosition(1));
                } else if (_state == ReadState.Payload) {
                    int advancedBytes = 0;

                    try {
                        foreach (ReadOnlyMemory<byte> seq in buffer) {
                            // calculate the amount of needed data to complete this packet
                            int neededBytes = _packetPayload.Length - _packetPayloadOffset;

                            // calculate how much of this buffer we should copy, if the buffer is smaller than the amount we need we take the full buffer
                            // otherwise we take as much as we can
                            int copyBytes = Math.Min(seq.Length, neededBytes);

                            // copy in the data we can get into the frame payload
                            seq.Span.Slice(0, copyBytes).
                                CopyTo(new Span<byte>(_packetPayload, _packetPayloadOffset, copyBytes));

                            // increment the amount we were able to copy in
                            advancedBytes += copyBytes;
                            _packetPayloadOffset += copyBytes;

                            // if we have a complete frame, return it
                            if (_packetPayloadOffset == _packetPayload.Length) {
                                // output the frames
                                frames = new ClassicPacket[] { new ClassicPacket() { Id = _packetId, Payload = _packetPayload } };

                                // reset the state
                                Reset();

                                return true;
                            }
                        }
                    } finally {
                        reader.AdvanceTo(buffer.GetPosition(advancedBytes));
                    }
                }
            }

            // we didn't get any complete frames in this read
            frames = Enumerable.Empty<ClassicPacket>();
            return false;
        }
    }
}
