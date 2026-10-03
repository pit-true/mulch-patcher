using System;
using System.IO;

namespace MulchPatcher
{
    internal static class Ips
    {
        private const int Eof = 0x454F46;
        internal static byte[] Apply(byte[] source, byte[] patch)
        {
            PatchReader reader = new PatchReader(patch, 5, patch.Length);
            using (MemoryStream output = new MemoryStream())
            {
                output.Write(source, 0, source.Length);
                while (true)
                {
                    int offset = reader.Big(3);
                    // WinIPS also accepts a literal record at the address spelled "EOF".
                    // A terminator is unambiguous only at the end (or before a 3-byte size).
                    if (offset == Eof && (reader.End - reader.Position == 0 || reader.End - reader.Position == 3))
                    {
                        if (reader.End - reader.Position == 3) output.SetLength(reader.Big(3));
                        else if (reader.Position != reader.End) throw new InvalidDataException("IPSのEOF以降に不正なデータがあります。");
                        return output.ToArray();
                    }
                    int length = reader.Big(2);
                    if (length == 0)
                    {
                        length = reader.Big(2);
                        byte value = reader.Byte();
                        if (length == 0) throw new InvalidDataException("IPSのRLE長が0です。");
                        output.Position = offset;
                        for (int i = 0; i < length; i++) output.WriteByte(value);
                    }
                    else
                    {
                        int position = reader.Position;
                        reader.Skip(length);
                        output.Position = offset;
                        output.Write(patch, position, length);
                    }
                }
            }
        }

        private static void Record(MemoryStream stream, byte[] target, int start, int length)
        {
            if (start == Eof) { start--; length++; }
            if (start > 0xFFFFFF || length > 65535)
                throw new InvalidDataException("この変更はIPSの範囲を超えています。BPSまたはUPSを選択してください。");
            bool repeated = length >= 4;
            for (int i = 1; repeated && i < length; i++) repeated = target[start + i] == target[start];
            Binary.Big(stream, start, 3);
            if (repeated)
            {
                Binary.Big(stream, 0, 2);
                Binary.Big(stream, length, 2);
                stream.WriteByte(target[start]);
            }
            else
            {
                Binary.Big(stream, length, 2);
                stream.Write(target, start, length);
            }
        }

        internal static byte[] Create(byte[] source, byte[] target)
        {
            if (target.Length < source.Length && target.Length > 0xFFFFFF)
                throw new InvalidDataException("この縮小サイズはIPSで指定できません。BPSまたはUPSを選択してください。");
            using (MemoryStream stream = new MemoryStream())
            {
                Binary.Text(stream, "PATCH");
                int i = 0;
                while (i < target.Length)
                {
                    if (i < source.Length && source[i] == target[i]) { i++; continue; }
                    int start = i;
                    // Leave space for a preceding byte if the offset is the EOF sentinel.
                    int limit = start == Eof ? 65534 : 65535;
                    while (i < target.Length && i - start < limit && (i >= source.Length || source[i] != target[i])) i++;
                    Record(stream, target, start, i - start);
                }
                Binary.Text(stream, "EOF");
                if (target.Length < source.Length) Binary.Big(stream, target.Length, 3);
                return stream.ToArray();
            }
        }
    }
}
