using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ProtoSocket
{
    /// <summary>
    /// Represents an interface to upgrade protocols.
    /// </summary>
    public interface IProtocolUpgrader
    {
        /// <summary>
        /// Upgrades the protocol, the class has exclusive control of the peer until the task completes.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <param name="peer">The peer.</param>
        /// <returns></returns>
        Task<Stream> UpgradeAsync(Stream stream, IProtocolPeer peer);
    }
}
