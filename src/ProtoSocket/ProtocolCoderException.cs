using System;
using System.Collections.Generic;
using System.Text;

namespace ProtoSocket
{
    /// <summary>
    /// Represents an error during encoding/decoding a frame.
    /// </summary>
    public class ProtocolCoderException : Exception
    {
        #region Constructors
        /// <summary>
        /// Creates a new protocol coder exception.
        /// </summary>
        public ProtocolCoderException() {
        }

        /// <summary>
        /// Creates a new protocol coder exception with a message.
        /// </summary>
        /// <param name="message">The message.</param>
        public ProtocolCoderException(string message) : base(message) {
        }

        /// <summary>
        /// Creates a new protocol coder exception with a message and inner exception.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="innerException">The inner exception.</param>
        public ProtocolCoderException(string message, Exception innerException) : base(message, innerException) {
        }
        #endregion
    }
}
