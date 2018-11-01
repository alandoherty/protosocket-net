using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProtoSocket
{
    /// <summary>
    /// Represents the protocol side.
    /// </summary>
    public enum ProtocolSide
    {
        /// <summary>
        /// This peer has no side.
        /// </summary>
        None,

        /// <summary>
        /// The peer is a server.
        /// </summary>
        Server,

        /// <summary>
        /// The peer is a client.
        /// </summary>
        Client
    }
}
