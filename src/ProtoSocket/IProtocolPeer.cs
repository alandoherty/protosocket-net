using System;
using System.Collections.Generic;
using System.IO;
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
        /// Gets the coder.
        /// </summary>
        object Coder { get; }

        /// <summary>
        /// Gets the peer state.
        /// </summary>
        ProtocolState State { get; }

        /// <summary>
        /// Gets or sets the peer mode.
        /// </summary>
        ProtocolMode Mode { get; set; }

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
        /// Gets the exception (if any), which caused the peer to close.
        /// </summary>
        Exception CloseException { get; }

        /// <summary>
        /// Gets if the peer is connected.
        /// </summary>
        bool IsConnected { get; }
        
        /// <summary>
        /// Closes the peer, waiting for all outbound messages to be sent.
        /// </summary>
        /// <param name="reason">The close reason.</param>
        /// <returns></returns>
        Task CloseAsync(string reason = "Unspecified");

        /// <summary>
        /// Gets the raw underlying data stream. Reading from the stream when the peer is in <see cref="ProtocolMode.Active"/> will almost certainly create problems, and writing should be synchronized such that no data is flushed concurrently. 
        /// </summary>
        /// <returns>The raw data stream.</returns>
        Stream GetStream();

        /// <summary>
        /// Gets the network statistics for the peer.
        /// </summary>
        /// <param name="stats">The statistics.</param>
        void GetStatistics(out PeerStatistics stats);
    }
}
