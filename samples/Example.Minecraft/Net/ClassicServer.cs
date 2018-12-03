using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using ProtoSocket;

namespace Example.Minecraft.Net
{
    /// <summary>
    /// Provides functionality for a classic server.
    /// </summary>
    public class ClassicServer : ProtocolServer<ClassicConnection, ClassicPacket>
    {
        #region Constructors
        public ClassicServer(Uri uri) : base ((p) => new ClassicCoder()) {
            Configure(uri);
        }
        #endregion
    }
}
