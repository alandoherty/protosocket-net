using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProtoSocket
{
    /// <summary>
    /// Represents a protocol server.
    /// </summary>
    /// <typeparam name="TConnection">The connection type.</typeparam>
    /// <typeparam name="TFrame">The frame type.</typeparam>
    public abstract class ProtocolServer<TConnection, TFrame> : IDisposable, IProtocolServer 
        where TConnection : ProtocolConnection<TConnection, TFrame>
        where TFrame : class
    {
        #region Fields
        private int _disposed;
        private TcpListener _listener;
        private IConnectionFilter _filter;
        private List<TConnection> _connections = new List<TConnection>();
        private List<TConnection> _connectionsAnnounced = new List<TConnection>();
        private IProtocolCoder<TFrame> _coder;

        private CancellationTokenSource _stopSource;
        #endregion

        #region Properties
        /// <summary>
        /// Gets or sets the connection filter, if any.
        /// </summary>
        public IConnectionFilter ConnectionFilter {
            get {
                return _filter;
            } set {
                _filter = value;
            }
        }

        /// <summary>
        /// Gets the number of connections.
        /// </summary>
        public int Count {
            get {
                return 0;
            }
        }

        /// <summary>
        /// Gets a copy of the connections.
        /// </summary>
        public TConnection[] Connections {
            get {
                lock (_connections) {
                    return _connections.ToArray();
                }
            }
        }
        #endregion

        #region Events
        /// <summary>
        /// Called when a client connects.
        /// </summary>
        public event EventHandler<PeerConnectedEventArgs<TFrame>> Connected;

        /// <summary>
        /// Called when a server connects.
        /// </summary>
        public event EventHandler<PeerDisconnectedEventArgs<TFrame>> Disconnected;

        /// <summary>
        /// Called when a client connects.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The event arguments.</param>
        protected virtual void OnConnected(object sender, PeerConnectedEventArgs<TFrame> e) {
            Connected?.Invoke(sender, e);
        }

        /// <summary>
        /// Called when a client disconnects.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The event arguments.</param>
        protected virtual void OnDisconnected(object sender, PeerDisconnectedEventArgs<TFrame> e) {
            Disconnected?.Invoke(sender, e);
        }
        #endregion

        #region Methods
        /// <summary>
        /// Configures the listening endpoint.
        /// </summary>
        /// <param name="uriString">The URI string.</param>
        public void Configure(string uriString) {
            Configure(new Uri(uriString));
        }

        /// <summary>
        /// Configures the listening endpoint.
        /// </summary>
        /// <param name="uri">The URI string.</param>
        public void Configure(Uri uri) {
            // check if we're already configured
            if (_listener != null)
                throw new InvalidOperationException("The server can only be configured once");

            // check scheme
            if (!uri.Scheme.Equals("tcp", StringComparison.CurrentCultureIgnoreCase))
                throw new UriFormatException("The protocol scheme must be TCP");

            // check if no port is specified
            if (uri.IsDefaultPort)
                throw new UriFormatException("The port must be defined in the URI");

            // try and parse address
            if (!IPAddress.TryParse(uri.Host, out IPAddress addr)) {
            }

            // create listener
            _listener = new TcpListener(IPAddress.Parse(uri.Host), uri.Port);
        }

        /// <summary>
        /// Accepts the provided client.
        /// </summary>
        /// <param name="client">The client.</param>
        private async void AcceptNext(TcpClient client) {
            // filter
            if (_filter != null) {
                bool allow = false;

                try {
                    if (_filter is IAsyncConnectionFilter)
                        allow = await (_filter as IAsyncConnectionFilter).FilterAsync(client, _stopSource.Token).ConfigureAwait(false);
                    else if (_filter is IConnectionFilter)
                        allow = _filter.Filter(client);
                    else
                        throw new NotSupportedException("The connection filter is not supported");
                } catch (OperationCanceledException) {
                    return;
                }

                // if we were filtered, dispose
                if (!allow) {
                    client.Dispose();
                    return;
                }
            }

            // create connection
            TConnection connection = (TConnection)Activator.CreateInstance(typeof(TConnection), this, _coder);

            // add events
            connection.Connected += delegate (object o, PeerConnectedEventArgs<TFrame> e) {
                // add peer to announced list
                lock (_connectionsAnnounced)
                    _connectionsAnnounced.Add((TConnection)e.Peer);

                // trigger event
                OnConnected(this, e);
            };

            connection.Disconnected += delegate (object o, PeerDisconnectedEventArgs<TFrame> e) {
                // check if the connected event has been called for this peer, if not
                // we don't trigger the disconnected event either
                bool hasBeenAnnounced = false;

                lock (_connectionsAnnounced) {
                    hasBeenAnnounced = _connectionsAnnounced.Contains(e.Peer);

                    if (hasBeenAnnounced)
                        _connectionsAnnounced.Remove((TConnection)e.Peer);
                }

                // trigger event
                try {
                    if (hasBeenAnnounced)
                        OnDisconnected(this, e);
                } catch(Exception) {
                    // remove from connections
                    lock (_connections) {
                        _connections.Remove((TConnection)e.Peer);
                    }

                    throw;
                }

                // remove from connections
                lock (_connections) {
                    _connections.Remove((TConnection)e.Peer);
                }
            };

            // add to connection list
            lock (_connections) {
                _connections.Add(connection);
            }

            // configure
            connection.Configure(client);
        }

        /// <summary>
        /// Accepts the next client from the listener.
        /// </summary>
        private async void AcceptLoop() {
            while (true) {
                // accept next client
                TcpClient client;

                try {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                } catch (SocketException ex) {
                    // log
                    Debug.WriteLine($"socket exception in AcceptLoop: {ex.ToString()}");
                    continue;
                } catch (ObjectDisposedException) {
                    return;
                }

                // accept client
                AcceptNext(client);
            }
        }

        /// <summary>
        /// Starts listening for connections.
        /// </summary>
        public virtual void Start() {
            // check if disposed
            if (_disposed > 0)
                throw new ObjectDisposedException("The protocol server has been disposed");

            // create stop source
            _stopSource = new CancellationTokenSource();

            // start listener and accept next
            _listener.Start();
            AcceptLoop();
        }

        /// <summary>
        /// Stops listening for connections.
        /// </summary>
        public virtual void Stop() { 
            Dispose();
        }

        /// <summary>
        /// Disposes the underlying listener by stopping.
        /// </summary>
        public void Dispose() {
            // lock only one disposal
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            // stop listening
            _listener.Stop();

            // cancel any async operations
            _stopSource.Cancel();
        }
        #endregion

        #region Classes
        #endregion

        #region Constructors
        /// <summary>
        /// Creates a new protocol server.
        /// </summary>
        /// <param name="coder">The protocol coder.</param>
        public ProtocolServer(IProtocolCoder<TFrame> coder) {
            // check coder isn't null
            if (coder == null)
                throw new ArgumentNullException(nameof(coder), "The coder cannot be null");

            _coder = coder;
        }
        #endregion
    }
}
