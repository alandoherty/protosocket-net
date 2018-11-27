using System;
using System.Collections.Generic;
using System.IO;
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
    public class ChatCoder : IStreamCoder<ChatMessage>
    {
        public ProtocolCoderType Type {
            get {
                return ProtocolCoderType.Stream;
            }
        }

        public async Task<ChatMessage> ReadAsync(Stream stream, CoderContext<ChatMessage> ctx, CancellationToken cancellationToken) {
            byte[] msgSize = new byte[2];

            // read size
            if (await stream.ReadBlockAsync(msgSize, 0, 2, cancellationToken) != 2)
                return null;

            byte[] msgBytes = new byte[BitConverter.ToInt16(msgSize, 0)];

            if (await stream.ReadBlockAsync(msgBytes, 0, msgBytes.Length, cancellationToken) != msgBytes.Length)
                return null;

            using (MemoryStream ms = new MemoryStream(msgBytes)) {
                BinaryReader reader = new BinaryReader(ms);

                string text = reader.ReadString();
                string name = reader.ReadString();

                return new ChatMessage() {
                    Text = text,
                    Name = name
                };
            }
        }

        public async Task WriteAsync(Stream stream, ChatMessage frame, CoderContext<ChatMessage> ctx, CancellationToken cancellationToken) {
            using (MemoryStream ms = new MemoryStream()) {
                BinaryWriter writer = new BinaryWriter(ms);

                writer.Write((short)0);
                writer.Write(frame.Text);
                writer.Write(frame.Name);
                writer.Seek(0, SeekOrigin.Begin);
                writer.Write((short)(ms.Length - 2));

                await stream.WriteAsync(ms.ToArray(), 0, (int)ms.Length, cancellationToken);
            }
        }
    }
}
