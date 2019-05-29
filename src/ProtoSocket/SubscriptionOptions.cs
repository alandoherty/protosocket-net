using System;
using System.Collections.Generic;
using System.Text;

namespace ProtoSocket
{
    /// <summary>
    /// Defines the subscription options.
    /// </summary>
    [Flags]
    public enum SubscriptionOptions
    {
        /// <summary>
        /// No extra options.
        /// </summary>
        None = 0,

        /// <summary>
        /// Flushes any frames in the receive queue and calls <see cref="IObserver{T}.OnNext(T)"/>.
        /// </summary>
        Flush = 1
    }
}
