using System;
using System.Collections.Generic;
using System.Text;

namespace ProtoSocket
{
    /// <summary>
    /// A factory function to create peer coders.
    /// </summary>
    /// <typeparam name="TFrame">The frame type.</typeparam>
    /// <param name="peer">The peer.</param>
    /// <returns>The coder.</returns>
    public delegate IProtocolCoder<TFrame> ProtocolCoderFactory<TFrame>(ProtocolPeer<TFrame> peer)
        where TFrame : class;
}
