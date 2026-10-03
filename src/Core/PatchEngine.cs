using System;
using System.IO;
using System.Text;

namespace MulchPatcher
{
    public static class PatchEngine
    {
        public static PatchFormat Detect(byte[] patch)
        {
            if (patch.Length >= 5 && Encoding.ASCII.GetString(patch, 0, 5) == "PATCH") return PatchFormat.IPS;
            if (patch.Length >= 4)
            {
                string magic = Encoding.ASCII.GetString(patch, 0, 4);
                if (magic == "BPS1") return PatchFormat.BPS;
                if (magic == "UPS1") return PatchFormat.UPS;
            }
            throw new InvalidDataException("IPS / BPS / UPSのパッチを選択してください。形式はファイルの内容から判定します。");
        }

        public static byte[] Apply(byte[] source, byte[] patch)
        {
            Binary.Size((ulong)source.Length);
            switch (Detect(patch))
            {
                case PatchFormat.IPS: return Ips.Apply(source, patch);
                case PatchFormat.BPS: return Bps.Apply(source, patch);
                case PatchFormat.UPS: return Ups.Apply(source, patch);
                default: throw new InvalidDataException();
            }
        }

        public static byte[] Create(byte[] source, byte[] target, PatchFormat format)
        {
            Binary.Size((ulong)source.Length);
            Binary.Size((ulong)target.Length);
            switch (format)
            {
                case PatchFormat.IPS: return Ips.Create(source, target);
                case PatchFormat.BPS: return Bps.Create(source, target);
                case PatchFormat.UPS: return Ups.Create(source, target);
                default: throw new ArgumentOutOfRangeException("format");
            }
        }
    }
}
