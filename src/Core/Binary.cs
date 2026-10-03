using System;
using System.IO;
using System.Text;

namespace MulchPatcher
{
    public enum PatchFormat { IPS, BPS, UPS }

    internal sealed class PatchReader
    {
        internal readonly byte[] Data;
        internal readonly int End;
        internal int Position;
        internal PatchReader(byte[] data, int start, int end) { Data = data; Position = start; End = end; }
        internal byte Byte()
        {
            if (Position >= End) throw new InvalidDataException("パッチが途中で途切れています。");
            return Data[Position++];
        }
        internal int Big(int count)
        {
            int value = 0;
            while (count-- > 0) value = (value << 8) | Byte();
            return value;
        }
        internal ulong Number()
        {
            ulong value = 0, factor = 1;
            try
            {
                checked
                {
                    while (true)
                    {
                        byte b = Byte();
                        value += (ulong)(b & 127) * factor;
                        if ((b & 128) != 0) return value;
                        factor *= 128;
                        value += factor;
                    }
                }
            }
            catch (OverflowException) { throw new InvalidDataException("パッチ内の数値が大きすぎます。"); }
        }
        internal void Skip(int length)
        {
            if (length < 0 || length > End - Position) throw new InvalidDataException("パッチ内のデータ長が不正です。");
            Position += length;
        }
    }

    internal static class Binary
    {
        // Bound allocations for malformed patches; comfortably above 32 MiB GBA ROMs.
        internal const int MaxSize = 512 * 1024 * 1024;
        private static readonly uint[] CrcTable = MakeTable();
        private static uint[] MakeTable()
        {
            uint[] table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                uint n = i;
                for (int bit = 0; bit < 8; bit++) n = (n >> 1) ^ ((n & 1) != 0 ? 0xEDB88320U : 0);
                table[i] = n;
            }
            return table;
        }
        internal static uint Crc(byte[] bytes, int count = -1)
        {
            if (count < 0) count = bytes.Length;
            uint value = 0xFFFFFFFF;
            for (int i = 0; i < count; i++) value = CrcTable[(value ^ bytes[i]) & 255] ^ (value >> 8);
            return value ^ 0xFFFFFFFF;
        }
        internal static uint UInt32(byte[] data, int offset)
        {
            return (uint)(data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16 | data[offset + 3] << 24);
        }
        internal static int Size(ulong value)
        {
            if (value > MaxSize) throw new InvalidDataException("対応するファイルサイズは512 MiBまでです。");
            return (int)value;
        }
        internal static void Number(Stream stream, ulong value)
        {
            while (true)
            {
                byte b = (byte)(value & 127);
                value >>= 7;
                if (value == 0) { stream.WriteByte((byte)(b | 128)); return; }
                stream.WriteByte(b);
                value--;
            }
        }
        internal static void Big(Stream stream, int value, int count)
        {
            for (int i = count - 1; i >= 0; i--) stream.WriteByte((byte)(value >> (8 * i)));
        }
        internal static void UInt32(Stream stream, uint value)
        {
            for (int i = 0; i < 4; i++) stream.WriteByte((byte)(value >> (8 * i)));
        }
        internal static void Text(Stream stream, string value)
        {
            byte[] data = Encoding.ASCII.GetBytes(value);
            stream.Write(data, 0, data.Length);
        }
        internal static byte[] Finish(MemoryStream stream, byte[] source, byte[] target)
        {
            UInt32(stream, Crc(source));
            UInt32(stream, Crc(target));
            byte[] data = stream.ToArray();
            UInt32(stream, Crc(data));
            return stream.ToArray();
        }
        internal static void CheckPatch(byte[] patch)
        {
            if (patch.Length < 18 || Crc(patch, patch.Length - 4) != UInt32(patch, patch.Length - 4))
                throw new InvalidDataException("パッチのCRC32が一致しません。破損している可能性があります。");
        }
        internal static void CheckSource(byte[] source, int size, uint crc)
        {
            if (source.Length != size || Crc(source) != crc)
                throw new InvalidDataException("元ROMが一致しません。必要なサイズ: " + size + " bytes / CRC32: " + crc.ToString("X8") +
                    "\n選択したROM: " + source.Length + " bytes / CRC32: " + Crc(source).ToString("X8"));
        }
        internal static void CheckTarget(byte[] target, uint crc)
        {
            if (Crc(target) != crc) throw new InvalidDataException("適用後のCRC32が一致しません。出力は保存していません。");
        }
    }
}
