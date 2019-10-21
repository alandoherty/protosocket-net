using ProtoSocket;
using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Example.Ssl
{
    public class SslServer : ProtocolServer<SslConnection, string>
    {
        private X509Certificate2 _cert;

        internal X509Certificate2 Certificate {
            get {
                return _cert;
            }
        }

        public SslServer(X509Certificate2 cert) : base(p => new SslCoder(), new PeerConfiguration(ProtocolMode.Passive)) {
            _cert = cert;
        }
    }
}
