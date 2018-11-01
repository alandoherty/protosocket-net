using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProtoSocket
{
    /// <summary>
    /// Represents an interface for encoding and decoding. The implementation must be stateless.
    /// </summary>
    public interface IProtocolCoder<TFrame>
        where TFrame : class
    {
        /// <summary>
        /// Write the frame to the stream asyncronously.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <param name="frame">The frame object.</param>
        /// <param name="ctx">The coder context.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        Task WriteAsync(Stream stream, TFrame frame, CoderContext<TFrame> ctx, CancellationToken cancellationToken);
        
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
