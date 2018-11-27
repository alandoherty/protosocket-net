using Example.Minecraft.Net.Packets;
using System;
using System.Collections.Generic;
using System.Text;

namespace Example.Minecraft.Net
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
