using System;
using System.Collections.Generic;
using System.Text;

namespace ProtoSocket
{
    /// <summary>
    /// Provides configuration to an instantiated peer.
    /// </summary>
    public sealed class PeerConfiguration
    {
        #region Defaults
        /// <summary>
        /// The default active peer configuration.
        /// </summary>
        public static readonly PeerConfiguration Active = new PeerConfiguration() {
            Mode = ProtocolMode.Active
        };

        /// <summary>
        /// The default passive peer configuration.
        /// </summary>
        public static readonly PeerConfiguration Passive = new PeerConfiguration() {
            Mode = ProtocolMode.Passive
        };
        #endregion

        /// <summary>
        /// Gets or sets the internal read/write buffer sizes. You should only increase this if your application benefits.
        /// </summary>
        public int BufferSize { get; set; } = 8192;

        /// <summary>
        /// Gets or sets the protocol mode.
        /// </summary>
        public ProtocolMode Mode { get; set; } = ProtocolMode.Active;

        /// <summary>
        /// Creates a new peer configuration object.
        /// </summary>
        /// <param name="protocolMode">The protocol mode.</param>
        /// <param name="bufferSize">The buffer size.</param>
        public PeerConfiguration(ProtocolMode protocolMode = ProtocolMode.Active, int bufferSize = 8192) {
            BufferSize = bufferSize;
            Mode = ProtocolMode.Active;
        }
    }
}
