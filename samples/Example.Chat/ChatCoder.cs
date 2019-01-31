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

namespace Example.Chat
{
    /// <summary>
    /// Encodes/decodes chat messages.
    /// </summary>
    public class ChatCoder : IProtocolCoder<ChatFrame>
    {
        private ReadState _state;
        private byte[] _messagePayload;
        private int _messageOffset;

        public bool Read(PipeReader reader, CoderContext<ChatFrame> ctx, out ChatFrame frame) {
            if (reader.TryRead(out ReadResult result) && !result.IsCompleted) {
                // get the sequence buffer
                ReadOnlySequence<byte> buffer = result.Buffer;

                try {
                    while (buffer.Length > 0) {
                        if (_state == ReadState.Length) {
                            if (buffer.Length >= 2) {
                                // to array
                                byte[] messageLengthBytes = buffer.Slice(0, 2).ToArray();
                                _messagePayload = new byte[BitConverter.ToUInt16(messageLengthBytes, 0)];
                                _messageOffset = 0;

                                // increment the amount we were able to copy in
                                buffer = buffer.Slice(2);
                                _state = ReadState.Message;
                            } else {
                                break;
                            }
                        } else if (_state == ReadState.Message) {
                            // copy as much as possible
                            int numSliceBytes = Math.Min((int)buffer.Length, _messagePayload.Length - _messageOffset);
                          
                            // copy in array, increment offset and set new buffer position
                            buffer.Slice(0, numSliceBytes).CopyTo(new Span<byte>(_messagePayload, _messageOffset, numSliceBytes));
                            _messageOffset += numSliceBytes;
                            buffer = buffer.Slice(numSliceBytes);

                            if (_messageOffset == _messagePayload.Length) {
                                // output the frames
                                using (MemoryStream ms = new MemoryStream(_messagePayload)) {
                                    BinaryReader msReader = new BinaryReader(ms);

                                    ushort textLength = msReader.ReadUInt16();
                                    byte[] textBytes = msReader.ReadBytes(textLength);
                                    byte nameLength = msReader.ReadByte();
                                    byte[] nameBytes = msReader.ReadBytes(nameLength);

                                    // output the frames
                                    frame = new ChatFrame() {
                                        Message = Encoding.UTF8.GetString(textBytes),
                                        Name = Encoding.UTF8.GetString(nameBytes)
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
                    reader.AdvanceTo(buffer.GetPosition(0), buffer.End);
                }
            }

            // we didn't find a frame
            frame = default(ChatFrame);
            return false;
        }

        public void Reset() {
            _state = ReadState.Length;
            _messagePayload = null;
            _messageOffset = 0;
        }

        /// <summary>
        /// Defines the internal read states.
        /// </summary>
        enum ReadState
        {
            Length,
            Message
        }

        public void Write(Stream stream, ChatFrame frame, CoderContext<ChatFrame> ctx) {
            byte[] nameBytes = string.IsNullOrEmpty(frame.Name) ? new byte[0] : Encoding.UTF8.GetBytes(frame.Name);
            byte[] messageBytes = Encoding.UTF8.GetBytes(frame.Message);

            BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, true);
            writer.Write((ushort)(3 + nameBytes.Length + messageBytes.Length));
            writer.Write((ushort)messageBytes.Length);
            writer.Write(messageBytes);
            writer.Write((byte)nameBytes.Length);
            writer.Write(nameBytes);
        }
    }
}
