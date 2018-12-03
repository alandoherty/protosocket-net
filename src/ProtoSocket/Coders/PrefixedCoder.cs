using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProtoSocket.Coders
{
    //todo

    /// <summary>
    /// Provides a pre-built coder which can create frames based off length prefixed payloads.
    /// </summary>
    abstract class PrefixedCoder<TFrame> : IProtocolCoder<TFrame>
        where TFrame: class
    {
        #region Fields
        private int _prefixByteCount;

        private ReadState _state;

        private byte[] _frameLength;
        private byte[] _framePayload;
        private int _framePayloadOffset;
        #endregion

        #region Properties
        /// <summary>
        /// Gets the maximum frame size.
        /// </summary> 
        public abstract ulong MaximumSize { get; set; }
        #endregion

        #region Methods
        /// <summary>
        /// Reads the prefixed frame from the pipe.
        /// </summary>
        /// <param name="reader">The pipe reader.</param>
        /// <param name="ctx">The coder context.</param>
        /// <param name="frame">The frames.</param>
        /// <returns></returns>
        public bool Read(PipeReader reader, CoderContext<TFrame> ctx, out TFrame frame) {
            while (reader.TryRead(out ReadResult result)) {
                // check if the pipe is completed
                if (result.IsCompleted)
                    break;

                // get the sequence buffer
                ReadOnlySequence<byte> buffer = result.Buffer;

                if (_state == ReadState.Prefix) {
                    // read in the prefix and setup the payload state
                    //_frameLength = (PacketId)buffer.First.Span[0];
                    //_framePayload = new byte[PacketSizes[_packetId]];
                    _state = ReadState.Payload;

                    // advance by one byte
                    reader.AdvanceTo(buffer.GetPosition(1));
                } else if (_state == ReadState.Payload) {
                    int advancedBytes = 0;

                    try {
                        foreach (ReadOnlyMemory<byte> seq in buffer) {
                            // calculate the amount of needed data to complete this packet
                            int neededBytes = _framePayload.Length - _framePayloadOffset;

                            // calculate how much of this buffer we should copy, if the buffer is smaller than the amount we need we take the full buffer
                            // otherwise we take as much as we can
                            int copyBytes = Math.Min(seq.Length, neededBytes);

                            // copy in the data we can get into the frame payload
                            seq.Span.Slice(0, copyBytes).
                                CopyTo(new Span<byte>(_framePayload, _framePayloadOffset, copyBytes));

                            // increment the amount we were able to copy in
                            advancedBytes += copyBytes;
                            _framePayloadOffset += copyBytes;

                            // if we have a complete frame, return it
                            if (_framePayloadOffset == _framePayload.Length) {
                                // reset the state
                                Reset();

                                frame = ToFrame(_framePayload);
                                return true;
                            }
                        }
                    } finally {
                        reader.AdvanceTo(buffer.GetPosition(advancedBytes));
                    }
                }
            }

            // we didn't get any complete frames in this read
            frame = null;
            return false;
        }

        /// <summary>
        /// Resets the coder state.
        /// </summary>
        public void Reset() {
            _state = ReadState.Prefix;
            _frameLength = new byte[8];
            _framePayload = null;
            _framePayloadOffset = 0;
        }

        /// <summary>
        /// Writes the frame to the buffer asyncronously.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <param name="frame">The frame.</param>
        /// <param name="ctx">The coder context.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        public async Task WriteAsync(Stream stream, TFrame frame, CoderContext<TFrame> ctx, CancellationToken cancellationToken) {
            // get payload
            byte[] payload = FromFrame(frame);
            byte[] prefixBytes = BitConverter.GetBytes((long)payload.Length);

            if (payload.Length > 8192) {
                // write asyncronously
                await stream.WriteAsync(prefixBytes, 0, _prefixByteCount, cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
            } else {
                // rent a combined buffer, since we could save effort by using one write call
                int combinedLength = payload.Length + _prefixByteCount;
                byte[] combinedBuffer = ArrayPool<byte>.Shared.Rent(combinedLength);

                try {
                    // copy data into the buffer
                    Buffer.BlockCopy(prefixBytes, 0, combinedBuffer, 0, _prefixByteCount);
                    Buffer.BlockCopy(payload, 0, combinedBuffer, _prefixByteCount, payload.Length);

                    // write asyncronously
                    await stream.WriteAsync(combinedBuffer, 0, combinedLength, cancellationToken).ConfigureAwait(false);
                } finally {
                    ArrayPool<byte>.Shared.Return(combinedBuffer);
                }
            }
        }
        #endregion

        #region Abstract Methods
        /// <summary>
        /// Create a frame from the received payload.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <returns>The frame.</returns>
        protected abstract TFrame ToFrame(byte[] frame);

        /// <summary>
        /// Creates a payload from the frame.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <returns>The payload span.</returns>
        protected abstract byte[] FromFrame(TFrame frame);
        #endregion

        #region Enums
        /// <summary>
        /// Defines the possible read states.
        /// </summary>
        enum ReadState
        {
            Prefix,
            Payload
        }
        #endregion

        #region Constructors
        /// <summary>
        /// Creates a new prefixed coder.
        /// </summary>
        /// <param name="prefixBytes">The number of bytes in the prefix.</param>
        public PrefixedCoder(int prefixBytes) {
            if ((prefixBytes < 1 || prefixBytes > 8))
                throw new ArgumentOutOfRangeException("The prefix bytes must be between one and eight");

            _prefixByteCount = prefixBytes;
            MaximumSize = 2 ^ (ulong)(prefixBytes * 8);
        }
        #endregion
    }
}
