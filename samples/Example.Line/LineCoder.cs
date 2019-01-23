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
        public bool Read(PipeReader reader, CoderContext<string> ctx, out string frame) {
            if (reader.TryRead(out ReadResult result) && !result.IsCompleted) {
                // create array (not the most efficient way we could do this)
                byte[] arr = result.Buffer.ToArray();

                // set frame
                frame = Encoding.UTF8.GetString(arr);

                // advance
                reader.AdvanceTo(result.Buffer.End, result.Buffer.End);
                return true;
            }

            // we didn't find a frame
            frame = null;
            return false;
        }

        public void Reset() {
        }

        public Task WriteAsync(Stream stream, string frame, CoderContext<string> ctx, CancellationToken cancellationToken) {
            byte[] arr = Encoding.UTF8.GetBytes(frame);
            return stream.WriteAsync(arr, 0, arr.Length);
        }
    }
}
