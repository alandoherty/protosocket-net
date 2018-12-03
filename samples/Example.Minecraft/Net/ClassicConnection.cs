using System;
using System.Collections.Generic;
using System.Text;
using ProtoSocket;

namespace Example.Minecraft.Net
{
    public class ClassicConnection : ProtocolConnection<ClassicConnection, ClassicPacket>
    {
        public ClassicConnection(ProtocolServer<ClassicConnection, ClassicPacket> server, ProtocolCoderFactory<ClassicPacket> coderFactory) : base(server, coderFactory) {

        }
    }
}
