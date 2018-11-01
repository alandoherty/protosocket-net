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
    public abstract class ProtocolClient<TFrame> : ProtocolPeer<TFrame>
        where TFrame : class
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
            return ConnectAsync(new Uri(string.Format("tcp://{0}:{1}", host, port)), TimeSpan.Zero, CancellationToken.None);
        }

        /// <summary>
        /// Connects to the provided URI, only supports tcp:// scheme currently.
        /// </summary>
        /// <param name="uri">The uri.</param>
        /// <returns></returns>
        public Task ConnectAsync(Uri uri) {
            return ConnectAsync(uri, TimeSpan.Zero, CancellationToken.None);
        }

        /// <summary>
        /// Connects to the provided URI, only supports tcp:// scheme currently.
        /// </summary>
        /// <param name="uri">The endpoint.</param>
        /// <param name="timeout">The timeout.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        public virtual async Task ConnectAsync(Uri uri, TimeSpan timeout, CancellationToken cancellationToken) {
            // check scheme
            if (!uri.Scheme.Equals("tcp", StringComparison.CurrentCultureIgnoreCase))
                throw new UriFormatException("The protocol scheme must be TCP");

            // check if no port is specified
            if (uri.IsDefaultPort)
                throw new UriFormatException("The port must be defined in the URI");

            // create client and connect
            TcpClient client = new TcpClient();

            // create tasks
            Task timeoutTask = null;
            Task task = null;
            Task connectTask = client.ConnectAsync(uri.Host, uri.Port);

            if (timeout != TimeSpan.Zero)
                timeoutTask = Task.Delay(timeout);

            // build the final task
            try {
                if (timeoutTask == null && !cancellationToken.CanBeCanceled) {
                    task = connectTask;
                    await task.ConfigureAwait(false);
                } else if (timeoutTask != null && !cancellationToken.CanBeCanceled) {
                    task = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
                } else if (timeoutTask == null && cancellationToken.CanBeCanceled) {
                    task = await Task.WhenAny(connectTask).WithCancellation(cancellationToken).ConfigureAwait(false);
                } else if (timeoutTask != null && cancellationToken.CanBeCanceled) {
                    task = await Task.WhenAny(connectTask, timeoutTask).WithCancellation(cancellationToken).ConfigureAwait(false);
                }
            } catch (OperationCanceledException) {
                try {
                    client.Dispose();
                } catch (Exception) { }
                
                throw;
            }

            // check if we timed out
            if (timeoutTask != null && task == timeoutTask) {
                try {
                    client.Dispose();
                } catch (Exception) { }

                throw new TimeoutException("The request timed out before completion");
            }

            // configure
            Configure(client);
        }
        #endregion

        #region Constructors
        /// <summary>
        /// Creates a new protocol client.
        /// </summary>
        /// <param name="coder">The coder.</param>
        public ProtocolClient(IProtocolCoder<TFrame> coder) : base(coder) {

        }
        #endregion
    }
}
