using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProtoSocket
{
    /// <summary>
    /// Represents a connection filter.
    /// </summary>
    public interface IConnectionFilter
    {
        /// <summary>
        /// Gets if this filter is asynchronous.
        /// </summary>
        bool IsAsynchronous { get; }

        /// <summary>
        /// Filter the incoming connection, blocking this operation will prevent additional clients from being accepted.
        /// </summary>
        /// <param name="incomingCtx">The incoming connect context.</param>
        /// <remarks>This will be called if <see cref="IsAsynchronous"/> returns false.</remarks>
        /// <returns>If to accept the connection.</returns>
        bool Filter(IncomingContext incomingCtx);

        /// <summary>
        /// Filter the incoming connection.
        /// </summary>
        /// <param name="incomingCtx">The incoming connect context.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <remarks>This will be called if <see cref="IsAsynchronous"/> returns true.</remarks>
        /// <returns>The task to determine if to accept the connection.</returns>
        Task<bool> FilterAsync(IncomingContext incomingCtx, CancellationToken cancellationToken = default);
    }
}
