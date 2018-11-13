using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace ProtoSocket
{
    /// <summary>
    /// Represents a low-level protocol peer.
    /// </summary>
    public abstract class ProtocolPeer<TFrame> : IDisposable
        where TFrame : class
    {
        #region Fields
        private NetworkStream _netStream;
        private Stream _dataStream;
        private TcpClient _client;
        private string _closeReason;
        private object _userdata = null;
        private IProtocolCoder<TFrame> _coder;
        private ProtocolState _state = ProtocolState.Connecting;

        private IPEndPoint _localEP;
        private IPEndPoint _remoteEP;

        private int _disposed;
        internal CancellationTokenSource _disposeSource = new CancellationTokenSource();
        internal CancellationTokenSource _readCancelSource = new CancellationTokenSource();
        private BufferBlock<TFrame> _inBuffer = null;
        private BufferBlock<QueuedFrame> _outBuffer = null;
        private SemaphoreSlim _outBufferLock = new SemaphoreSlim(1, 1);

        private Dictionary<object, CorrelatedWait> _correlations = new Dictionary<object, CorrelatedWait>();
        private List<Tuple<TaskCompletionSource<TFrame>, Predicate<TFrame>>> _correlationPredicates = new List<Tuple<TaskCompletionSource<TFrame>, Predicate<TFrame>>>();

        private long _statFramesIn;
        private long _statFramesOut;

        private object _sendLock = new object();
        #endregion

        #region Properties
        /// <summary>
        /// Gets the side of the protocol.
        /// </summary>
        public abstract ProtocolSide Side { get; }

        /// <summary>
        /// Gets the coder used by this peer.
        /// </summary>
        public IProtocolCoder<TFrame> Coder {
            get {
                return _coder;
            }
        }

        /// <summary>
        /// Gets or sets the userdata stored on the peer.
        /// </summary>
        public object Userdata {
            get {
                return _userdata;
            } set {
                _userdata = value;
            }
        }

        /// <summary>
        /// Gets the peer state.
        /// </summary>
        public ProtocolState State {
            get {
                return _state;
            }
        }

        /// <summary>
        /// Gets the remote end point.
        /// </summary>
        public IPEndPoint RemoteEndPoint {
            get {
                return _remoteEP;
            }
        }

        /// <summary>
        /// Gets the local end point.
        /// </summary>
        public IPEndPoint LocalEndPoint {
            get {
                return _localEP;
            }
        }

        /// <summary>
        /// Gets the reason the peer was closed.
        /// </summary>
        public string CloseReason {
            get {
                return _closeReason;
            }
        }

        /// <summary>
        /// Gets if the peer is connected.
        /// </summary>
        public bool IsConnected {
            get {
                return _state == ProtocolState.Connected;
            }
        }
        #endregion

        #region Events
        /// <summary>
        /// Called when the peer connects.
        /// </summary>
        public event EventHandler<PeerConnectedEventArgs<TFrame>> Connected;

        /// <summary>
        /// Called when the peer disconnects.
        /// </summary>
        public event EventHandler<PeerDisconnectedEventArgs<TFrame>> Disconnected;

        /// <summary>
        /// Called when the peer receives a frame.
        /// </summary>
        public event EventHandler<PeerReceivedEventArgs<TFrame>> Received;

        /// <summary>
        /// Called when the peer has changed state.
        /// </summary>
        public event EventHandler<PeerStateChangedEventArgs<TFrame>> StateChanged;

        /// <summary>
        /// Invoke the state changed event.
        /// </summary>
        /// <param name="e">The event arguments.</param>
        private void OnStateChanged(PeerStateChangedEventArgs<TFrame> e) {
            StateChanged?.Invoke(this, e);
        }

        /// <summary>
        /// Invoke the connected event.
        /// </summary>
        /// <param name="e">The event arguments.</param>
        protected virtual void OnConnected(PeerConnectedEventArgs<TFrame> e) {
            if (_state == ProtocolState.Connecting) {
                OnStateChanged(new PeerStateChangedEventArgs<TFrame>() {
                    OldState = _state,
                    NewState = ProtocolState.Connected
                });

                _state = ProtocolState.Connected;

                // trigger events
                Connected?.Invoke(this, e);
            }
        }

        /// <summary>
        /// Invoke the disconnected event.
        /// </summary>
        /// <param name="e">The event arguments.</param>
        protected virtual void OnDisconnected(PeerDisconnectedEventArgs<TFrame> e) {
            if (_state == ProtocolState.Disconnecting) {
                OnStateChanged(new PeerStateChangedEventArgs<TFrame>() {
                    OldState = _state,
                    NewState = ProtocolState.Disconnected
                });

                _state = ProtocolState.Disconnected;

                // trigger events
                Disconnected?.Invoke(this, e);
            }
        }

        /// <summary>
        /// Invoke the received event.
        /// </summary>
        /// <param name="e">The event arguments.</param>
        protected virtual void OnReceived(PeerReceivedEventArgs<TFrame> e) {
            Received?.Invoke(this, e);
        }
        #endregion

        #region Queued IO
        /*
        /// <summary>
        /// Queues sending a frame, it may be sent eventually or not at all.
        /// The task will complete once the frame has been sent successfully.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <param name="token">The cancellation token.</param>
        /// <returns></returns>
        private async Task QueueSendAsync(TFrame frame, CancellationToken token) {

        }
        */
        /// <summary>
        /// Processes any queued sends.
        /// </summary>
        /// <param name="token">The cancellation token.</param>
        /// <returns></returns>
        private Task ProcessQueueAsync(CancellationToken token) {
            while(_outBuffer.TryReceive(out QueuedFrame frame)) {
            }

            return Task.FromResult(true);
        }
        #endregion

        #region Sending
        /// <summary>
        /// Sends a single frame to the opposing peer asyncronously.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <param name="token">The cancellation token.</param>
        /// <returns></returns>
        private async Task RawSendAsync(TFrame frame, CancellationToken token) {
            // generate a linked cancellation if necessary
            CancellationToken cancelToken = _disposeSource.Token;
            CancellationTokenSource cancelTokenLinked = null;

            if (token != CancellationToken.None) {
                cancelTokenLinked = CancellationTokenSource.CreateLinkedTokenSource(token, _disposeSource.Token);
                cancelToken = cancelTokenLinked.Token;
            }

            // write to buffer
            try {
                await _coder.WriteAsync(_dataStream, frame, new CoderContext<TFrame>(this), _disposeSource.Token).ConfigureAwait(false);
                await _dataStream.FlushAsync().ConfigureAwait(false);

                if (cancelTokenLinked != null)
                    cancelTokenLinked.Dispose();

                // increment stat
                _statFramesOut++;
            } catch(Exception) {
                _closeReason = "Failed to encode frame";
                ForceClose();
                throw;
            }
        }

        /// <summary>
        /// Sends the frame asyncronously.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <returns></returns>
        public Task SendAsync(TFrame frame) {
            return SendAsync(frame, CancellationToken.None);
        }

        /// <summary>
        /// Sends the frames asyncronously.
        /// </summary>
        /// <param name="frames">The frames.</param>
        /// <returns></returns>
        public Task SendAsync(IEnumerable<TFrame> frames) {
            return SendAsync(frames, CancellationToken.None);
        }

        /// <summary>
        /// Sends many frames asyncronously.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        public virtual Task SendAsync(TFrame frame, CancellationToken cancellationToken) {
            if (_client == null)
                throw new InvalidOperationException("The peer has not been configured");

            // try and enter the send lock
            //bool canLock = false;
            //Monitor.TryEnter(_sendLock, ref canLock);

            // get combined cancellation token
            if (cancellationToken == CancellationToken.None)
                cancellationToken = _disposeSource.Token;
            else
                cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeSource.Token).Token;

            // if we can't enter the send lock we need to queue up
            //if (canLock) {
                //return QueueSendAsync(frame, cancellationToken);
            //} else {
                return RawSendAsync(frame, cancellationToken);
            //}
        }

        /// <summary>
        /// Sends many frames asyncronously.
        /// </summary>
        /// <param name="frames">The frame array.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        public virtual async Task SendAsync(IEnumerable<TFrame> frames, CancellationToken cancellationToken) {
            // get combined cancellation token
            if (cancellationToken == CancellationToken.None)
                cancellationToken = _disposeSource.Token;
            else
                cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeSource.Token).Token;

            //TODO: more efficient
            foreach (TFrame frame in frames)
                await RawSendAsync(frame, cancellationToken).ConfigureAwait(false);
        }
        #endregion

        #region Receiving
        /// <summary>
        /// Receives a single frame asyncronously.
        /// </summary>
        /// <returns></returns>
        public Task<TFrame> ReceiveAsync() {
            return _inBuffer.ReceiveAsync(_disposeSource.Token);
        }

        /// <summary>
        /// Receives a single frame asyncronously.
        /// </summary>
        /// <param name="timeout"></param>
        /// <returns></returns>
        public Task<TFrame> ReceiveAsync(TimeSpan timeout) {
            return _inBuffer.ReceiveAsync(timeout, _disposeSource.Token);
        }

        /// <summary>
        /// Tries to receive a frame from the connection, not guarenteed to respond if another ReceiveAsync call is in process.
        /// </summary>
        /// <param name="filter">The filter.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        private async Task<TFrame> TryReceiveAsync(Predicate<TFrame> filter, CancellationToken cancellationToken) {
            while (true) {
                // throw if requested
                cancellationToken.ThrowIfCancellationRequested();

                // get output
                await _inBuffer.OutputAvailableAsync(cancellationToken).ConfigureAwait(false);

                // wait for both
                if (_inBuffer.TryReceive(filter, out TFrame frame))
                    return frame;
            }
        }

        /// <summary>
        /// Receives a single frame asyncronously that matches the predicate.
        /// </summary>
        /// <param name="filter">The filter predicate.</param>
        /// <param name="timeout">The timeout.</param>
        /// <returns></returns>
        public async Task<TFrame> ReceiveAsync(Predicate<TFrame> filter, TimeSpan timeout) {
            if (_client == null)
                throw new InvalidOperationException("The peer has not been configured");

            // create timeout task
            Task timeoutTask = Task.Delay(timeout);

            while (true) {
                // create completion and cancellation for predicate
                TaskCompletionSource<TFrame> predicateTaskSource = new TaskCompletionSource<TFrame>();
                CancellationTokenSource predicateCancellation = new CancellationTokenSource();
                var predicateTuple = new Tuple<TaskCompletionSource<TFrame>, Predicate<TFrame>>(predicateTaskSource, filter);

                // add to list
                lock (_correlationPredicates) {
                    _correlationPredicates.Add(predicateTuple);
                }

                // try and receive
                Task<TFrame> immediateTask = TryReceiveAsync(filter, predicateCancellation.Token);

                // wait for both
                Task firstTask = await Task.WhenAny(predicateTaskSource.Task, immediateTask, timeoutTask).ConfigureAwait(false);

                if (firstTask == timeoutTask) {
                    // cancel predicate task
                    predicateCancellation.Cancel();

                    // remove from list
                    lock (_correlationPredicates)
                        _correlationPredicates.Remove(predicateTuple);

                    // throw timeout
                    throw new TimeoutException("The receive timed out before the predicate could match");
                } else if (firstTask == immediateTask) {
                    // remove from list
                    lock (_correlationPredicates)
                        _correlationPredicates.Remove(predicateTuple);

                    return immediateTask.Result;
                } else if (firstTask == predicateTaskSource.Task) {
                    // cancel predicate task
                    predicateCancellation.Cancel();

                    // remove from list
                    lock (_correlationPredicates)
                        _correlationPredicates.Remove(predicateTuple);

                    return predicateTaskSource.Task.Result;
                }
            }
        }
        #endregion

        #region Request/Response
        /// <summary>
        /// Waits for a response to a correlation id asyncronously.
        /// </summary>
        /// <param name="correlationId">The correlation id.</param>
        /// <param name="correlationWait">The correlation wait.</param>
        /// <param name="timeout">The timeout.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        private async Task<TFrame> WaitForCorrelationAsync(object correlationId, CorrelatedWait correlationWait, TimeSpan timeout, CancellationToken cancellationToken) {
            // create tasks
            Task timeoutTask = null;
            Task task = null;

            if (timeout != TimeSpan.Zero)
                timeoutTask = Task.Delay(timeout);

            // build the final task
            try {
                if (timeoutTask == null && !cancellationToken.CanBeCanceled) {
                    task = correlationWait.TaskSource.Task;
                    await task.ConfigureAwait(false);
                } else if (timeoutTask != null && !cancellationToken.CanBeCanceled) {
                    task = await Task.WhenAny(correlationWait.TaskSource.Task, timeoutTask).ConfigureAwait(false);
                } else if (timeoutTask == null && cancellationToken.CanBeCanceled) {
                    task = await Task.WhenAny(correlationWait.TaskSource.Task).WithCancellation(cancellationToken).ConfigureAwait(false);
                } else if (timeoutTask != null && cancellationToken.CanBeCanceled) {
                    task = await Task.WhenAny(correlationWait.TaskSource.Task, timeoutTask).WithCancellation(cancellationToken).ConfigureAwait(false);
                }
            } catch (OperationCanceledException ex) {
                lock (_correlations) {
                    try {
                        _correlations.Remove(correlationId);
                    } catch (Exception) {
                    }
                }

                if (_disposeSource.Token == ex.CancellationToken)
                    throw new ObjectDisposedException(nameof(ProtocolPeer<TFrame>), "The peer was disposed before the correlation could be fulfilled");
                else
                    throw;
            }

            // check if we timed out
            if (timeoutTask != null && task == timeoutTask) {
                lock (_correlations) {
                    try {
                        _correlations.Remove(correlationId);
                    } catch (Exception) {
                    }
                }

                throw new TimeoutException("The request timed out before completion");
            }

            return ((Task<TFrame>)task).Result;
        }

        /// <summary>
        /// Sends the frame and waits for the response, the frame must be correlatable.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <returns></returns>
        public Task<TFrame> RequestAsync(TFrame frame) {
            return RequestAsync(frame, TimeSpan.Zero, CancellationToken.None);
        }

        /// <summary>
        /// Sends the frame and waits for the response, the frame must be correlatable.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <param name="timeout">The timeout.</param>
        /// <returns></returns>
        public Task<TFrame> RequestAsync(TFrame frame, TimeSpan timeout) {
            return RequestAsync(frame, timeout, CancellationToken.None);
        }

        /// <summary>
        /// Sends the frame and waits for the response, the frame must be correlatable.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        public Task<TFrame> RequestAsync(TFrame frame, CancellationToken cancellationToken) {
            return RequestAsync(frame, TimeSpan.Zero, cancellationToken);
        }

        /// <summary>
        /// Sends the frame and waits for the response, the frame must be correlatable.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <param name="timeout">The timeout.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        public virtual async Task<TFrame> RequestAsync(TFrame frame, TimeSpan timeout, CancellationToken cancellationToken) {
            if (_client == null)
                throw new InvalidOperationException("The peer has not been configured");

            // check we can get a corellation for this frame
            if (!(frame is ICorrelatableFrame<TFrame>))
                throw new InvalidOperationException(string.Format("The frame must implement {0}", nameof(ICorrelatableFrame<TFrame>)));

            // get combined cancellation token
            if (cancellationToken == CancellationToken.None)
                cancellationToken = _disposeSource.Token;
            else
                cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeSource.Token).Token;

            // get requestable frame
            ICorrelatableFrame<TFrame> requestableFrame = frame as ICorrelatableFrame<TFrame>;

            // add frame to correlation list
            CorrelatedWait wait = new CorrelatedWait() {
                TaskSource = new TaskCompletionSource<TFrame>()
            };

            lock (_correlations) {
                // check if it already exists
                if (_correlations.ContainsKey(requestableFrame.CorrelationId))
                    throw new InvalidOperationException("The correlation is already in use for another frame");

                _correlations.Add(requestableFrame.CorrelationId, wait);
            }

            // send request
            await SendAsync(frame, cancellationToken).ConfigureAwait(false);

            // wait for the response
            return await WaitForCorrelationAsync(requestableFrame.CorrelationId, wait, timeout, cancellationToken).ConfigureAwait(false);
        }
        #endregion

        #region Methods
        /// <summary>
        /// Upgrades the peer with the provided upgrader.
        /// </summary>
        /// <param name="upgrader">The upgrader.</param>
        /// <returns></returns>
        public async Task UpgradeAsync(IProtocolUpgrader<TFrame> upgrader) {
            if (_client == null)
                throw new InvalidOperationException("The peer has not been configured");

            // check state
            if (_state == ProtocolState.Upgrading)
                throw new InvalidOperationException("The peer is already upgrading");
            else if (_state != ProtocolState.Connected)
                throw new InvalidOperationException("The peer must be connected to upgrade");

            // cancel pending read
            _readCancelSource.Cancel();

            // update state
            OnStateChanged(new PeerStateChangedEventArgs<TFrame>() {
                OldState = _state,
                NewState = ProtocolState.Upgrading
            });

            _state = ProtocolState.Upgrading;

            // perform upgrade
            try {
                _dataStream = await upgrader.UpgradeAsync(_dataStream, this);
            } catch (Exception) {
                _closeReason = "Failed to upgrade peer stream";
                ForceClose();
                throw;
            }

            // resume state
            OnStateChanged(new PeerStateChangedEventArgs<TFrame>() {
                OldState = _state,
                NewState = ProtocolState.Connected
            });

            _state = ProtocolState.Connected;

            // create new read cancel source and restart loop
            _readCancelSource = new CancellationTokenSource();
            ReadLoop();
        }

        /// <summary>
        /// Forcibly close the peer, should be called when connection is lost.
        /// We have no time to send out any queued frames.
        /// </summary>
        private void ForceClose() {
            if (_state == ProtocolState.Disconnected)
                return;

            // dispose client
            OnStateChanged(new PeerStateChangedEventArgs<TFrame>() {
                OldState = _state,
                NewState = ProtocolState.Disconnected
            });

            _state = ProtocolState.Disconnecting;

            // dispose
            Dispose();
        }

        /// <summary>
        /// Reads the next frame.
        /// </summary>
        private async void ReadLoop() {
            while (_disposed == 0) {
                try {
                    // read frame
                    TFrame frame = default(TFrame);

                    try {
                        using (CancellationTokenSource cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(_disposeSource.Token, _readCancelSource.Token)) {
                            frame = await _coder.ReadAsync(_dataStream, new CoderContext<TFrame>(this), _disposeSource.Token);
                        }
                    } catch (OperationCanceledException ex) {
                        if (ex.CancellationToken == _disposeSource.Token) {
                            _closeReason = "Peer disposed before incoming frame decoded";
                            throw;
                        } else if (ex.CancellationToken == _readCancelSource.Token) {
                            return;
                        }
                    } catch (Exception) {
                        _closeReason = "Failed to decode incoming frame";
                        throw;
                    }

                    // if the frame is null we've reached end of stream
                    if (frame == null) {
                        _closeReason = "End of stream";
                        ForceClose();
                        return;
                    }

                    // increment stat
                    _statFramesIn++;

                    // correlate frame if supported
                    if (frame is ICorrelatableFrame<TFrame>) {
                        ICorrelatableFrame<TFrame> requestableFrame = frame as ICorrelatableFrame<TFrame>;

                        // verify we have a correlation
                        bool dropDead = false;

                        if (requestableFrame.HasCorrelation && requestableFrame.ShouldCorrelate(out dropDead)) {
                            // get the wait, if found
                            CorrelatedWait wait = default(CorrelatedWait);

                            lock (_correlations) {
                                if (_correlations.TryGetValue(requestableFrame.CorrelationId, out wait))
                                    _correlations.Remove(requestableFrame.CorrelationId);
                            }

                            // complete task
                            if (wait.TaskSource != null)
                                wait.TaskSource.SetResult(frame);

                            // read the next frame if we need to drop it
                            if (dropDead || wait.TaskSource != null)
                                continue;
                        }
                    }

                    // predicate correlations
                    if (_correlationPredicates.Count > 0) {
                        lock(_correlationPredicates) {
                            // look for the tuple
                            Tuple<TaskCompletionSource<TFrame>, Predicate<TFrame>> foundTuple = null;

                            foreach(var tuple in _correlationPredicates) {
                                if (tuple.Item2(frame)) {
                                    foundTuple = tuple;
                                    tuple.Item1.TrySetResult(frame);
                                    break;
                                }
                            }

                            // remove
                            _correlationPredicates.Remove(foundTuple);
                        }
                    }

                    // call event
                    PeerReceivedEventArgs<TFrame> e = new PeerReceivedEventArgs<TFrame>() { Peer = this, Frame = frame };
                    OnReceived(e);

                    if (!e.Handled) {
                        // post frame
                        await _inBuffer.SendAsync(frame);
                    }
                } catch (Exception) {
                    ForceClose();
                    return;
                }
            }
        }

        /// <summary>
        /// Configures this peer with the provided transport.
        /// </summary>
        /// <param name="client">The client.</param>
        internal void Configure(TcpClient client) {
            // check if disposed
            if (_disposed > 0)
                throw new ObjectDisposedException(nameof(ProtocolPeer<TFrame>));

            // check if already configured
            if (_client != null)
                throw new InvalidOperationException("The peer has already been configured");

            // set client
            _client = client;
            _client.NoDelay = true;
            _localEP = (IPEndPoint)_client.Client.LocalEndPoint;
            _remoteEP = (IPEndPoint)_client.Client.RemoteEndPoint;

            // setup state
            OnStateChanged(new PeerStateChangedEventArgs<TFrame>() {
                OldState = _state,
                NewState = ProtocolState.Connecting
            });
            _state = ProtocolState.Connecting;

            // setup buffers
            _inBuffer = new BufferBlock<TFrame>();
            _outBuffer = new BufferBlock<QueuedFrame>();

            // set streams
            _netStream = client.GetStream();
            _dataStream = _netStream;

            // call event
            OnConnected(new PeerConnectedEventArgs<TFrame>() { Peer = this });

            // begin async read loop
            ReadLoop();
        }

        /// <summary>
        /// Transfers the underlying transport to another peer and disposes this object.
        /// The target peer must be unconfigured.
        /// </summary>
        /// <typeparam name="NTFrame">The new frame type.</typeparam>
        /// <param name="peer">The peer.</param>
        public void Transfer<NTFrame>(ProtocolPeer<NTFrame> peer)
            where NTFrame : class {
            if (_client == null)
                throw new InvalidOperationException("The peer has not been configured");

            // get client and nullify current reference
            TcpClient client = _client;

            // dispose
            _client = null;
            Dispose();

            // configure new peer
            peer.Configure(_client);
        }

        /// <summary>
        /// Disposes the peer and the underlying transport.
        /// </summary>
        public void Dispose() {
            // lock only one disposal
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            // close reason
            Interlocked.CompareExchange(ref _closeReason, "Disposed", null);

            // cancel
            try {
                _disposeSource.Cancel();
            } catch (Exception) { }

            // dispose client, if available
            try {
                if (_client != null)
                    _client.Dispose();
            } catch (Exception) { }

            // complete input buffer
            try {
                if (_inBuffer != null) {
                    _inBuffer.TryReceiveAll(out IList<TFrame> inList);
                    _inBuffer.Complete();
                }

                if (_outBuffer != null) {
                    _outBuffer.TryReceiveAll(out IList<QueuedFrame> outList);
                    _outBuffer.Complete();
                }
            } catch (Exception ex) {
                Debug.WriteLine($"failed to cleanup in/out buffers: {ex.ToString()}");
            }

            // cleanup out buffer lock
            try {
                _outBufferLock.Dispose();
            } catch(Exception ex) {
                Debug.WriteLine($"failed to cleanup out buffer lock: {ex.ToString()}");
            }

            // disconnected event
            if (_client != null)
                OnDisconnected(new PeerDisconnectedEventArgs<TFrame>() { Peer = this });
        }

        /// <summary>
        /// Closes the peer, waiting for all outbound messages to be sent.
        /// </summary>
        /// <param name="reason">The close reaosn.</param>
        /// <returns></returns>
        public async Task CloseAsync(string reason="Unspecified") {
            if (_client == null)
                throw new InvalidOperationException("The peer has not been configured");

            // check if already disconnecting or disconnected
            if (_state == ProtocolState.Disconnecting || _state == ProtocolState.Disconnected)
                return;

            // move to disconnecting
            OnStateChanged(new PeerStateChangedEventArgs<TFrame>() {
                OldState = _state,
                NewState = ProtocolState.Disconnecting
            });

            _state = ProtocolState.Disconnecting;
            _closeReason = reason;

            // process queue
            await ProcessQueueAsync(CancellationToken.None);

            // close
            Dispose();
        }
        #endregion

        #region Structures
        /// <summary>
        /// Represents a queued frame waiting to be sent.
        /// </summary>
        struct QueuedFrame
        {
            public TaskCompletionSource<bool> TaskSource { get; set; }
            public TFrame Frame { get; set; }
        }

        /// <summary>
        /// Represents a wait for a correleated frame.
        /// </summary>
        struct CorrelatedWait
        {
            public TaskCompletionSource<TFrame> TaskSource { get; set; }
        }
        #endregion

        #region Constructors
        /// <summary>
        /// Creates an uninitialized protocol peer.
        /// </summary>
        /// <param name="coder">The protocol coder.</param>
        protected ProtocolPeer(IProtocolCoder<TFrame> coder) {
            _coder = coder;
        }
        #endregion
    }

    /// <summary>
    /// Defines event arguments for peer connection.
    /// </summary>
    /// <typeparam name="TFrame">The frame type.</typeparam>
    public class PeerConnectedEventArgs<TFrame>
        where TFrame : class
    {
        /// <summary>
        /// Gets the peer object.
        /// </summary>
        public ProtocolPeer<TFrame> Peer { get; internal set; }
    }

    /// <summary>
    /// Defines event arguments for peer disconnected.
    /// </summary>
    /// <typeparam name="TFrame">The frame type.</typeparam>
    public class PeerDisconnectedEventArgs<TFrame>
        where TFrame : class
    {
        /// <summary>
        /// Gets the peer object.
        /// </summary>
        public ProtocolPeer<TFrame> Peer { get; internal set; }
    }

    /// <summary>
    /// Defines event arguments for a received frame.
    /// </summary>
    /// <typeparam name="TFrame">The frame type.</typeparam>
    public class PeerReceivedEventArgs<TFrame>
        where TFrame : class
    {
        /// <summary>
        /// Gets the peer object.
        /// </summary>
        public ProtocolPeer<TFrame> Peer { get; internal set; }

        /// <summary>
        /// Gets the frame.
        /// </summary>
        public TFrame Frame { get; internal set; }

        /// <summary>
        /// Gets or sets if the frame was handled and should not be pushed to the receive queue.
        /// </summary>
        public bool Handled { get; set; }
    }

    /// <summary>
    /// Defines event arguments for when a peer state changes.
    /// </summary>
    /// <typeparam name="TFrame">The frame type.</typeparam>
    public class PeerStateChangedEventArgs<TFrame>
        where TFrame : class
    {
        /// <summary>
        /// Gets the peer object.
        /// </summary>
        public ProtocolPeer<TFrame> Peer { get; internal set; }

        /// <summary>
        /// Gets the old state.
        /// </summary>
        public ProtocolState OldState { get; internal set; }

        /// <summary>
        /// Gets the new state.
        /// </summary>
        public ProtocolState NewState { get; internal set; }
    }
}
