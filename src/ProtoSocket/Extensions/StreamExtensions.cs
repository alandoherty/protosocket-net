using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProtoSocket.Extensions
{
    /// <summary>
    /// Provides extensions to the stream class.
    /// </summary>
    public static class StreamExtensions
    {
        #region Extensions
        /// <summary>
        /// Reads a block of data asyncronously, unlike <see cref="Stream.ReadAsync"/> you are guarenteed to get all of the data. If you get less than the requested count the end of stream has been reached.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <param name="buffer">The buffer.</param>
        /// <param name="offset">The offset.</param>
        /// <param name="count">The count.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The number of bytes read.</returns>
        public static async Task<int> ReadBlockAsync(this Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
            int headerTotal = 0;

            while (headerTotal != buffer.Length) {
                int headerRead = await stream.ReadAsync(buffer, headerTotal, buffer.Length - headerTotal, cancellationToken).ConfigureAwait(false);

                if (headerRead == 0)
                    return headerTotal;
                else
                    headerTotal += headerRead;
            }

            return headerTotal;
        }
        #endregion
    }
}
