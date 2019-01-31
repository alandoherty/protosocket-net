using System;
using System.Collections.Generic;
using System.Text;
using ProtoSocket;

namespace Example.Minecraft.Net
{
    public class ClassicConnection : ProtocolConnection<ClassicConnection, ClassicPacket>
    {
        public ClassicConnection(ProtocolServer<ClassicConnection, ClassicPacket> server, ProtocolCoderFactory<ClassicPacket> coderFactory, ProtocolMode mode, int bufferSize) : base(server, coderFactory, mode, bufferSize) {

        }
    }
}
