using ProtoSocket;
using ProtoSocket.Upgraders;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Example.Ssl
{
    public class SslClient : ProtocolClient<string>
    {
        public override async Task ConnectAsync(Uri uri)
        {
            // connect to the server
            await base.ConnectAsync(uri).ConfigureAwait(false);

            // upgrade, this isn't setup to verify trust correctly and will blindly accept any certificate
            // DO NOT USE IN PRODUCTION
            try {
                SslUpgrader upgrader = new SslUpgrader(uri.Host);
                upgrader.RemoteValidationCallback = (o, crt, cert, sse) => {
                    return true;
                };

                await UpgradeAsync(upgrader).ConfigureAwait(false);
            } catch(Exception) {
                Dispose();
                throw;
            }

            // enable active mode so frames start being read by ProtoSocket
            Mode = ProtocolMode.Active;
        }

        public SslClient() : base(new SslCoder(), new PeerConfiguration(ProtocolMode.Passive)) {
        }
    }
}
