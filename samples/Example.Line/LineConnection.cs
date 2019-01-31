using ProtoSocket;
using System;
using System.Collections.Generic;
using System.Text;

namespace Example.Line
{
    class LineConnection : ProtocolConnection<LineConnection, string>
    {
        public LineConnection(ProtocolServer<LineConnection, string> server, ProtocolCoderFactory<string> coderFactory, ProtocolMode mode, int bufferSize) : base(server, coderFactory, mode, bufferSize) {
        }
    }
}
