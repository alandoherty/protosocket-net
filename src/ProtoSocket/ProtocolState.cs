using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProtoSocket
{
    /// <summary>
    /// Defines the states of a protocol peer.
    /// </summary>
    public enum ProtocolState
    {
        /// <summary>
        /// The underlying peer transport is connecting.
        /// </summary>
        Connecting,
        
        /// <summary>
        /// The underlying peer transport is connected.
        /// </summary>
        Connected,
        
        /// <summary>
        /// The peer is being upgraded.
        /// </summary>
        Upgrading,

        /// <summary>
        /// The underlying peer transport is disconnecting.
        /// </summary>
        Disconnecting,

        /// <summary>
        /// The underlying peer transport is disconnected.
        /// The peer is not available for use at this point.
        /// </summary>
        Disconnected
    }
}
