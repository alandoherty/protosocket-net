using System;
using System.Collections.Generic;
using System.Text;

namespace ProtoSocket
{
    /// <summary>
    /// Defines the possible coder types.
    /// </summary>
    public enum ProtocolCoderType
    {
        /// <summary>
        /// If the coder writes and reads to streams.
        /// </summary>
        Stream,

        /// <summary>
        /// If the coder writes to streams but uses pipelines for reading.
        /// </summary>
        Pipeline
    }
}
