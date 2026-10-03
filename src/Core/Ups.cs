using System;
using System.IO;

namespace MulchPatcher
{
    internal static class Ups
    {
        internal static byte[] Apply(byte[] source, byte[] patch)
        {
            Binary.CheckPatch(patch);
            PatchReader reader = new PatchReader(patch, 4, patch.Length - 12);
            int originalSize = Binary.Size(reader.Number()), modifiedSize = Binary.Size(reader.Number());
            uint originalCrc = Binary.UInt32(patch, patch.Length - 12), modifiedCrc = Binary.UInt32(patch, patch.Length - 8);
            uint crc = Binary.Crc(source);
            bool forward = source.Length == originalSize && crc == originalCrc;
            bool reverse = source.Length == modifiedSize && crc == modifiedCrc;
            if (!forward && !reverse) { Binary.CheckSource(source, originalSize, originalCrc); throw new InvalidDataException(); }
            int size = forward ? modifiedSize : originalSize;
            int extent = Math.Max(originalSize, modifiedSize);
            byte[] target = new byte[size];
            Buffer.BlockCopy(source, 0, target, 0, Math.Min(source.Length, target.Length));
            long offset = 0;
            while (reader.Position < reader.End)
            {
                ulong skip = reader.Number();
                if (skip > (ulong)extent || offset > extent - (long)skip) throw new InvalidDataException("UPSの位置が不正です。");
                offset += (long)skip;
                while (true)
                {
                    byte delta = reader.Byte();
                    if (delta == 0) { offset++; break; }
                    if (offset >= extent) throw new InvalidDataException("UPSの書き込み範囲が不正です。");
                    if (offset < target.Length) target[(int)offset] ^= delta;
                    offset++;
                }
            }
            Binary.CheckTarget(target, forward ? modifiedCrc : originalCrc);
            return target;
        }

        private static byte At(byte[] data, int i) { return i < data.Length ? data[i] : (byte)0; }
        internal static byte[] Create(byte[] source, byte[] target)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                Binary.Text(stream, "UPS1");
                Binary.Number(stream, (ulong)source.Length);
                Binary.Number(stream, (ulong)target.Length);
                int position = 0, cursor = 0, extent = Math.Max(source.Length, target.Length);
                while (position < extent)
                {
                    if (At(source, position) == At(target, position)) { position++; continue; }
                    Binary.Number(stream, (ulong)(position - cursor));
                    while (position < extent && At(source, position) != At(target, position))
                    {
                        stream.WriteByte((byte)(At(source, position) ^ At(target, position)));
                        position++;
                    }
                    stream.WriteByte(0);
                    position++; // The terminator also advances the logical ROM cursor.
                    cursor = position;
                }
                return Binary.Finish(stream, source, target);
            }
        }
    }
}
