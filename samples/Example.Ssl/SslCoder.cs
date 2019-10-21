using ProtoSocket;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Text;

namespace Example.Ssl
{
    public class SslCoder : IProtocolCoder<string>
    {
        private byte[] _bytes;
        private int _bytesLength;
        private int _bytesOffset;
        private State _state;

        public bool Read(PipeReader reader, CoderContext<string> ctx, out string frame)
        {
            if (reader.TryRead(out ReadResult result) && !result.IsCompleted) {
                // get the sequence buffer
                ReadOnlySequence<byte> buffer = result.Buffer;

                try {
                    while (buffer.Length > 0) {
                        if (_state == State.Size) {
                            if (buffer.Length >= 2) {
                                // copy length from buffer
                                Span<byte> lengthBytes = stackalloc byte[2];

                                buffer.Slice(0, 2)
                                    .CopyTo(lengthBytes);

                                int length = BitConverter.ToUInt16(lengthBytes);

                                if (length > 32768)
                                    throw new ProtocolCoderException("The client sent an invalid frame length");

                                // increment the amount we were able to copy in
                                buffer = buffer.Slice(2);

                                // move state to content if we have a message with data
                                if (length > 0) {
                                    _bytes = ArrayPool<byte>.Shared.Rent(length);
                                    _bytesLength = length;
                                    _bytesOffset = 0;
                                    _state = State.Content;
                                } else {
                                    frame = string.Empty;
                                    return true;
                                }
                            } else {
                                break;
                            }
                        } else if (_state == State.Content) {
                            if (buffer.Length >= 1) {
                                // figure out how much data we can read, and how much we actually have to read
                                int remainingBytes = _bytesLength - _bytesOffset;
                                int maxBytes = Math.Min((int)buffer.Length, remainingBytes);

                                // copy into buffer
                                buffer.Slice(0, maxBytes)
                                    .CopyTo(_bytes.AsSpan(_bytesOffset, maxBytes));

                                // increment offset by amount we copied
                                _bytesOffset += maxBytes;
                                buffer = buffer.Slice(maxBytes);

                                // if we have filled the content array we can now produce a frame
                                if (_bytesOffset == _bytesLength) {
                                    try {
                                        frame = Encoding.UTF8.GetString(_bytes);
                                        _state = State.Size;
                                        return true;
                                    } finally {
                                        ArrayPool<byte>.Shared.Return(_bytes);
                                        _bytes = null;
                                    }
                                }
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

        public void Reset()
        {
            throw new NotSupportedException();
        }

        public void Write(Stream stream, string frame, CoderContext<string> ctx)
        {
            // encode the frame into a UTF8 byte array
            byte[] frameBytes = Encoding.UTF8.GetBytes(frame);

            if (frameBytes.Length > 32768)
                throw new ProtocolCoderException("The frame is too large to write");

            // encode the length
            byte[] lengthBytes = BitConverter.GetBytes((ushort)frame.Length);

            // write to stream
            stream.Write(lengthBytes);
            stream.Write(frameBytes);
        }

        /// <summary>
        /// Defines the states for this coder.
        /// </summary>
        enum State
        {
            Size,
            Content
        }

        ~SslCoder()
        {
            // return the array back to the pool if we deconstruct before finishing the entire frame
            if (_bytes != null)
                ArrayPool<byte>.Shared.Return(_bytes);
        }
    }
}
