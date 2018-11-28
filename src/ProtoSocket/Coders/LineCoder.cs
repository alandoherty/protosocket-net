using System;
using System.Collections.Generic;
using System.Text;

namespace ProtoSocket.Coders
{
    /// <summary>
    /// Provides a pre-built coder which can create frames based off UTF-8 strings seperated by newlines.
    /// </summary>
    public abstract class LineCoder<TFrame>
    {
        /// <summary>
        /// Gets or sets the new line string.
        /// </summary>
        public string NewLine { get; set; } = Environment.NewLine;

        /// <summary>
        /// Gets or sets the encoding.
        /// </summary>
        public Encoding Encoding { get; set; } = Encoding.UTF8;

        /// <summary>
        /// Create a frame from the received line.
        /// </summary>
        /// <param name="line">The line.</param>
        /// <returns>The frame.</returns>
        protected abstract TFrame ToFrame(string line);
    }
}
