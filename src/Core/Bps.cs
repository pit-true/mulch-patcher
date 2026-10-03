using System;
using System.IO;

namespace MulchPatcher
{
    internal static class Bps
    {
        internal static byte[] Apply(byte[] source, byte[] patch)
        {
            Binary.CheckPatch(patch);
            PatchReader reader = new PatchReader(patch, 4, patch.Length - 12);
            int sourceSize = Binary.Size(reader.Number());
            int targetSize = Binary.Size(reader.Number());
            reader.Skip(Binary.Size(reader.Number()));
            Binary.CheckSource(source, sourceSize, Binary.UInt32(patch, patch.Length - 12));
            byte[] target = new byte[targetSize];
            int output = 0;
            long sourceCursor = 0, targetCursor = 0;
            while (reader.Position < reader.End)
            {
                ulong action = reader.Number();
                int length = Binary.Size((action >> 2) + 1);
                int type = (int)(action & 3);
                if (length > target.Length - output) throw new InvalidDataException("BPSの書き込み範囲が不正です。");
                if (type == 0)
                {
                    if (output > source.Length - length) throw new InvalidDataException("BPSの元ROM参照範囲が不正です。");
                    Buffer.BlockCopy(source, output, target, output, length);
                }
                else if (type == 1)
                {
                    int position = reader.Position;
                    reader.Skip(length);
                    Buffer.BlockCopy(patch, position, target, output, length);
                }
                else
                {
                    ulong encoded = reader.Number();
                    if ((encoded >> 1) > Binary.MaxSize) throw new InvalidDataException("BPSの相対位置が不正です。");
                    long delta = (long)(encoded >> 1) * ((encoded & 1) == 0 ? 1 : -1);
                    if (type == 2)
                    {
                        sourceCursor += delta;
                        if (sourceCursor < 0 || sourceCursor > source.Length - length)
                            throw new InvalidDataException("BPSのSourceCopy範囲が不正です。");
                        Buffer.BlockCopy(source, (int)sourceCursor, target, output, length);
                        sourceCursor += length;
                    }
                    else
                    {
                        targetCursor += delta;
                        if (targetCursor < 0 || targetCursor >= output)
                            throw new InvalidDataException("BPSが未生成のデータを参照しています。");
                        // Overlap is intentional: a short pattern can expand into a long run.
                        for (int i = 0; i < length; i++) target[output + i] = target[(int)targetCursor + i];
                        targetCursor += length;
                    }
                }
                output += length;
            }
            if (output != target.Length) throw new InvalidDataException("BPSの出力サイズが一致しません。");
            Binary.CheckTarget(target, Binary.UInt32(patch, patch.Length - 8));
            return target;
        }

        private static bool Same(byte[] source, byte[] target, int i)
        { return i < source.Length && source[i] == target[i]; }
        private static bool Repeat(byte[] target, int i)
        { return i + 3 < target.Length && target[i] == target[i + 1] && target[i] == target[i + 2] && target[i] == target[i + 3]; }
        private static void Action(Stream stream, int type, int length)
        { Binary.Number(stream, ((ulong)(length - 1) << 2) | (uint)type); }

        internal static byte[] Create(byte[] source, byte[] target)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                Binary.Text(stream, "BPS1");
                Binary.Number(stream, (ulong)source.Length);
                Binary.Number(stream, (ulong)target.Length);
                Binary.Number(stream, 0); // No metadata.
                int position = 0, targetCursor = 0;
                while (position < target.Length)
                {
                    int start = position;
                    if (Same(source, target, position))
                    {
                        while (position < target.Length && Same(source, target, position)) position++;
                        Action(stream, 0, position - start);
                    }
                    else if (Repeat(target, position))
                    {
                        byte value = target[position];
                        while (position < target.Length && target[position] == value) position++;
                        Action(stream, 1, 1);
                        stream.WriteByte(value);
                        Action(stream, 3, position - start - 1);
                        long delta = (long)start - targetCursor;
                        Binary.Number(stream, ((ulong)Math.Abs(delta) << 1) | (delta < 0 ? 1UL : 0UL));
                        targetCursor = position - 1;
                    }
                    else
                    {
                        do { position++; }
                        while (position < target.Length && !Same(source, target, position) && !Repeat(target, position));
                        Action(stream, 1, position - start);
                        stream.Write(target, start, position - start);
                    }
                }
                return Binary.Finish(stream, source, target);
            }
        }
    }
}
