using System;
using System.Collections.Generic;
using System.Text;
using ProtoSocket;

namespace Example.Chat
{
    public class ChatClient : ProtocolClient<ChatMessage>
    {
        public ChatClient() : base(new ChatCoder()) {
        }
    }
}
