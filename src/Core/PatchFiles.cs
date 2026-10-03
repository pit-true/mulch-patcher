using System;
using System.IO;

namespace MulchPatcher
{
    public sealed class PatchResult
    {
        public PatchFormat Format { get; internal set; }
        public int SourceSize { get; internal set; }
        public int TargetSize { get; internal set; }
        public uint TargetCrc { get; internal set; }
        public string OutputPath { get; internal set; }
    }

    public static class PatchFiles
    {
        public static byte[] Read(string path)
        {
            using (FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Binary.Size((ulong)input.Length);
                byte[] bytes = new byte[(int)input.Length];
                int position = 0;
                while (position < bytes.Length)
                {
                    int read = input.Read(bytes, position, bytes.Length - position);
                    if (read == 0) throw new EndOfStreamException("読み込み中にファイルサイズが変わりました。");
                    position += read;
                }
                return bytes;
            }
        }

        public static void ValidateOutput(string path, params string[] inputs)
        {
            string output = Path.GetFullPath(path);
            foreach (string input in inputs)
                if (string.Equals(output, Path.GetFullPath(input), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("元ROMやパッチと同じパスには保存できません。別の名前を指定してください。");
            if (File.Exists(output) || Directory.Exists(output))
                throw new IOException("保存先がすでに存在します。別の名前を指定してください。");
        }

        public static void WriteNew(string path, byte[] data, params string[] inputs)
        {
            ValidateOutput(path, inputs);
            string output = Path.GetFullPath(path);
            // Complete the write before publishing the destination. Move never replaces an existing file.
            string temporary = Path.Combine(Path.GetDirectoryName(output), ".mulch-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(data, 0, data.Length);
                    stream.Flush(true);
                }
                File.Move(temporary, output);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public static PatchResult Apply(string sourcePath, string patchPath, string outputPath)
        {
            ValidateOutput(outputPath, sourcePath, patchPath);
            byte[] source = Read(sourcePath), patch = Read(patchPath);
            PatchFormat format = PatchEngine.Detect(patch);
            byte[] target = PatchEngine.Apply(source, patch);
            WriteNew(outputPath, target, sourcePath, patchPath);
            return new PatchResult { Format = format, SourceSize = source.Length, TargetSize = target.Length,
                TargetCrc = Binary.Crc(target), OutputPath = Path.GetFullPath(outputPath) };
        }

        public static PatchResult Create(string sourcePath, string targetPath, string outputPath, PatchFormat format)
        {
            ValidateOutput(outputPath, sourcePath, targetPath);
            byte[] source = Read(sourcePath), target = Read(targetPath);
            byte[] patch = PatchEngine.Create(source, target, format);
            // Verify the patch against the expected target before saving it.
            byte[] verified = PatchEngine.Apply(source, patch);
            if (verified.Length != target.Length) throw new InvalidDataException("作成したパッチのサイズ検証に失敗しました。");
            for (int i = 0; i < target.Length; i++)
                if (verified[i] != target[i]) throw new InvalidDataException("作成したパッチの内容検証に失敗しました。");
            WriteNew(outputPath, patch, sourcePath, targetPath);
            return new PatchResult { Format = format, SourceSize = source.Length, TargetSize = target.Length,
                TargetCrc = Binary.Crc(target), OutputPath = Path.GetFullPath(outputPath) };
        }
    }
}
