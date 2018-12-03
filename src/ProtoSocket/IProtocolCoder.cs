﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
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
        /// Resets the coder to it's initial state.
        /// </summary>
        /// <remarks>This is not called initially, you should call this inside your constructor.</remarks>
        void Reset();
        
        /// <summary>
        /// Processes the input and optionally return one or more frames.
        /// </summary>
        /// <param name="reader">The reader.</param>
        /// <param name="ctx">The coder context.</param>
        /// <param name="frames">The optional output frames.</param>
        /// <returns>If any frames were outputted.</returns>
        bool Read(PipeReader reader, CoderContext<TFrame> ctx, out IEnumerable<TFrame> frames);

        /// <summary>
        /// Write the frame to the stream asyncronously.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <param name="frame">The frame object.</param>
        /// <param name="ctx">The coder context.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        Task WriteAsync(Stream stream, TFrame frame, CoderContext<TFrame> ctx, CancellationToken cancellationToken);
    }
}
