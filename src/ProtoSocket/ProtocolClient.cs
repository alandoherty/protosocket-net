using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProtoSocket
{
    /// <summary>
    /// Provides functionality for connecting to protocol servers.
    /// </summary>
    /// <typeparam name="TFrame">The frame type.</typeparam>
    public class ProtocolClient<TFrame> : ProtocolPeer<TFrame>, IProtocolClient
    {
        #region Properties
        /// <summary>
        /// Gets the protocol side.
        /// </summary>
        public override ProtocolSide Side {
            get {
                return ProtocolSide.Client;
            }
        }
        #endregion

        #region Methods
        /// <summary>
        /// Connects to the provided host and port.
        /// </summary>
        /// <param name="host">The host.</param>
        /// <param name="port">The port.</param>
        /// <returns></returns>
        public Task ConnectAsync(string host, int port) {
            return ConnectAsync(new Uri(string.Format("tcp://{0}:{1}", host, port)));
        }

        /// <summary>
        /// Connects to the provided URI, only supports tcp:// scheme currently.
        /// </summary>
        /// <param name="uri">The endpoint.</param>
        /// <returns></returns>
        public virtual async Task ConnectAsync(Uri uri) {
            // check if already connected
            if (IsConnected)
                throw new InvalidOperationException("The peer is already connected");

            // check scheme
            if (!uri.Scheme.Equals("tcp", StringComparison.CurrentCultureIgnoreCase))
                throw new UriFormatException("The protocol scheme must be TCP");

            // check if no port is specified
            if (uri.IsDefaultPort)
                throw new UriFormatException("The port must be defined in the URI");

            // create client and connect
            TcpClient client = new TcpClient();

            // try and connect
            await client.ConnectAsync(uri.Host, uri.Port).ConfigureAwait(false);

            // configure
            Configure(client);
        }
        #endregion

        #region Constructors
        /// <summary>
        /// Creates a new protocol client.
        /// </summary>
        /// <param name="coder">The coder.</param>
        /// <param name="configuration">The peer configuration.</param>
        public ProtocolClient(IProtocolCoder<TFrame> coder, PeerConfiguration configuration = null) : base(coder, configuration) {
        }
        #endregion
    }
}
