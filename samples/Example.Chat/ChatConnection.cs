using System;
using System.Collections.Generic;
using System.Text;
using ProtoSocket;

namespace Example.Chat
{
    public class ChatConnection : ProtocolConnection<ChatConnection, ChatMessage>
    {
        public ChatConnection(ProtocolServer<ChatConnection, ChatMessage> server, IProtocolCoder<ChatMessage> coder) : base(server, coder) {
        }
    }
}
