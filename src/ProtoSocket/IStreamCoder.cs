using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProtoSocket
{
    /// <summary>
    /// Represents the interface for stream based coders.
    /// </summary>
    public interface IStreamCoder<TFrame> : IProtocolCoder<TFrame>
         where TFrame : class
    {
        /// <summary>
        /// Reads the frame from the stream asyncronously.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <param name="ctx">The coder context.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The frame, or null if reached end of stream.</returns>
        Task<TFrame> ReadAsync(Stream stream, CoderContext<TFrame> ctx, CancellationToken cancellationToken);
    }
}
