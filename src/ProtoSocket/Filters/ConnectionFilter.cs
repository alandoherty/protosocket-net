using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProtoSocket.Filters
{
    /// <summary>
    /// Provides an synchronous connection filter implementation 
    /// </summary>
    public class ConnectionFilter : IConnectionFilter
    {
        private Func<IncomingContext, bool> _delegate;

        bool IConnectionFilter.IsAsynchronous {
            get {
                return false;
            }
        }

        bool IConnectionFilter.Filter(IncomingContext incomingCtx) {
            return _delegate(incomingCtx);
        }

        Task<bool> IConnectionFilter.FilterAsync(IncomingContext incomingCtx, CancellationToken cancellationToken) {
            throw new InvalidOperationException();
        }

        /// <summary>
        /// Creates a new connection filter with the specific delegate.
        /// </summary>
        /// <param name="func">The function to filter connections.</param>
        public ConnectionFilter(Func<IncomingContext, bool> func) {
            _delegate = func;
        }
    }
}
