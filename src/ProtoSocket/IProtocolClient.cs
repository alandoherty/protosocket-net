using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProtoSocket
{
    /// <summary>
    /// Defines a base interface for protocol clients.
    /// </summary>
    public interface IProtocolClient : IProtocolPeer
    {
        /// <summary>
        /// Connects to the provided host and port.
        /// </summary>
        /// <param name="host">The host.</param>
        /// <param name="port">The port.</param>
        /// <returns></returns>
        Task ConnectAsync(string host, int port);

        /// <summary>
        /// Connects to the provided URI, only supports tcp:// scheme currently.
        /// </summary>
        /// <param name="uri">The uri.</param>
        /// <returns></returns>
        Task ConnectAsync(Uri uri);
    }
}
