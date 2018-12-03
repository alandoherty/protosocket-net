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
        private int _messageLength;

        public bool Read(PipeReader reader, CoderContext<ChatMessage> ctx, out ChatMessage frame) {
            while (reader.TryRead(out ReadResult result)) {
                // check if the pipe is completed
                if (result.IsCompleted)
                    break;

                // get the sequence buffer
                ReadOnlySequence<byte> buffer = result.Buffer;

                try {
                    while (buffer.Length > 0) {
                        if (_state == ReadState.Length) {
                            if (buffer.Length >= 2) {
                                // to array
                                byte[] messageLengthBytes = buffer.Slice(0, 2).ToArray();
                                _messageLength = BitConverter.ToInt16(messageLengthBytes, 0);

                                // increment the amount we were able to copy in
                                buffer = buffer.Slice(2);
                                _state = ReadState.Message;
                            }
                        } else if (_state == ReadState.Message) {
                            if (buffer.Length >= _messageLength) {
                                // to array
                                byte[] messagePayload = buffer.Slice(0, _messageLength).ToArray();

                                // increment the amount we were able to copy in
                                buffer = buffer.Slice(_messageLength);

                                // output the frames
                                using (MemoryStream ms = new MemoryStream(messagePayload)) {
                                    BinaryReader msReader = new BinaryReader(ms);

                                    string text = msReader.ReadString();
                                    string name = msReader.ReadString();

                                    // output the frames
                                    frame = new ChatMessage() {
                                        Text = text,
                                        Name = name
                                    };
                                }

                                // reset the state
                                Reset();
                                return true;
                            } else {
                                break;
                            }
                        }
                    }
                } finally {
                    reader.AdvanceTo(buffer.GetPosition(0));
                }
            }

            // we didn't find a frame
            frame = null;
            return false;
        }

        public void Reset() {
            _state = ReadState.Length;
            _messageLength = 0;
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
