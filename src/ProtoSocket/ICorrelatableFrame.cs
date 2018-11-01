using System;
using System.Collections.Generic;
using System.Text;

namespace ProtoSocket
{
    /// <summary>
    /// Represents an interface for matching request and response frames.
    /// </summary>
    public interface ICorrelatableFrame<TFrame>
        where TFrame : class
    {
        /// <summary>
        /// Gets the corellation id for this frame.
        /// </summary>
        object CorrelationId { get;  }

        /// <summary>
        /// Gets if this frame has a correlation id.
        /// </summary>
        bool HasCorrelation { get; }

        /// <summary>
        /// Checks if the frame should be correlated. This method is key for describing if your protocol has explicit request/response, or if
        /// any frame can initiate a request. In a explicit request/response system all frames should be dropped, regardless of if a correlation is found. In other systems you
        /// may want frames which cannot be correlated to be treated as requests, instead of being dropped.
        /// </summary>
        /// <param name="dropFrame">If the frame should be dropped when no correlation is found.</param>
        /// <returns>If the request should be correlated.</returns>
        bool ShouldCorrelate(out bool dropFrame);

        /// <summary>
        /// Adds the correlation to this frame.
        /// </summary>
        /// <param name="correlationId">The correlation id.</param>
        /// <returns></returns>
        TFrame WithCorrelation(object correlationId);
    }
}
