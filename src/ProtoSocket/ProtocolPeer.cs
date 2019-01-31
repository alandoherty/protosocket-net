using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProtoSocket
{
    /// <summary>
    /// Represents a low-level protocol peer.
    /// </summary>
    public abstract class ProtocolPeer<TFrame> : IProtocolPeer, IObservable<TFrame>, IDisposable
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
        private ProtocolMode _mode = ProtocolMode.Active;

        private IPEndPoint _localEP;
        private IPEndPoint _remoteEP;

        private int _disposed;
        internal CancellationTokenSource _disposeCancelSource = new CancellationTokenSource();
        internal CancellationTokenSource _readCancelSource = new CancellationTokenSource();
        internal CancellationTokenSource _readDisposeCancelSource = null;

        private Dictionary<object, CorrelatedWait> _correlations = new Dictionary<object, CorrelatedWait>();

        private long _statFramesIn;
        private long _statFramesOut;
        private long _statBytesIn;
        private long _statBytesOut;
        private DateTimeOffset? _statTimeConnected;
        private DateTimeOffset? _statTimeDisconnected;

        private Queue<QueuedFrame> _sendQueue = new Queue<QueuedFrame>();
        private SemaphoreSlim _sendSemaphore = new SemaphoreSlim(1, 1);

        private Queue<TFrame> _receiveQueue = new Queue<TFrame>();
        private TaskCompletionSource<TFrame> _receiveSource = null;

        private List<Subscription> _subscriptions = new List<Subscription>();

        private PipeReader _pipeReader = null;
        private PipeWriter _pipeWriter = null;
        private Pipe _pipe = null;

        private int _bufferSize;

        private bool _isDisposableFrame;
        private bool _connectedAnnounced;
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
        /// Gets the coder used by this peer.
        /// </summary>
        object IProtocolPeer.Coder {
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
        /// Gets the peer mode.
        /// </summary>
        public ProtocolMode Mode {
            get {
                return _mode;
            } set {
                // if same value ignore
                if (value == _mode)
                    return;

                // set mode
                _mode = value;

                // start the loop or stop the loop
                if (value == ProtocolMode.Active) {
                    ReadLoop();
                } else {
                    _readCancelSource.Cancel();
                }
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
        /// Gets the close exception, if any.
        /// </summary>
        public Exception CloseException {
            get {
                return _closeException;
            }
        }

        /// <summary>
        /// Gets or sets if to enable TCP keep alive.
        /// </summary>
        public bool KeepAlive {
            get {
                return (int)_client.Client.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive) == 1;
            } set {
                _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, value ? 1 : 0);
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

        #region Misc
        /// <summary>
        /// Gets the raw underlying data stream. Reading from the stream when the peer is in <see cref="ProtocolMode.Active"/> will almost certainly create problems, and writing should be synchronized such that no data is flushed concurrently. 
        /// </summary>
        /// <returns>The raw data stream.</returns>
        public Stream GetStream() {
            return _dataStream;
        }

        /// <summary>
        /// Gets the network statistics for the peer.
        /// </summary>
        /// <param name="stats">The statistics.</param>
        public void GetStatistics(out PeerStatistics stats) {
            stats = default;
            stats.AliveSpan = _statTimeConnected == null ? TimeSpan.Zero : (_statTimeDisconnected.HasValue ? _statTimeDisconnected.Value : DateTimeOffset.UtcNow) - _statTimeConnected.Value;
            stats.BytesIn = _statBytesIn;
            stats.BytesOut = _statBytesOut;
            stats.FramesIn = _statFramesIn;
            stats.FramesOut = _statFramesOut;
            stats.TimeConnected = _statTimeConnected;
            stats.TimeDisconnected = _statTimeDisconnected;
        }
        #endregion

        #region Queued Sending
        /// <summary>
        /// Queues the frames to be sent, this will happen on the next call to <see cref="FlushAsync(CancellationToken)"/>. 
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
        /// Queues the frames to be sent, this will happen on the next call to <see cref="FlushAsync(CancellationToken)"/>. 
        /// </summary>
        /// <param name="frames">The frames.</param>
        /// <exception cref="ObjectDisposedException">If the peer closes during the operation.</exception>
        /// <exception cref="InvalidOperationException">If the peer is disconnecting or disconnected.</exception>
        /// <returns>The total number of queued frames.</returns>
        public int Queue(params TFrame[] frames) {
            return Queue(frames);
        }

        /// <summary>
        /// Queues the frame to be sent, this will happen on the next call to <see cref="FlushAsync(CancellationToken)"/>/<see cref="SendAsync(TFrame, CancellationToken)"/>.
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
        /// The frame will be sent on the next call to <see cref="FlushAsync(CancellationToken)"/>.
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
        /// <returns>The number of sent frames.</returns>
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
                long bufferOutBytes = 0;
                MemoryStream bufferStream = new MemoryStream(_bufferSize);

                foreach (QueuedFrame queuedFrame in queuedFrames) {
                    // write to buffer
                    _coder.Write(bufferStream, queuedFrame.Frame, new CoderContext<TFrame>(this));

                    // if we're written at least buffer size bytes lets write it to the underlying stream and seek back
                    if (bufferStream.Position >= _bufferSize) {
                        // get the buffer segment from the stream
                        ArraySegment<byte> bufferArray;

                        if (!bufferStream.TryGetBuffer(out bufferArray))
                            throw new InvalidOperationException("The buffer could not be retrieved");

                        // write to stream and seek back
                        await _dataStream.WriteAsync(bufferArray.Array, bufferArray.Offset, bufferArray.Count, _disposeCancelSource.Token).ConfigureAwait(false);

                        bufferOutBytes += bufferArray.Count;
                        bufferStream.Seek(0, SeekOrigin.Begin);
                    }
                }
                
                // if we have any data to send, write it to the underlying stream
                if (bufferStream.Position >= 0) {
                    // get the buffer segment from the stream
                    ArraySegment<byte> bufferArray;

                    if (!bufferStream.TryGetBuffer(out bufferArray))
                        throw new InvalidOperationException("The buffer could not be retrieved");

                    bufferOutBytes += bufferArray.Count;
                    await _dataStream.WriteAsync(bufferArray.Array, bufferArray.Offset, bufferArray.Count, _disposeCancelSource.Token).ConfigureAwait(false);
                }

                // flush underlying stream
                await _dataStream.FlushAsync().ConfigureAwait(false);

                // increment stats
                _statFramesOut += queuedFrames.Count;
                _statBytesOut += bufferOutBytes;

                // mark all queued frames as sent
                foreach (QueuedFrame queuedFrame in queuedFrames) {
                    // trigger task source (used by QueueAsync)
                    if (queuedFrame.TaskSource != null)
                        queuedFrame.TaskSource.TrySetResult(true);

                    // if IDisposable call Dispose to indicate the frame was sent
                    if (queuedFrame.Frame is IDisposable)
                        ((IDisposable)queuedFrame.Frame).Dispose();
                }
            } catch(Exception ex) {
                Abort("Failed to encode frame", ex);
                throw;
            }

            return queuedFrames.Count;
        }

        /// <summary>
        /// Flushes all queued frames asyncronously.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="ObjectDisposedException">If the peer closes during the operation.</exception>
        /// <exception cref="InvalidOperationException">If the peer is disconnecting or disconnected.</exception>
        /// <exception cref="InvalidOperationException">If the peer has not been configured yet.</exception>
        /// <exception cref="OperationCanceledException">If the operation is cancelled.</exception>
        /// <returns>The number of sent frames.</returns>
        public virtual async Task<int> FlushAsync(CancellationToken cancellationToken = default(CancellationToken)) {
            // validate the peer isn't disposed or not ready
            if (_disposed == 1)
                throw new ObjectDisposedException(nameof(ProtocolPeer<TFrame>), "The peer has been disposed");
            else if (_state == ProtocolState.Upgrading)
                throw new InvalidOperationException("The peer is currently upgrading");
            else if (_state == ProtocolState.Disconnected || _state == ProtocolState.Disconnecting)
                throw new InvalidOperationException("The peer is not ready to send frames");
            else if (_client == null)
                throw new InvalidOperationException("The peer has not been configured");

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
        /// Sends and flushes a frame and all other queued frames asyncronously.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="ObjectDisposedException">If the peer closes during the operation.</exception>
        /// <exception cref="InvalidOperationException">If the peer is disconnecting or disconnected.</exception>
        /// <exception cref="InvalidOperationException">If the peer has not been configured yet.</exception>
        /// <exception cref="OperationCanceledException">If the operation is cancelled.</exception>
        /// <returns>The number of sent frames.</returns>
        public virtual async Task<int> SendAsync(TFrame frame, CancellationToken cancellationToken = default(CancellationToken)) {
            // validate the peer isn't disposed or not ready
            if (_disposed == 1)
                throw new ObjectDisposedException(nameof(ProtocolPeer<TFrame>), "The peer has been disposed");
            else if (_state == ProtocolState.Upgrading)
                throw new InvalidOperationException("The peer is currently upgrading");
            else if (_state == ProtocolState.Disconnected || _state == ProtocolState.Disconnecting)
                throw new InvalidOperationException("The peer is not ready to send frames");
            else if (_client == null)
                throw new InvalidOperationException("The peer has not been configured");

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
        /// <exception cref="InvalidOperationException">If the peer is disconnecting or disconnected.</exception>
        /// <exception cref="InvalidOperationException">If the peer has not been configured yet.</exception>
        /// <exception cref="OperationCanceledException">If the operation is cancelled.</exception>
        /// <returns>The number of sent frames.</returns>
        public virtual async Task<int> SendAsync(IEnumerable<TFrame> frames, CancellationToken cancellationToken = default(CancellationToken)) {
            // validate the peer isn't disposed or not ready
            if (_disposed == 1)
                throw new ObjectDisposedException(nameof(ProtocolPeer<TFrame>), "The peer has been disposed");
            else if (_state == ProtocolState.Upgrading)
                throw new InvalidOperationException("The peer is currently upgrading");
            else if (_state == ProtocolState.Disconnected || _state == ProtocolState.Disconnecting)
                throw new InvalidOperationException("The peer is not ready to send frames");
            else if (_client == null)
                throw new InvalidOperationException("The peer has not been configured");

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
        /// Trys to receive a frame from the peer asyncronously, this operation will not block.
        /// </summary>
        /// <param name="frame">The output frame.</param>
        /// <returns>If a frame was read.</returns>
        public bool TryReceive(out TFrame frame) {
            if (_receiveQueue.Count > 0) {
                lock (_receiveQueue) {
                    if (_receiveQueue.Count > 0) {
                        frame = _receiveQueue.Dequeue();
                        return true;
                    }
                }
            }

            frame = default(TFrame);
            return false;
        }

        /// <summary>
        /// Receives a single frame asyncronously.
        /// </summary>
        /// <param name="receiveBuffer">The receive buffer, not used in <see cref="ProtocolMode.Active"/> mode.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="ObjectDisposedException">If the peer closes during the operation.</exception>
        /// <exception cref="InvalidOperationException">If the peer is disconnecting or disconnected.</exception>
        /// <exception cref="InvalidOperationException">If the peer has not been configured yet.</exception>
        /// <exception cref="InvalidOperationException">If multiple concurrent receive calls are detected.</exception>
        /// <exception cref="OperationCanceledException">If the operation is cancelled.</exception>
        /// <returns></returns>
        public Task<TFrame> ReceiveAsync(byte[] receiveBuffer = null, CancellationToken cancellationToken = default(CancellationToken)) {
            return ReceiveAsync(Timeout.InfiniteTimeSpan, receiveBuffer, cancellationToken);
        }

        /// <summary>
        /// Receives a single frame asyncronously.
        /// </summary>
        /// <param name="timeout">The optional timeout.</param>
        /// <param name="receiveBuffer">The receive buffer, not used in <see cref="ProtocolMode.Active"/> mode.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="ObjectDisposedException">If the peer closes during the operation.</exception>
        /// <exception cref="InvalidOperationException">If the peer is disconnecting or disconnected.</exception>
        /// <exception cref="InvalidOperationException">If the peer has not been configured yet.</exception>
        /// <exception cref="OperationCanceledException">If the operation is cancelled.</exception>
        /// <exception cref="TimeoutException">If the operation has timed out.</exception>
        /// <returns>The received frame.</returns>
        public virtual async Task<TFrame> ReceiveAsync(TimeSpan timeout, byte[] receiveBuffer = null, CancellationToken cancellationToken = default(CancellationToken)) {
            // validate the peer isn't disposed or not ready
            if (_disposed == 1)
                throw new ObjectDisposedException(nameof(ProtocolPeer<TFrame>), "The peer has been disposed");
            else if (_state == ProtocolState.Upgrading)
                throw new InvalidOperationException("The peer is currently upgrading");
            else if (_state == ProtocolState.Disconnected || _state == ProtocolState.Disconnecting)
                throw new InvalidOperationException("The peer is not ready to receive frames");
            else if (_client == null)
                throw new InvalidOperationException("The peer has not been configured");

            // check if anything is in the queue, try and lock and get it if so
            if (_receiveQueue.Count > 0) {
                lock (_receiveQueue) {
                    if (_receiveQueue.Count > 0) {
                        return _receiveQueue.Dequeue();
                    }
                }
            }

            if (_mode == ProtocolMode.Active) {
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
            } else {
                byte[] recvBuffer = receiveBuffer ?? new byte[4096];
                List<TFrame> recvFrames = new List<TFrame>(1);

                // read from the peer
                if (await ReadAsync(recvBuffer, recvFrames, 1).ConfigureAwait(false) == 0) {
                    if (_closeException != null)
                        throw _closeException;
                    else
                        throw new Exception(_closeReason);
                }

                // return the frame
                return recvFrames[0];
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

                if (_disposeCancelSource.Token == ex.CancellationToken)
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
            // validate the peer isn't disposed or not ready
            if (_disposed == 1)
                throw new ObjectDisposedException(nameof(ProtocolPeer<TFrame>), "The peer has been disposed");
            else if (_state == ProtocolState.Upgrading)
                throw new InvalidOperationException("The peer is currently upgrading");
            else if (_state == ProtocolState.Disconnected || _state == ProtocolState.Disconnecting)
                throw new InvalidOperationException("The peer is not ready to send frames");
            else if (_client == null)
                throw new InvalidOperationException("The peer has not been configured");

            // check we can get a corellation for this frame
            if (!(frame is ICorrelatableFrame<TFrame>))
                throw new InvalidOperationException(string.Format("The frame must implement {0}", nameof(ICorrelatableFrame<TFrame>)));

            // get combined cancellation token
            if (cancellationToken == CancellationToken.None)
                cancellationToken = _disposeCancelSource.Token;
            else
                cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCancelSource.Token).Token;

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
        public async Task UpgradeAsync(IProtocolUpgrader upgrader) {
            if (_client == null)
                throw new InvalidOperationException("The peer has not been configured");

            // check state
            if (_state == ProtocolState.Upgrading)
                throw new InvalidOperationException("The peer is already upgrading");
            else if (_state != ProtocolState.Connected)
                throw new InvalidOperationException("The peer must be connected to upgrade");

            // raw upgrade
            await UpgradeRawAsync(upgrader).ConfigureAwait(false);

            // restart read loop
            _state = ProtocolState.Connected;
            ReadLoop();
        }

        /// <summary>
        /// Upgrades the peer with the provided upgrader. Does not check/change the state or restart the read loop.
        /// </summary>
        /// <param name="upgrader">The upgrader.</param>
        /// <returns></returns>
        private async Task UpgradeRawAsync(IProtocolUpgrader upgrader) {
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

            // create new read cancel source and restart loop
            _readCancelSource = new CancellationTokenSource();
            _readDisposeCancelSource = CancellationTokenSource.CreateLinkedTokenSource(_readCancelSource.Token, _disposeCancelSource.Token);
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
                NewState = ProtocolState.Disconnecting
            });

            // set disconnected
            _state = ProtocolState.Disconnecting;

            // remove all subscriptions mark as error
            lock(_subscriptions) {
                foreach(Subscription subscription in _subscriptions) {
                    subscription.Observer.OnError(closeException ?? new Exception($"Peer aborted: {closeReason ?? "Unknown"}"));
                }

                _subscriptions.Clear();
            }

            // dispose
            Dispose();
        }

        /// <summary>
        /// Reads up to the maximum number of frames.
        /// </summary>
        /// <param name="readBuffer">The read buffer.</param>
        /// <param name="outputBuffer">The output buffer.</param>
        /// <param name="maximumRead">The maximum number of frames to read.</param>
        /// <returns>The number of frames read.</returns>
        private async Task<int> ReadAsync(byte[] readBuffer, IList<TFrame> outputBuffer, int maximumRead = -1) {
            int readBufferCount = 0;
            int frameRead = 0;

            while (_coder.Read(_pipeReader, new CoderContext<TFrame>(this), out TFrame frame) && (maximumRead == -1 || maximumRead > frameRead)) {
                outputBuffer.Add(frame);
                frameRead++;
            }

            if (frameRead > 0)
                return frameRead;

            // check cancellation status
            if (_readDisposeCancelSource.IsCancellationRequested) {
                if (_disposeCancelSource.IsCancellationRequested)
                    Abort("Peer disposed before read from stream");

                return 0;
            }

            // read stream into pipe
            try {
                readBufferCount = await _dataStream.ReadAsync(readBuffer, 0, readBuffer.Length, _readDisposeCancelSource.Token).ConfigureAwait(false);
            } catch (OperationCanceledException ex) {
                if (ex.CancellationToken == _disposeCancelSource.Token)
                    Abort("Peer disposed before read from stream", ex);

                return 0;
            } catch (ObjectDisposedException ex) {
                Abort("End of stream", ex);
                return 0;
            } catch (Exception ex) {
                Abort($"Read exception: {ex.Message}");
                return 0;
            }

            // check if end of stream has been reached
            if (readBufferCount == 0) {
                Abort("End of stream");
                return 0;
            }

            // write to pipe
            try {
                await _pipeWriter.WriteAsync(new ReadOnlyMemory<byte>(readBuffer, 0, readBufferCount), _readCancelSource.Token).ConfigureAwait(false);
            } catch(OperationCanceledException) {
                return 0;
            }

            // increment stats
            _statBytesIn += readBufferCount;
            
            // try and process the incoming data into frames
            try {
                while (_coder.Read(_pipeReader, new CoderContext<TFrame>(this), out TFrame frame) && (maximumRead == -1 || maximumRead > frameRead)) {
                    outputBuffer.Add(frame);
                    frameRead++;
                    _statFramesIn++;
                }
            } catch (Exception ex) {
                _closeReason = (ex is ProtocolCoderException) ? "Failed to decode incoming frame" : "Exception occured while decoding frame";
                _closeException = ex;
                Abort(_closeReason, _closeException);
                return 0;
            }

            return frameRead;
        }

        /// <summary>
        /// Reads the next frames.
        /// </summary>
        private async void ReadLoop() {
            PeerReceivedEventArgs<TFrame> eventArgs = new PeerReceivedEventArgs<TFrame>() {
                Peer = this
            };

            // the internal read buffer
            byte[] readBuffer = new byte[_bufferSize];

            // the internal frame buffer
            List<TFrame> frameBuffer = new List<TFrame>(16);

            while (_disposed == 0) {
                try {
                    // read the next frames
                    await ReadAsync(readBuffer, frameBuffer, -1).ConfigureAwait(false);

                    // if we found at least one frame lets process them
                    if (frameBuffer.Count > 0) {
                        foreach(TFrame frame in frameBuffer) {
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

                    // clear frame buffer
                    frameBuffer.Clear();
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
        protected internal void Configure(TcpClient client) {
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
            _pipe = new Pipe(new PipeOptions(null, null, null, Math.Max(32768, _bufferSize * 2)));
            _pipeReader = _pipe.Reader;
            _pipeWriter = _pipe.Writer;

            // call event
            OnConnected(new PeerConnectedEventArgs<TFrame>() { Peer = this });
            _connectedAnnounced = true;

            // set time
            _statTimeConnected = DateTimeOffset.UtcNow;

            // begin async read loop
            if (_mode == ProtocolMode.Active)
                ReadLoop();
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

                    // dispose
                    if (queuedFrame.Frame is IDisposable)
                        ((IDisposable)queuedFrame.Frame).Dispose();
                }
            }

            // cancel
            try {
                _disposeCancelSource.Cancel();
            } catch (Exception) { }

            // dispose client, if available
            try {
                if (_client != null)
                    _client.Dispose();
            } catch (Exception) { }

            // remove all subscriptions mark as completed
            lock (_subscriptions) {
                foreach (Subscription sub in _subscriptions) {
                    sub.Observer.OnCompleted();
                }

                _subscriptions.Clear();
            }

            // mark pipe as completed
            if (_pipe != null) {
                _pipeReader.Complete(_closeException);
                _pipeWriter.Complete(_closeException);
            }

            // disconnected event
            if (_connectedAnnounced)
                OnDisconnected(new PeerDisconnectedEventArgs<TFrame>() { Peer = this });
            else
                _state = ProtocolState.Disconnected;

            // statistics time
            _statTimeDisconnected = DateTimeOffset.UtcNow;
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
        private ProtocolPeer(ProtocolMode mode, int bufferSize) {
            if (bufferSize < 1)
                throw new ArgumentOutOfRangeException(nameof(bufferSize), "The buffer size cannot be zero");

            _bufferSize = bufferSize;
            _mode = mode;
            _isDisposableFrame = typeof(IDisposable).GetTypeInfo().IsAssignableFrom(typeof(TFrame).GetTypeInfo());
        }

        /// <summary>
        /// Creates an uninitialized protocol peer.
        /// </summary>
        /// <param name="coder">The protocol coder.</param>
        /// <param name="mode">The protocol mode.</param>
        /// <param name="bufferSize">The read buffer size.</param>
        protected ProtocolPeer(IProtocolCoder<TFrame> coder, ProtocolMode mode = ProtocolMode.Active, int bufferSize = 8192) 
            : this(mode, bufferSize) {
            _readDisposeCancelSource = CancellationTokenSource.CreateLinkedTokenSource(_readCancelSource.Token, _disposeCancelSource.Token);
            _coder = coder;
        }

        /// <summary>
        /// Creates an uninitialized protocol peer.
        /// </summary>
        /// <param name="coderFactory">The protocol coder factory.</param>
        /// <param name="mode">The protocol mode.</param>
        /// <param name="bufferSize">The read buffer size.</param>
        protected ProtocolPeer(ProtocolCoderFactory<TFrame> coderFactory, ProtocolMode mode = ProtocolMode.Active, int bufferSize = 8192) 
            : this(mode, bufferSize) {
            _readDisposeCancelSource = CancellationTokenSource.CreateLinkedTokenSource(_readCancelSource.Token, _disposeCancelSource.Token);
            _coder = coderFactory(this);
        }
        #endregion
    }

    /// <summary>
    /// Defines event arguments for peer connection.
    /// </summary>
    /// <typeparam name="TFrame">The frame type.</typeparam>
    public class PeerConnectedEventArgs<TFrame>
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
