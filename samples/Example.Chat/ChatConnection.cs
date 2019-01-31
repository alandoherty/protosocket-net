using System;
using System.Collections.Generic;
using System.Text;
using ProtoSocket;

namespace Example.Chat
{
    public class ChatConnection : ProtocolConnection<ChatConnection, ChatFrame>
    {
        public string Name { get; private set; } = "Unnamed";

        protected override bool OnReceived(PeerReceivedEventArgs<ChatFrame> e) {
            if (!string.IsNullOrEmpty(e.Frame.Name))
                Name = e.Frame.Name;

            return base.OnReceived(e);
        }

        public ChatConnection(ProtocolServer<ChatConnection, ChatFrame> server, ProtocolCoderFactory<ChatFrame> coderFactory, ProtocolMode mode, int bufferSize) : base(server, coderFactory, mode, bufferSize) {
        }
    }
}
