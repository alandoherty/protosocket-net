using System;
using System.Collections.Generic;
using System.Text;

namespace Example.Minecraft.Net.Packets
{
    /// <summary>
    /// Defines the possible packet ID's.
    /// </summary>
    public enum PacketId
    {
        Identification = 0x00,
        Ping = 0x01,
        LevelInitialize = 0x02,
        LevelDataChunk = 0x03,
        LevelFinalize = 0x04,
        AskBlock = 0x05,
        SetBlock = 0x06,
        SpawnPlayer = 0x07,
        PositionAngle = 0x08,
        PositionAngleUpdate = 0x09,
        DespawnPlayer = 0x0C,
        Message = 0x0D,
        DisconnectPlayer = 0x0E,
        SetUserType = 0x0F
    }
}
