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
    public class ProtocolServer<TConnection, TFrame> : IDisposable, IProtocolServer 
        where TConnection : ProtocolConnection<TConnection, TFrame>
    {
        #region Fields
        private int _disposed;
        private TcpListener _listener;
        private IConnectionFilter _filter;
        private List<TConnection> _connections = new List<TConnection>();
        private List<TConnection> _connectionsAnnounced = new List<TConnection>();
        private ProtocolCoderFactory<TFrame> _coderFactory;
        private Uri _endpoint;

        private PeerConfiguration _peerConfiguration;

        private AcceptMode _acceptMode = AcceptMode.Active;
        private TimeSpan _acceptDelay = TimeSpan.Zero;

        private CancellationTokenSource _disposeSource;
        #endregion

        #region Properties
        /// <summary>
        /// Gets or sets the connection filter, if any.
        /// </summary>
        public IConnectionFilter Filter {
            get {
                return _filter;
            } set {
                _filter = value;
            }
        }

        /// <summary>
        /// Gets the configured endpoints, if any.
        /// </summary>
        public IEnumerable<Uri> Endpoints {
            get {
                if (_endpoint == null)
                    throw new InvalidOperationException("The server has not been configured");

                return new Uri[] { _endpoint };
            }
        }

        /// <summary>
        /// Gets the number of connections.
        /// </summary>
        public int Count {
            get {
                return _connections.Count;
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

        /// <summary>
        /// Gets or sets the accept mode, you cannot change the accept mode after <see cref="Start"/> has been called.
        /// </summary>
        public AcceptMode AcceptMode {
            get {
                return _acceptMode;
            } set {
                if (_disposeSource != null)
                    throw new InvalidOperationException("The accept mode cannot be changed after the server has been started");

                _acceptMode = value;
            }
        }

        /// <summary>
        /// Gets or sets the optional accept delay, this is only applicable when the <see cref="AcceptMode"/> is <see cref="AcceptMode.Active"/>.
        /// </summary>
        public TimeSpan AcceptDelay {
            get {
                return _acceptDelay;
            } set {
                _acceptDelay = value;
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
            _endpoint = uri;
            _listener = new TcpListener(IPAddress.Parse(uri.Host), uri.Port);
        }
        
        /// <summary>
        /// Starts listening for new connections.
        /// </summary>
        public virtual void Start() {
            // check if disposed
            if (_disposed > 0)
                throw new ObjectDisposedException("The protocol server has been disposed");
            else if (_disposeSource != null)
                throw new InvalidOperationException("The server has already been started");

            // create dispose source
            _disposeSource = new CancellationTokenSource();
            
            // start listener and accept next
            _listener.Start();

            // start accept loop if our accept mode is active
            if (_acceptMode == AcceptMode.Active)
                AcceptLoop();
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
            _disposeSource.Cancel();
        }

        /// <summary>
        /// Sends and flushes the frame to all connections or those which match a predicate.
        /// </summary>
        /// <param name="frame">The connections.</param>
        /// <param name="predicate">The predicate to match connections.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        public Task SendAsync(TFrame frame, Predicate<TConnection> predicate = null, CancellationToken cancellationToken = default(CancellationToken)) {
            // create task list
            List<Task> tasks = predicate == null ? new List<Task>() : new List<Task>(_connections.Count);

            lock (_connections) {
                foreach (TConnection conn in _connections) {
                    if (conn.IsConnected && (predicate == null || predicate(conn)))
                        tasks.Add(conn.SendAsync(frame));
                }
            }

            return Task.WhenAll(tasks);
        }

        /// <summary>
        /// Flushes all connections or those which match a predicate.
        /// </summary>
        /// <param name="predicate">The predicate to match connections.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        public Task FlushAsync(Predicate<TConnection> predicate = null, CancellationToken cancellationToken = default(CancellationToken)) {
            // create task list
            List<Task> tasks = predicate == null ? new List<Task>() : new List<Task>(_connections.Count);

            lock (_connections) {
                foreach (TConnection conn in _connections) {
                    if (conn.IsConnected && (predicate == null || predicate(conn)))
                        tasks.Add(conn.FlushAsync(cancellationToken));
                }
            }

            return Task.WhenAll(tasks);
        }

        /// <summary>
        /// Queues the frame on all connections or those which match a predicate.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <param name="predicate">The predicate to match connections.</param>
        public void Queue(TFrame frame, Predicate<TConnection> predicate = null) {
            lock (_connections) {
                foreach (TConnection conn in _connections) {
                    if (conn.IsConnected && (predicate == null || predicate(conn)))
                        conn.Queue(frame);
                }
            }
        }
        #endregion

        #region Accepting
        /// <summary>
        /// Accepts the next <see cref="ProtocolConnection{TConnection, TFrame}"/> from the transport.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token, will only cancel if multiple accepts occur due to filtering.</param>
        /// <exception cref="ObjectDisposedException">If the server is disposed.</exception>
        /// <exception cref="OperationCanceledException">If the accept operation is cancelled.</exception>
        /// <returns>The accepted connection.</returns>
        public async Task<ProtocolConnection<TConnection, TFrame>> AcceptAsync(CancellationToken cancellationToken = default) {
            // check if disposed
            if (_disposed > 0)
                throw new ObjectDisposedException("The protocol server has been disposed");
            else if (_disposeSource == null)
                throw new InvalidOperationException("The server has not been started yet");

            // check the accept mode
            if (_acceptMode == AcceptMode.Active)
                throw new InvalidOperationException("The server must be in passive accept mode to accept connections manually");

            // keep trying to accept connections, sometimes we will filter connections which is why we have to loop
            // since this operation either returns a connection or gets cancelled
            while(true) {
                // check cancellation
                _disposeSource.Token.ThrowIfCancellationRequested();
                cancellationToken.ThrowIfCancellationRequested();

                // accept next client
                TcpClient client;

                try {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                } catch (SocketException ex) {
                    // log
                    Debug.WriteLine($"socket exception in AcceptLoop: {ex.ToString()}");
                    continue;
                }

                // accept client
                var conn = await AcceptNextAsync(client).ConfigureAwait(false);

                if (conn != null)
                    return conn;
            }
        }

        /// <summary>
        /// Accepts the next client from the listener.
        /// </summary>
        private async void AcceptLoop() {
            while (true) {
                // check if cancellation is requested
                if (_disposeSource.IsCancellationRequested) {
                    _disposeSource = null;
                    return;
                }

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
                Task _ = AcceptNextAsync(client);

                // optional delay
                if (_acceptDelay > TimeSpan.Zero) {
                    try {
                        await Task.Delay(_acceptDelay, _disposeSource.Token).ConfigureAwait(false);
                    } catch(OperationCanceledException) {
                        continue;
                    }
                }
            }
        }

        /// <summary>
        /// Accepts the provided client.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <returns></returns>
        private async Task<ProtocolConnection<TConnection, TFrame>> AcceptNextAsync(TcpClient client) {
            // filter
            if (_filter != null) {
                bool allow = false;
                IncomingContext incomingCtx = new IncomingContext() {
                    Server = this,
                    RemoteEndPoint = client.Client.RemoteEndPoint
                };

                try {
                    if (_filter.IsAsynchronous)
                        allow = await _filter.FilterAsync(incomingCtx, _disposeSource.Token).ConfigureAwait(false);
                    else
                        allow = _filter.Filter(incomingCtx);
                } catch (OperationCanceledException) {
                    client.Dispose();
                    return null;
                }

                // if we were filtered, dispose
                if (!allow) {
                    client.Dispose();
                    return null;
                }
            }

            // create connection
            TConnection connection = (TConnection)Activator.CreateInstance(typeof(TConnection), this, _coderFactory, _peerConfiguration);

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
                } catch (Exception) {
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

            // configure and optionally upgrade
            connection.Configure(client);

            return connection;
        }
        #endregion

        #region Classes
        #endregion

        #region Constructors
        /// <summary>
        /// Creates a new protocol server.
        /// </summary>
        /// <param name="coderFactory">The protocol coder factory.</param>
        /// <param name="peerConfiguration">The peer configuration.</param>
        public ProtocolServer(ProtocolCoderFactory<TFrame> coderFactory, PeerConfiguration peerConfiguration = null) {
            // check coder isn't null
            if (coderFactory == null)
                throw new ArgumentNullException(nameof(coderFactory), "The coder factory cannot be null");

            _coderFactory = coderFactory;
            _peerConfiguration = peerConfiguration;
        }
        #endregion
    }
}
