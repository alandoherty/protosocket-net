using System;
using System.Collections.Generic;
using System.Text;
using ProtoSocket;

namespace Example.Chat
{
    public class ChatConnection : ProtocolConnection<ChatConnection, ChatMessage>
    {
        public ChatConnection(ProtocolServer<ChatConnection, ChatMessage> server, ProtocolCoderFactory<ChatMessage> coderFactory) : base(server, coderFactory) {
        }
    }
}
