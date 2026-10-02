using System;
using System.IO;
using System.IO.Ports;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Aimmy2.External
{
    /// <summary>
    /// KMNet protocol constants, packet structures and command codes
    /// for KMBox Net communication.
    /// </summary>
    public static class KMNetProtocol
    {
        public enum KMNetCommand : byte
        {
            MouseMove = 0x01,
            MouseClick = 0x02,
            MouseRelease = 0x03,
            MouseScroll = 0x04,
            KeepAlive = 0xFF
        }

        public enum MouseButton : byte
        {
            None = 0x00,
            Left = 0x01,
            Right = 0x02,
            Middle = 0x04
        }

        public struct KMNetPacket
        {
            public KMNetCommand Command;
            public short DeltaX;
            public short DeltaY;
            public MouseButton Button;
            public byte Checksum;

            public byte[] ToByteArray()
            {
                byte[] buffer = new byte[7];
                buffer[0] = (byte)Command;
                BitConverter.GetBytes((short)DeltaX).CopyTo(buffer, 1);
                BitConverter.GetBytes((short)DeltaY).CopyTo(buffer, 3);
                buffer[5] = (byte)Button;
                buffer[6] = Checksum;
                return buffer;
            }
        }
    }
}
