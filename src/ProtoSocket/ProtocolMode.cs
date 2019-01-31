using System;
using System.Collections.Generic;
using System.Text;

namespace ProtoSocket
{
    /// <summary>
    /// Defines the possible protocol modes. This determines how the peer handles incoming data.
    /// </summary>
    public enum ProtocolMode
    {
        /// <summary>
        /// In active mode, the peer will continually read data from the opposing peer and pass it to the coder.
        /// </summary>
        Active,

        /// <summary>
        /// In passive mode, the peer will only read data when you call <see cref="ProtocolPeer{TFrame}.ReceiveAsync(byte[], System.Threading.CancellationToken)"/>/<see cref="ProtocolPeer{TFrame}.RequestAsync(TFrame, System.Threading.CancellationToken)"/> explicitly.
        /// </summary>
        Passive
    }
}
