using System;
using System.Collections.Generic;
using System.Text;
using ProtoSocket;

namespace Example.Chat
{
    public class ChatServer : ProtocolServer<ChatConnection, ChatMessage>
    {
        public ChatServer() : base(new ChatCoder()) {
        }
    }
}
