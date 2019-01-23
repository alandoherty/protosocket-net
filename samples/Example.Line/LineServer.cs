using ProtoSocket;
using System;
using System.Collections.Generic;
using System.Text;

namespace Example.Line
{
    class LineServer : ProtocolServer<LineConnection, string>
    {
        public LineServer() : base((p) => new LineCoder()) {
        }
    }
}
