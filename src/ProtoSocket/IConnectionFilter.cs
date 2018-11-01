using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

namespace ProtoSocket
{
    /// <summary>
    /// Represents a connection filter.
    /// </summary>
    public interface IConnectionFilter
    {
        /// <summary>
        /// Filter the incoming connection, this operation must not block.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <returns>If to accept the connection.</returns>
        bool Filter(TcpClient client);
    }
}
