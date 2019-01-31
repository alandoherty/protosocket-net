using ProtoSocket;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Example.Line
{
    /// <summary>
    /// Encodes/decodes strings.
    /// </summary>
    public class LineCoder : IProtocolCoder<string>
    {
        private byte[] _newlineBytes;
        private Encoding _encoding = Encoding.UTF8;

        /// <summary>
        /// Gets or sets the encoding.
        /// </summary>
        public Encoding Encoding {
            get {
                return _encoding;
            } set {
                string newlineStr = _encoding.GetString(_newlineBytes);
                _encoding = value;
                _newlineBytes = _encoding.GetBytes(newlineStr);
            }
        }

        /// <summary>
        /// Gets or sets the newline string.
        /// </summary>
        public string NewLine {
            get {
                return _encoding.GetString(_newlineBytes);
            }
            set {
                _newlineBytes = _encoding.GetBytes(value);
            }
        }

        bool IProtocolCoder<string>.Read(PipeReader reader, CoderContext<string> ctx, out string frame) {
            if (reader.TryRead(out ReadResult result) && !result.IsCompleted) {
                int matchedBytes = 0;
                int scannedBytes = 0;

                foreach(ReadOnlyMemory<byte> rom in result.Buffer) {
                    for (int i = 0; i < rom.Length; i++) {
                        if (rom.Span[i] == _newlineBytes[matchedBytes]) {
                            matchedBytes++;

                            if (matchedBytes == _newlineBytes.Length) {
                                result.Buffer.Slice(result.Buffer.GetPosition(0), result.Buffer.GetPosition(scannedBytes - matchedBytes));
                            }
                        } else {
                            matchedBytes = 0;
                        }

                        scannedBytes++;
                    }
                }

                // advance
                reader.AdvanceTo(result.Buffer.End, result.Buffer.End);
                frame = null;
                return true;
            }

            // we didn't find a frame
            frame = null;
            return false;
        }

        void IProtocolCoder<string>.Reset() {
        }

        void IProtocolCoder<string>.Write(Stream stream, string frame, CoderContext<string> ctx) {
            int byteCount = _encoding.GetByteCount(frame) + _newlineBytes.Length;
        }

        /// <summary>
        /// Creates a new line coder.
        /// </summary>
        public LineCoder() {
            NewLine = Environment.NewLine;
        }

        /// <summary>
        /// Creates a new line coder with the specified encoding.
        /// </summary>
        /// <param name="encoding">The encoding.</param>
        public LineCoder(Encoding encoding) {
            NewLine = Environment.NewLine;
            Encoding = encoding;
        }

        /// <summary>
        /// Creates a new line coder with the specified encoding and newline.
        /// </summary>
        /// <param name="encoding">The encoding.</param>
        /// <param name="newLine">The new line.</param>
        public LineCoder(Encoding encoding, string newLine) {
            NewLine = newLine;
            Encoding = encoding;
        }
    }
}
