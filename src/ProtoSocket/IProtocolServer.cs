﻿using System;
using System.Collections.Generic;
using System.Text;

namespace ProtoSocket
{
    /// <summary>
    /// Defines a base interface for protocol servers.
    /// </summary>
    public interface IProtocolServer : IDisposable
    {
        /// <summary>
        /// Gets or sets the connection filter, if any.
        /// </summary>
        IConnectionFilter Filter { get; set; }

        /// <summary>
        /// Gets the endpoint the server is listening on.
        /// </summary>
        IEnumerable<Uri> Endpoints { get; }

        /// <summary>
        /// Gets the number of connections.
        /// </summary>
        int Count { get; }

        /// <summary>
        /// Configures the listening endpoint.
        /// </summary>
        /// <param name="uriString">The URI string.</param>
        void Configure(string uriString);

        /// <summary>
        /// Configures the listening endpoint.
        /// </summary>
        /// <param name="uri">The URI string.</param>
        void Configure(Uri uri);

        /// <summary>
        /// Starts listening for connections.
        /// </summary>
        void Start();
    }
}
