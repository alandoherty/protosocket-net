using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProtoSocket
{
    /// <summary>
    /// Represents a low-level protocol peer.
    /// </summary>
    public abstract class ProtocolPeer<TFrame> : IProtocolPeer, IObservable<TFrame>, IDisposable
        where TFrame : class
    {
        #region Fields
        private NetworkStream _netStream;
        private Stream _dataStream;
        private TcpClient _client;

        private string _closeReason;
        private Exception _closeException;

        private object _userdata = null;
        private IProtocolCoder<TFrame> _coder;
        private ProtocolState _state = ProtocolState.Connecting;

        private IPEndPoint _localEP;
        private IPEndPoint _remoteEP;

        private int _disposed;
        internal CancellationTokenSource _disposeSource = new CancellationTokenSource();
        internal CancellationTokenSource _readCancelSource = new CancellationTokenSource();

        private Dictionary<object, CorrelatedWait> _correlations = new Dictionary<object, CorrelatedWait>();

        private long _statFramesIn;
        private long _statFramesOut;

        private Queue<QueuedFrame> _sendQueue = new Queue<QueuedFrame>();
        private SemaphoreSlim _sendSemaphore = new SemaphoreSlim(1, 1);

        private Queue<TFrame> _receiveQueue = new Queue<TFrame>();
        private TaskCompletionSource<TFrame> _receiveSource = null;

        private List<Subscription> _subscriptions = new List<Subscription>();

        private PipeReader _pipeReader = null;
        private PipeWriter _pipeWriter = null;
        private Pipe _pipe = null;
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
        /// Gets or sets if to enable TCP keep alive.
        /// </summary>
        public bool KeepAlive {
            get {
                return (bool)_client.Client.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive);
            } set {
                _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, value);
            }
        }

        /// <summary>
        /// Gets or sets if TCP no delay is enabled.
        /// </summary>
        public bool NoDelay {
            get {
                return _client.NoDelay;
            } set {
                _client.NoDelay = value;
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
        /// <returns>If any delegates were invoked.</returns>
        protected virtual bool OnReceived(PeerReceivedEventArgs<TFrame> e) {
            if (Received == null)
                return false;

            Received.Invoke(this, e);
            return true;
        }
        #endregion

        #region Queued Sending
        /// <summary>
        /// Queues the frames to be sent, this will happen on the next call to <see cref="SendAsync(CancellationToken)"/>. 
        /// </summary>
        /// <param name="frames">The frames.</param>
        /// <exception cref="ObjectDisposedException">If the peer closes during the operation.</exception>
        /// <exception cref="InvalidOperationException">If the peer is disconnecting or disconnected.</exception>
        /// <returns>The total number of queued frames.</returns>
        public virtual int Queue(IEnumerable<TFrame> frames) {
            // validate the peer isn't disposed or not ready
            if (_disposed == 1)
                throw new ObjectDisposedException(nameof(ProtocolPeer<TFrame>), "The peer has been disposed");
            else if (_state == ProtocolState.Disconnected || _state == ProtocolState.Disconnecting)
                throw new InvalidOperationException("The peer is not ready to queue packets");

            // enqueue all frames atomically
            lock (_sendQueue) {
                foreach (TFrame frame in frames)
                    _sendQueue.Enqueue(new QueuedFrame() { Frame = frame, TaskSource = null });

                return _sendQueue.Count;
            }
        }

        /// <summary>
        /// Queues the frames to be sent, this will happen on the next call to <see cref="SendAsync(CancellationToken)"/>. 
        /// </summary>
        /// <param name="frames">The frames.</param>
        /// <exception cref="ObjectDisposedException">If the peer closes during the operation.</exception>
        /// <exception cref="InvalidOperationException">If the peer is disconnecting or disconnected.</exception>
        /// <returns>The total number of queued frames.</returns>
        public int Queue(params TFrame[] frames) {
            return Queue(frames);
        }

        /// <summary>
        /// Queues the frame to be sent, this will happen on the next call to <see cref="SendAsync(CancellationToken)"/>.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <exception cref="ObjectDisposedException">If the peer closes during the operation.</exception>
        /// <exception cref="InvalidOperationException">If the peer is disconnecting or disconnected.</exception>
        /// <returns>The total number of queued frames.</returns>
        public virtual int Queue(TFrame frame) {
            // validate the peer isn't disposed or not ready
            if (_disposed == 1)
                throw new ObjectDisposedException(nameof(ProtocolPeer<TFrame>), "The peer has been disposed");
            else if (_state == ProtocolState.Disconnected || _state == ProtocolState.Disconnecting)
                throw new InvalidOperationException("The peer is not ready to queue packets");

            // enqueue all frames atomically
            lock (_sendQueue) {
                _sendQueue.Enqueue(new QueuedFrame() { Frame = frame, TaskSource = null });
                return _sendQueue.Count;
            }
        }

        /// <summary>
        /// Queues the frame to be sent and returns a task which will complete after sending.
        /// The frame will be sent on the next call to <see cref="SendAsync(CancellationToken)"/>.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <exception cref="ObjectDisposedException">If the peer closes during the operation.</exception>
        /// <exception cref="InvalidOperationException">If the peer is disconnecting or disconnected.</exception>
        /// <returns>A task which will complete once the frame is sent.</returns>
        public virtual Task QueueAsync(TFrame frame) {
            // validate the peer isn't disposed or not ready
            if (_disposed == 1)
                throw new ObjectDisposedException(nameof(ProtocolPeer<TFrame>), "The peer has been disposed");
            else if (_state == ProtocolState.Disconnected || _state == ProtocolState.Disconnecting)
                throw new InvalidOperationException("The peer is not ready to queue packets");

            // create completion source
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

            // enqueue
            lock (_sendQueue) {
                _sendQueue.Enqueue(new QueuedFrame() { Frame = frame, TaskSource = tcs });
            }

            return tcs.Task;
        }
        #endregion

        #region Sending
        /// <summary>
        /// Sends all queued frames.
        /// You MUST obtain the send semaphore before calling this method.
        /// </summary>
        /// <returns></returns>
        private async Task<int> SendAllAsync() {
            // get queued packets, we create the list with the non-thread safe count in hopes it's accurate after
            // we obtain the lock, worst case is we allocated one too much or too little and we get a small performance penalty
            List<QueuedFrame> queuedFrames = new List<QueuedFrame>(_sendQueue.Count);

            lock (_sendQueue) {
                while (_sendQueue.Count > 0)
                    queuedFrames.Add(_sendQueue.Dequeue());
            }

            // if no frames are available return zero, shouldn't happen though
            if (queuedFrames.Count == 0)
                return 0;

            // write to buffer
            try {
                foreach (QueuedFrame queuedFrame in queuedFrames) {
                    // write to underlying stream
                    await _coder.WriteAsync(_dataStream, queuedFrame.Frame, new CoderContext<TFrame>(this), _disposeSource.Token).ConfigureAwait(false);

                    // complete task source if available
                    if (queuedFrame.TaskSource != null)
                        queuedFrame.TaskSource.TrySetResult(true);
                }

                // flush underlying stream
                await _dataStream.FlushAsync().ConfigureAwait(false);

                // increment stat
                _statFramesOut++;
            } catch(Exception ex) {
                Abort("Failed to encode frame", ex);
                throw;
            }

            return queuedFrames.Count;
        }

        /// <summary>
        /// Sends all queued frames asyncronously.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="ObjectDisposedException">If the peer closes during the operation.</exception>
        /// <exception cref="InvalidOperationException">If the peer is disconnecting, disconnected or connecting.</exception>
        /// <exception cref="InvalidOperationException">If the peer has not been configured yet.</exception>
        /// <exception cref="OperationCanceledException">If the operation is cancelled.</exception>
        /// <returns>The number of sent frames.</returns>
        public virtual async Task<int> SendAsync(CancellationToken cancellationToken = default(CancellationToken)) {
            // validate the peer isn't disposed or not ready
            if (_disposed == 1)
                throw new ObjectDisposedException(nameof(ProtocolPeer<TFrame>), "The peer has been disposed");
            else if (_client == null)
                throw new InvalidOperationException("The peer has not been configured");
            else if (_state == ProtocolState.Disconnected || _state == ProtocolState.Disconnecting || _state == ProtocolState.Connecting)
                throw new InvalidOperationException("The peer is not ready to send frames");

            // wait for the send semaphore to become available, if cancelled we let the exception
            // bubble up since nothing has happened yet
            await _sendSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try {
                // last call for cancellation
                cancellationToken.ThrowIfCancellationRequested();

                // send all waiting frames
                return await SendAllAsync().ConfigureAwait(false);
            } finally {
                _sendSemaphore.Release();
            }
        }

        /// <summary>
        /// Sends a frame and all other queued frames asyncronously.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="ObjectDisposedException">If the peer closes during the operation.</exception>
        /// <exception cref="InvalidOperationException">If the peer is disconnecting, disconnected or connecting.</exception>
        /// <exception cref="InvalidOperationException">If the peer has not been configured yet.</exception>
        /// <exception cref="OperationCanceledException">If the operation is cancelled.</exception>
        /// <returns>The number of sent frames.</returns>
        public virtual async Task<int> SendAsync(TFrame frame, CancellationToken cancellationToken = default(CancellationToken)) {
            // validate the peer isn't disposed or not ready
            if (_disposed == 1)
                throw new ObjectDisposedException(nameof(ProtocolPeer<TFrame>), "The peer has been disposed");
            else if (_client == null)
                throw new InvalidOperationException("The peer has not been configured");
            else if (_state == ProtocolState.Disconnected || _state == ProtocolState.Disconnecting || _state == ProtocolState.Connecting)
                throw new InvalidOperationException("The peer is not ready to send frames");

            // wait for the send semaphore to become available, if cancelled we let the exception
            // bubble up since nothing has happened yet
            await _sendSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try {
                // last call for cancellation
                cancellationToken.ThrowIfCancellationRequested();

                // add to queue, we don't do QueueAsync since the following SendAsync call guarentees it gets sent
                Queue(frame);

                // send all queued frames
                return await SendAllAsync().ConfigureAwait(false);
            } finally {
                _sendSemaphore.Release();
            }
        }

        /// <summary>
        /// Sends many frames and any other queued frames asyncronously.
        /// </summary>
        /// <param name="frames">The frames.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="ObjectDisposedException">If the peer closes during the operation.</exception>
        /// <exception cref="InvalidOperationException">If the peer is disconnecting, disconnected or connecting.</exception>
        /// <exception cref="InvalidOperationException">If the peer has not been configured yet.</exception>
        /// <exception cref="OperationCanceledException">If the operation is cancelled.</exception>
        /// <returns>The number of sent frames.</returns>
        public virtual async Task<int> SendAsync(IEnumerable<TFrame> frames, CancellationToken cancellationToken = default(CancellationToken)) {
            // validate the peer isn't disposed or not ready
            if (_disposed == 1)
                throw new ObjectDisposedException(nameof(ProtocolPeer<TFrame>), "The peer has been disposed");
            else if (_client == null)
                throw new InvalidOperationException("The peer has not been configured");
            else if (_state == ProtocolState.Disconnected || _state == ProtocolState.Disconnecting || _state == ProtocolState.Connecting)
                throw new InvalidOperationException("The peer is not ready to send frames");

            // wait for the send semaphore to become available, if cancelled we let the exception
            // bubble up since nothing has happened yet
            await _sendSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try {
                // last call for cancellation
                cancellationToken.ThrowIfCancellationRequested();

                // add to queue, we don't do QueueAsync since the following SendAsync call guarentees it gets sent
                Queue(frames);

                // send all queued frames
                return await SendAllAsync().ConfigureAwait(false);
            } finally {
                _sendSemaphore.Release();
            }
        }
        #endregion

        #region Receiving
        /// <summary>
        /// Receives a single frame asyncronously.
        /// </summary>
        /// <exception cref="ObjectDisposedException">If the peer closes during the operation.</exception>
        /// <exception cref="InvalidOperationException">If the peer is disconnecting or disconnected.</exception>
        /// <exception cref="InvalidOperationException">If the peer has not been configured yet.</exception>
        /// <exception cref="InvalidOperationException">If multiple concurrent receive calls are detected.</exception>
        /// <exception cref="OperationCanceledException">If the operation is cancelled.</exception>
        /// <returns></returns>
        public Task<TFrame> ReceiveAsync(CancellationToken cancellationToken = default(CancellationToken)) {
            return ReceiveAsync(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        /// <summary>
        /// Receives a single frame asyncronously.
        /// </summary>
        /// <param name="timeout">The optional timeout.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="ObjectDisposedException">If the peer closes during the operation.</exception>
        /// <exception cref="InvalidOperationException">If the peer is disconnecting or disconnected.</exception>
        /// <exception cref="InvalidOperationException">If the peer has not been configured yet.</exception>
        /// <exception cref="OperationCanceledException">If the operation is cancelled.</exception>
        /// <exception cref="TimeoutException">If the operation has timed out.</exception>
        /// <returns>The received frame.</returns>
        public virtual async Task<TFrame> ReceiveAsync(TimeSpan timeout, CancellationToken cancellationToken = default(CancellationToken)) {
            // validate the peer isn't disposed or not ready
            if (_disposed == 1)
                throw new ObjectDisposedException(nameof(ProtocolPeer<TFrame>), "The peer has been disposed");
            else if (_client == null)
                throw new InvalidOperationException("The peer has not been configured");
            else if (_state == ProtocolState.Disconnected || _state == ProtocolState.Disconnecting)
                throw new InvalidOperationException("The peer is not ready to receive frames");

            // check if anything is in the queue, try and lock and get it if so
            if (_receiveQueue.Count > 0) {
                lock (_receiveQueue) {
                    if (_receiveQueue.Count > 0) {
                        return _receiveQueue.Dequeue();
                    }
                }
            }

            // create source
            TaskCompletionSource<TFrame> completionSource = new TaskCompletionSource<TFrame>();

            if (Interlocked.CompareExchange(ref _receiveSource, completionSource, null) != null)
                throw new InvalidOperationException("A receive call is already in progress");

            try {
                if (timeout == Timeout.InfiniteTimeSpan) {
                    // if we have an infinite timeout just await the completion source
                    return await completionSource.Task.ConfigureAwait(false);
                } else {
                    // create timeout delay
                    Task timeoutDelay = Task.Delay(timeout);
                    Task completedTask = await Task.WhenAny(timeoutDelay, completionSource.Task).ConfigureAwait(false);

                    if (completedTask == timeoutDelay)
                        throw new TimeoutException("The receive operation has timed out");
                    else
                        return ((Task<TFrame>)completedTask).Result;
                }
            } finally {
                _receiveSource = null;
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
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        public Task<TFrame> RequestAsync(TFrame frame, CancellationToken cancellationToken = default(CancellationToken)) {
            return RequestAsync(frame, TimeSpan.Zero, CancellationToken.None);
        }

        /// <summary>
        /// Sends the frame and waits for the response, the frame must be correlatable.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <param name="timeout">The timeout.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        public virtual async Task<TFrame> RequestAsync(TFrame frame, TimeSpan timeout, CancellationToken cancellationToken = default(CancellationToken)) {
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

            // update state
            OnStateChanged(new PeerStateChangedEventArgs<TFrame>() {
                OldState = _state,
                NewState = ProtocolState.Upgrading
            });

            _state = ProtocolState.Upgrading;

            // perform upgrade
            try {
                _dataStream = await upgrader.UpgradeAsync(_dataStream, this);
            } catch (Exception ex) {
                Abort("Failed to upgrade peer stream", ex);
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
        /// <param name="closeReason">The optional reason.</param>
        /// <param name="closeException">The optional exception.</param>
        private void Abort(string closeReason = null, Exception closeException = null) {
            // check if already disconnected
            if (_state == ProtocolState.Disconnected)
                return;

            // set close reason and/or exception if not set already
            if (closeReason != null && _closeReason == null)
                _closeReason = closeReason;
            if (closeException != null && _closeException == null)
                _closeException = closeException;   

            // dispose client
            OnStateChanged(new PeerStateChangedEventArgs<TFrame>() {
                OldState = _state,
                NewState = ProtocolState.Disconnected
            });

            // set disconnected
            _state = ProtocolState.Disconnected;

            // remove all subscriptions mark as error
            lock(_subscriptions) {
                foreach(IObserver<TFrame> observer in _subscriptions) {
                    observer.OnError(closeException ?? new Exception($"Peer aborted: {closeReason ?? "Unknown"}"));
                }

                _subscriptions.Clear();
            }

            // dispose
            Dispose();
        }

        /// <summary>
        /// Reads the next frame.
        /// </summary>
        private async void ReadLoop() {
            PeerReceivedEventArgs<TFrame> eventArgs = new PeerReceivedEventArgs<TFrame>() {
                Peer = this
            };

            // the internal read buffer
            byte[] buffer = new byte[1024];
            int bufferRead = 0;

            while (_disposed == 0) {
                try {
                    // read stream into pipe
                    try {
                        bufferRead = await _dataStream.ReadAsync(buffer, 0, buffer.Length, _disposeSource.Token).ConfigureAwait(false);
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

                    // check if end of stream has been reached
                    if (bufferRead == 0) {
                        Abort("End of stream");
                        return;
                    }

                    // write to pipe
                    await _pipeWriter.WriteAsync(new ReadOnlyMemory<byte>(buffer, 0, bufferRead)).ConfigureAwait(false);

                    // try and process the incoming data into frames
                    bool processedFrame = false;
                    IEnumerable<TFrame> processedFrames = null;

                    try {
                        processedFrame = _coder.Read(_pipeReader, new CoderContext<TFrame>(this), out processedFrames);
                    } catch(Exception ex) {
                        _closeReason = (ex is ProtocolCoderException) ? "Failed to decode incoming frame" : "Exception occured while decoding frame";
                        _closeException = ex;
                        break;
                    }

                    // if we found at least one frame lets process them
                    if (processedFrame) {
                        foreach(TFrame frame in processedFrames) {
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

                            // check if a receive source is pending and try and set its result
                            TaskCompletionSource<TFrame> receiveSource = _receiveSource;

                            if (receiveSource != null) {
                                // if successful dont notify event handler or subscribers
                                if (receiveSource.TrySetResult(frame))
                                    continue;
                            }

                            // if the frame was observed, if not we know to add it to the queue
                            bool wasObserved = false;

                            // call event
                            eventArgs.Handled = false;
                            eventArgs.Frame = frame;

                            if (OnReceived(eventArgs))
                                wasObserved = true;

                            // if handled don't notify subscribers
                            if (eventArgs.Handled)
                                continue;

                            // notify subscribers
                            if (Notify(frame))
                                wasObserved = true;

                            // if the frame wasn't observed by anything add it to the queue
                            if (!wasObserved) {
                                lock (_receiveQueue) {
                                    _receiveQueue.Enqueue(frame);
                                }
                            }
                        }
                    }
                } catch (Exception ex) {
                    Abort("Unexpected exception during read", ex);
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
            _localEP = (IPEndPoint)_client.Client.LocalEndPoint;
            _remoteEP = (IPEndPoint)_client.Client.RemoteEndPoint;

            // setup state
            OnStateChanged(new PeerStateChangedEventArgs<TFrame>() {
                OldState = _state,
                NewState = ProtocolState.Connecting
            });
            _state = ProtocolState.Connecting;

            // set streams
            _netStream = client.GetStream();
            _dataStream = _netStream;

            // set pipe
            _pipe = new Pipe();
            _pipeReader = _pipe.Reader;
            _pipeWriter = _pipe.Writer;

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

            // set close reason to disposed if none is available
            Interlocked.CompareExchange(ref _closeReason, "Disposed", null);

            // create disposed exception
            ObjectDisposedException objectDisposedException = new ObjectDisposedException($"The peer has been closed: {_closeReason}", _closeException);

            // mark receive as disposed, we retrieve it first because other threads could set it to null
            // inbetween our accessing it
            TaskCompletionSource<TFrame> receiveSource = _receiveSource;

            if (receiveSource != null)
                receiveSource.TrySetException(objectDisposedException);

            // mark any queued frames that have task completion sources as faulted
            if (_sendQueue.Count > 0) {
                while(_sendQueue.Count > 0) {
                    // dequeue a frame
                    QueuedFrame queuedFrame = _sendQueue.Dequeue();

                    // check if it has a completion source and mark it with the exception
                    if (queuedFrame.TaskSource != null)
                        queuedFrame.TaskSource.TrySetException(objectDisposedException);
                }
            }

            // cancel
            try {
                _disposeSource.Cancel();
            } catch (Exception) { }

            // dispose client, if available
            try {
                if (_client != null)
                    _client.Dispose();
            } catch (Exception) { }

            // remove all subscriptions mark as completeds
            lock (_subscriptions) {
                foreach (IObserver<TFrame> observer in _subscriptions) {
                    observer.OnCompleted();
                }

                _subscriptions.Clear();
            }

            // disconnected event
            if (_client != null)
                OnDisconnected(new PeerDisconnectedEventArgs<TFrame>() { Peer = this });
        }

        /// <summary>
        /// Closes the peer, giving a best effort for all outbound messages to be sent.
        /// </summary>
        /// <param name="reason">The close reason.</param>
        /// <returns></returns>
        public async Task CloseAsync(string reason="User requested") {
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
            
            // set as disconnecting
            _state = ProtocolState.Disconnecting;
            _closeReason = reason;

            // try and send all if we can
            try {
                if (await _sendSemaphore.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false)) {
                    try {
                        await SendAllAsync().ConfigureAwait(false);
                    } finally {
                        _sendSemaphore.Release();
                    }
                }
            } catch (Exception) { }

            // close
            Dispose();
        }

        /// <summary>
        /// Notifies subscribers of an incoming frame.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <returns>If the frame was delivered to subscribers.</returns>
        private bool Notify(TFrame frame) {
            bool observed = false;

            if (_subscriptions.Count > 0) {
                lock (_subscriptions) {
                    foreach (Subscription subscription in _subscriptions) {
                        if (subscription.Predicate == null) {
                            observed = true;
                            subscription.Observer.OnNext(frame);
                        } else {
                            if (subscription.Predicate(frame)) {
                                observed = true;
                                subscription.Observer.OnNext(frame);
                            }
                        }
                    }
                }
            }

            return observed;
        }

        /// <summary>
        /// Subscribes the observer to this peer.
        /// </summary>
        /// <param name="observer">The observer.</param>
        /// <returns>A disposable wrapper for the subscription.</returns>
        public IDisposable Subscribe(IObserver<TFrame> observer) {
            Subscription subscription = null;

            // add subscription
            lock (_subscriptions) {
                subscription = new Subscription(this, observer, null);
                _subscriptions.Add(subscription);
            }

            return subscription;
        }

        /// <summary>
        /// Subscribes the observer to this peer.
        /// </summary>
        /// <param name="observer">The observer.</param>
        /// <param name="predicate">The predicate to match for frames.</param>
        /// <returns>A disposable wrapper for the subscription.</returns>
        public IDisposable Subscribe(IObserver<TFrame> observer, Predicate<TFrame> predicate) {
            Subscription subscription = null;

            // add subscription
            lock (_subscriptions) {
                subscription = new Subscription(this, observer, predicate);
                _subscriptions.Add(subscription);
            }

            return subscription;
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

        class Subscription : IDisposable
        {
            public ProtocolPeer<TFrame> Peer { get; private set; }
            public Predicate<TFrame> Predicate { get; private set; }
            public IObserver<TFrame> Observer { get; private set; }

            public void Dispose() {
                lock(Peer._subscriptions) {
                    Peer._subscriptions.Remove(this);
                }
            }

            public Subscription(ProtocolPeer<TFrame> peer, IObserver<TFrame> observer, Predicate<TFrame> predicate) {
                Peer = peer;
                Observer = observer;
                Predicate = predicate;
            }
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
