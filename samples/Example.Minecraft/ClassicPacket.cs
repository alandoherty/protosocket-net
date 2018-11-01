using System;
using System.Collections.Generic;
using System.Text;

namespace Example.Minecraft
{
    /// <summary>
    /// Represents a classic packet.
    /// </summary>
    public class ClassicPacket
    {
        public PacketId Id { get; set; }
        public byte[] Payload { get; set; }
    }
}
