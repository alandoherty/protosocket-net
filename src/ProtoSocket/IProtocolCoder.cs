using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProtoSocket
{
    /// <summary>
    /// Represents the base interface for encoding and decoding.
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
        /// Gets the coder type.
        /// </summary>
        ProtocolCoderType Type { get; }
    }
}
