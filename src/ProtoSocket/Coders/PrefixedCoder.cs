using System;
using System.Collections.Generic;
using System.Text;

namespace ProtoSocket.Coders
{
    /// <summary>
    /// Provides a pre-built coder which can create frames based off length prefixed payloads.
    /// </summary>
    public abstract class PrefixedCoder<TFrame>
    {
        /// <summary>
        /// Gets or sets the maximum frame size.
        /// </summary> 
        public int MaximumSize { get; set; } = int.MaxValue;

        /// <summary>
        /// Create a frame from the received payload.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <returns>The frame.</returns>
        protected abstract TFrame ToFrame(byte[] frame);
    }
}
