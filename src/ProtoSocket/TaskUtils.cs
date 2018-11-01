using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProtoSocket
{
    static class TaskUtils
    {
        public static async Task WithCancellation(this Task task, CancellationToken cancellationToken) {
            var tcs = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(
                        s => ((TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
                if (task != await Task.WhenAny(task, tcs.Task).ConfigureAwait(false))
                    throw new OperationCanceledException(cancellationToken);
            await task.ConfigureAwait(false);
        }

        public static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancellationToken) {
            var tcs = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(
                        s => ((TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
                if (task != await Task.WhenAny(task, tcs.Task).ConfigureAwait(false))
                    throw new OperationCanceledException(cancellationToken);
            return await task.ConfigureAwait(false);
        }
    }
}
