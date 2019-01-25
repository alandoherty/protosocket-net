using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProtoSocket.Filters
{
    /// <summary>
    /// Provides an asyncronous connection filter implementation 
    /// </summary>
    public class AsyncConnectionFilter : IConnectionFilter
    {
        private Func<IncomingContext, CancellationToken, Task<bool>> _delegate;

        bool IConnectionFilter.IsAsynchronous {
            get {
                return true;
            }
        }

        bool IConnectionFilter.Filter(IncomingContext incomingCtx) {
            throw new InvalidOperationException();
        }

        Task<bool> IConnectionFilter.FilterAsync(IncomingContext incomingCtx, CancellationToken cancellationToken) {
            return _delegate(incomingCtx, cancellationToken);
        }

        /// <summary>
        /// Creates a new connection filter with the specific delegate.
        /// </summary>
        /// <param name="func">The function to filter connections.</param>
        public AsyncConnectionFilter(Func<IncomingContext, CancellationToken, Task<bool>> func) {
            _delegate = func;
        }
    }
}
