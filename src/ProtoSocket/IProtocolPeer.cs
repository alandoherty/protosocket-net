using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ProtoSocket
{
    /// <summary>
    /// Defines a base interface for protocol peers.
    /// </summary>
    public interface IProtocolPeer : IDisposable
    {
        /// <summary>
        /// Gets the side of the protocol.
        /// </summary>
        ProtocolSide Side { get; }

        /// <summary>
        /// Gets or sets the userdata stored on the peer.
        /// </summary>
        object Userdata { get; set; }

        /// <summary>
        /// Gets the peer state.
        /// </summary>
        ProtocolState State { get; }

        /// <summary>
        /// Gets the remote end point.
        /// </summary>
        IPEndPoint RemoteEndPoint { get; }

        /// <summary>
        /// Gets the local end point.
        /// </summary>
        IPEndPoint LocalEndPoint { get; }

        /// <summary>
        /// Gets the reason the peer was closed.
        /// </summary>
        string CloseReason { get; }

        /// <summary>
        /// Gets if the peer is connected.
        /// </summary>
        bool IsConnected { get; }
        
        /// <summary>
        /// Closes the peer, waiting for all outbound messages to be sent.
        /// </summary>
        /// <param name="reason">The close reaosn.</param>
        /// <returns></returns>
        Task CloseAsync(string reason = "Unspecified");
    }
}
