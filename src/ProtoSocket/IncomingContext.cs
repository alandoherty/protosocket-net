using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace ProtoSocket
{
    /// <summary>
    /// Represents an incoming connection context.
    /// </summary>
    public struct IncomingContext
    {
        /// <summary>
        /// Gets the network endpoint.
        /// </summary>
        public EndPoint RemoteEndPoint { get; internal set; }

        /// <summary>
        /// Gets the server.
        /// </summary>
        public IProtocolServer Server { get; internal set; }

        /// <summary>
        /// Gets the string representation 
        /// </summary>
        /// <returns></returns>
        public override string ToString() {
            return RemoteEndPoint.ToString();
        }
    }
}
