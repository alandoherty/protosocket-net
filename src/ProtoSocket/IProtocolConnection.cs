using System;
using System.Collections.Generic;
using System.Text;

namespace ProtoSocket
{
    /// <summary>
    /// Defines the base interface for protocol connections.
    /// </summary>
    public interface IProtocolConnection
    {
        /// <summary>
        /// Gets the server this connection was created on.
        /// </summary>
        IProtocolServer Server { get; }
    }
}
