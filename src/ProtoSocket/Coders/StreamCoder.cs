using System;
using System.Collections.Generic;
using System.Text;

namespace ProtoSocket.Coders
{
    /// <summary>
    /// Provides a pre-built coder which create frames as soon as data becomes available.
    /// </summary>
    public abstract class StreamCoder<TFrame>
    {
        /// <summary>
        /// Create a frame from the received data.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <returns>The frame.</returns>
        protected abstract TFrame ToFrame(byte[] frame);
    }
}
