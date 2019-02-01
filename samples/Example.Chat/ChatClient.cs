using System;
using System.Collections.Generic;
using System.Text;
using ProtoSocket;

namespace Example.Chat
{
    public class ChatClient : ProtocolClient<ChatFrame>
    {
        public ChatClient() : base(new ChatCoder()) {
        }
    }
}
