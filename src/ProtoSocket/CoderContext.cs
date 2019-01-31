using System;
using System.Collections.Generic;
using System.Text;

namespace ProtoSocket
{
    /// <summary>
    /// Represents a context for encoding/decoding.
    /// </summary>
    public struct CoderContext<TFrame>
    {
        #region Fields
        private ProtocolPeer<TFrame> _peer;
        #endregion

        #region Proerties
        /// <summary>
        /// Gets the peer.
        /// </summary>
        public ProtocolPeer<TFrame> Peer {
            get {
                return _peer;
            }
        }

        /// <summary>
        /// Gets the user data.
        /// </summary>
        public object Userdata {
            get {
                return _peer.Userdata;
            }
        }
        #endregion

        #region Constructors
        /// <summary>
        /// Creates a new coder context.
        /// </summary>
        /// <param name="peer">The peer.</param>
        internal CoderContext(ProtocolPeer<TFrame> peer) {
            _peer = peer;
        }
        #endregion
    }
}
