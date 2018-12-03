using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ProtoSocket;
using ProtoSocket.Extensions;

namespace Example.Chat
{
    /// <summary>
    /// Encodes/decodes chat messages.
    /// </summary>
    public class ChatCoder : IProtocolCoder<ChatMessage>
    {
        private ReadState _state;
        private byte[] _messageBytes;
        private int _messageBytesOffset;

        public bool Read(PipeReader reader, CoderContext<ChatMessage> ctx, out IEnumerable<ChatMessage> frames) {
            while (reader.TryRead(out ReadResult result)) {
                // check if the pipe is completed
                if (result.IsCompleted)
                    break;

                // get the sequence buffer
                ReadOnlySequence<byte> buffer = result.Buffer;

                if (_state == ReadState.Length) {
                    if (buffer.IsSingleSegment) {
                        if (buffer.First.Length >= 2) {
                            byte[] lengthBytes = buffer.First.Slice(0, 2).ToArray();
                            _messageBytes = new byte[BitConverter.ToInt16(lengthBytes, 0)];

                            // advance by one byte
                            reader.AdvanceTo(buffer.GetPosition(2));
                        } else {
                            break;
                        }
                    } else {
                        throw new NotSupportedException();
                    }
                } else if (_state == ReadState.Message) {
                    int advancedBytes = 0;

                    try {
                        foreach (ReadOnlyMemory<byte> seq in buffer) {
                            // calculate the amount of needed data to complete this packet
                            int neededBytes = _messageBytes.Length - _messageBytesOffset;

                            // calculate how much of this buffer we should copy, if the buffer is smaller than the amount we need we take the full buffer
                            // otherwise we take as much as we can
                            int copyBytes = Math.Min(seq.Length, neededBytes);

                            // copy in the data we can get into the frame payload
                            seq.Span.Slice(0, copyBytes).
                                CopyTo(new Span<byte>(_messageBytes, _messageBytesOffset, copyBytes));

                            // increment the amount we were able to copy in
                            advancedBytes += copyBytes;
                            _messageBytesOffset += copyBytes;

                            // if we have a complete frame, return it
                            if (_messageBytesOffset == _messageBytes.Length) {
                                using (MemoryStream ms = new MemoryStream(_messageBytes)) {
                                    BinaryReader msReader = new BinaryReader(ms);

                                    string text = msReader.ReadString();
                                    string name = msReader.ReadString();

                                    // output the frames
                                    frames = new ChatMessage[] { new ChatMessage() {
                                        Text = text,
                                        Name = name
                                    }};
                                }

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
            frames = Enumerable.Empty<ChatMessage>();
            return false;
        }

        public void Reset() {
            _state = ReadState.Length;
            _messageBytes = null;
            _messageBytesOffset = 0;
        }

        /// <summary>
        /// Defines the internal read states.
        /// </summary>
        enum ReadState
        {
            Length,
            Message
        }

        public async Task WriteAsync(Stream stream, ChatMessage frame, CoderContext<ChatMessage> ctx, CancellationToken cancellationToken) {
            using (MemoryStream ms = new MemoryStream()) {
                BinaryWriter writer = new BinaryWriter(ms);

                writer.Write((short)0);
                writer.Write(frame.Text);
                writer.Write(frame.Name);
                writer.Seek(0, SeekOrigin.Begin);
                writer.Write((short)(ms.Length - 2));

                await stream.WriteAsync(ms.ToArray(), 0, (int)ms.Length, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
