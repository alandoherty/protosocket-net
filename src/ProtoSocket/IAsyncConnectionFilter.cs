using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProtoSocket
{
    /// <summary>
    /// Represents a connection filter which should be ran asyncronously.
    /// </summary>
    public interface IAsyncConnectionFilter : IConnectionFilter
    {
        /// <summary>
        /// Filter the incoming connection.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="cancellation">The cancellation token.</param>
        /// <returns>The task to determine if to accept the connection.</returns>
        Task<bool> FilterAsync(TcpClient client, CancellationToken cancellation);
    }
}
