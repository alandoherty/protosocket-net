using System;
using System.Collections.Generic;
using System.Text;

namespace ProtoSocket
{
    /// <summary>
    /// Defines the possible accept modes.
    /// </summary>
    public enum AcceptMode
    {
        /// <summary>
        /// In active mode, the server will continually accept new connections in the background.
        /// </summary>
        Active,

        /// <summary>
        /// In passive mode, the server will only accept new connections when you call <see cref="ProtocolServer{TConnection, TFrame}.AcceptAsync(System.Threading.CancellationToken)"/> explicitly.
        /// </summary>
        Passive
    }
}
