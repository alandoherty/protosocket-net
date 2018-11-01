using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Example.Minecraft
{
    /// <summary>
    /// Represents a packet reader.
    /// </summary>
    public class PacketReader
    {
        #region Fields
        private MemoryStream _stream;
        #endregion

        #region Methods
        public short ReadShort() {
            // read short
            byte[] shortBytes = new byte[2];
            _stream.Read(shortBytes, 0, 2);

            // convert to little endian
            Array.Reverse(shortBytes);

            return BitConverter.ToInt16(shortBytes, 0);
        }

        public byte[] ReadByteArray() {
            // read bytes
            byte[] bytes = new byte[1024];
            _stream.Read(bytes, 0, 1024);

            return bytes;
        }

        public byte ReadByte() {
            // read byte
            int b = _stream.ReadByte();

            if (b == -1)
                throw new EndOfStreamException("The end of stream was reached");

            return (byte)b;
        }

        public sbyte ReadSByte() {
            // read byte
            int b = _stream.ReadByte();

            if (b == -1)
                throw new EndOfStreamException("The end of stream was reached");

            return (sbyte)b;
        }

        public string ReadString() {
            // read string
            byte[] strBytes = new byte[64];
            _stream.Read(strBytes, 0, strBytes.Length);

            // find size
            int size = 64;

            for (int i = strBytes.Length - 1; i > -1; i--) {
                if (strBytes[i] == 0x20) {
                    size = i;
                    break;
                }
            }

            return Encoding.ASCII.GetString(strBytes, 0, size).TrimEnd();
        }
        #endregion

        #region Constructors
        public PacketReader(byte[] payload) {
            _stream = new MemoryStream(payload);
        }
        #endregion
    }
}
