using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Example.Minecraft
{
    /// <summary>
    /// Provides functionality to write classic packets.
    /// </summary>
    public class PacketWriter
    {
        #region Fields
        private MemoryStream _stream;
        #endregion

        #region Methods
        public void WriteShort(short s) {
            byte[] shortBytes = BitConverter.GetBytes(s);
            Array.Reverse(shortBytes);
            _stream.Write(shortBytes, 0, 2);
        }

        public void WriteByte(byte b) {
            _stream.WriteByte(b);
        }

        public void WriteSByte(sbyte b) {
            _stream.WriteByte((byte)b);
        }

        public void WriteString(string str) {
            if (str.Length > 64)
                throw new ArgumentException("The string cannot be longer than 64 characters");

            // get string
            byte[] rawBytes = new byte[64];

            for (int i = 0; i < rawBytes.Length; i++) {
                rawBytes[i] = 0x20;
            }

            byte[] strBytes = Encoding.ASCII.GetBytes(str);

            Buffer.BlockCopy(strBytes, 0, rawBytes, 0, strBytes.Length);
            _stream.Write(rawBytes, 0, 64);
        }

        public void WriteByteArray(byte[] arr) {
            if (arr.Length != 1024)
                throw new ArgumentException("The byte array must be 1024 bytes in length");

            _stream.Write(arr, 0, arr.Length);
        }

        public byte[] ToArray() {
            return _stream.ToArray();
        }
        #endregion

        #region Constructors
        /// <summary>
        /// Creates a new packet writer.
        /// </summary>
        public PacketWriter() {
            _stream = new MemoryStream();
        }
        #endregion
    }
}
