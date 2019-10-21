using ProtoSocket;
using ProtoSocket.Upgraders;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Example.Ssl
{
    public class SslConnection : ProtocolConnection<SslConnection, string>
    {
        protected async override void OnConnected(PeerConnectedEventArgs<string> e)
        {
            // call connected, we can't upgrade until the peer has been marked as connected
            base.OnConnected(e);

            // upgrade, if an error occurs log
            try {
                SslUpgrader upgrader = new SslUpgrader(((SslServer)Server).Certificate);

                await UpgradeAsync(upgrader);
            } catch(Exception ex) {
                Console.Error.WriteLine($"err:{e.Peer.RemoteEndPoint}: failed to upgrade SSL: {ex.ToString()}");
                return;
            }

            // enable active mode so frames start being read by ProtoSocket
            Mode = ProtocolMode.Active;
        }

        protected override bool OnReceived(PeerReceivedEventArgs<string> e)
        {
            // log message
            Console.WriteLine($"msg:{e.Peer.RemoteEndPoint}: {e.Frame}");

            // send an empty frame reply, we send as a fire and forget for the purposes of simplicity
            // any exception will be lost to the ether
            Task _ = SendAsync(string.Empty);

            // indicates that we observed this frame, it will still call Notify/etc and other handlers but it won't add to the receive queue
            return true;
        }

        public SslConnection(ProtocolServer<SslConnection, string> server, ProtocolCoderFactory<string> coderFactory, PeerConfiguration configuration = null) : base(server, coderFactory, configuration) {
        }
    }
}
