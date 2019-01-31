using System;
using System.Collections.Generic;
using System.Text;

namespace ProtoSocket
{
    /// <summary>
    /// Represents statistics for a peer.
    /// </summary>
    public struct PeerStatistics
    {
        /// <summary>
        /// Gets the number of received frames.
        /// </summary>
        public long FramesIn { get; internal set; }

        /// <summary>
        /// Gets the number of sent frames.
        /// </summary>
        public long FramesOut { get; internal set; }

        /// <summary>
        /// Gets the number of bytes received.
        /// </summary>
        public long BytesIn { get; internal set; }

        /// <summary>
        /// Gets the number of bytes sent.
        /// </summary>
        public long BytesOut { get; internal set; }

        /// <summary>
        /// Gets the time elapsed during connection and disconnection.
        /// </summary>
        public TimeSpan AliveSpan { get; internal set; }

        /// <summary>
        /// Gets the time the peer connected.
        /// </summary>
        public DateTimeOffset? TimeConnected { get; internal set; }

        /// <summary>
        /// Gets the time the peer disconnected.
        /// </summary>
        public DateTimeOffset? TimeDisconnected { get; internal set; }
    }
}
