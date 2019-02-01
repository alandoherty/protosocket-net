using System;
using System.Collections.Generic;
using System.Text;

namespace ProtoSocket
{
    /// <summary>
    /// Represents a peer connection.
    /// </summary>
    /// <typeparam name="TConnection">The connection type.</typeparam>
    /// <typeparam name="TFrame">The frame type.</typeparam>
    public abstract class ProtocolConnection<TConnection, TFrame> : ProtocolPeer<TFrame>, IProtocolConnection
        where TConnection : ProtocolConnection<TConnection, TFrame>
    {
        #region Fields
        private ProtocolServer<TConnection, TFrame> _server;
        #endregion

        #region Properties
        /// <summary>
        /// Gets the connection side.
        /// </summary>
        public override ProtocolSide Side {
            get {
                return ProtocolSide.Server;
            }
        }

        /// <summary>
        /// Gets the server this connection was created on.
        /// </summary>
        public ProtocolServer<TConnection, TFrame> Server {
            get {
                return _server;
            }
        }

        /// <summary>
        /// Gets the server this connection was created on.
        /// </summary>
        IProtocolServer IProtocolConnection.Server {
            get {
                return _server;
            }
        }
        #endregion

        #region Constructors
        /// <summary>
        /// Creates a new protocol connection with the provided coder.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="coderFactory">The coder factory.</param>
        /// <param name="configuration">The peer configuration.</param>
        protected ProtocolConnection(ProtocolServer<TConnection, TFrame> server, ProtocolCoderFactory<TFrame> coderFactory, PeerConfiguration configuration = null)
            : base(coderFactory, configuration) {
            _server = server;
        }
        #endregion
    }
}
