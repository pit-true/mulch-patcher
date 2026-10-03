using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MulchPatcher
{
    internal static class Tests
    {
        private static int assertions;
        private static readonly string Root = Path.Combine(Path.GetTempPath(), "mulch-tests-" + Guid.NewGuid().ToString("N"));
        private static void Assert(bool condition, string message)
        { assertions++; if (!condition) throw new Exception(message); }
        private static void Equal(byte[] a, byte[] b, string message)
        {
            Assert(a.Length == b.Length, message + " (length)");
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) throw new Exception(message + " at " + i);
            assertions++;
        }
        private static void Reject(Action action, string message)
        {
            bool rejected = false;
            try { action(); }
            catch (IOException) { rejected = true; }
            catch (InvalidDataException) { rejected = true; }
            Assert(rejected, message);
        }
        private static byte[] Bytes(string text) { return Encoding.ASCII.GetBytes(text); }
        private static uint Crc(byte[] bytes, int count = -1)
        {
            uint crc = 0xFFFFFFFF;
            if (count < 0) count = bytes.Length;
            for (int i = 0; i < count; i++)
            {
                crc ^= bytes[i];
                for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320U : 0);
            }
            return crc ^ 0xFFFFFFFF;
        }
        private static void U32(Stream stream, uint n) { for (int i = 0; i < 4; i++) stream.WriteByte((byte)(n >> (8 * i))); }
        private static void Num(Stream stream, ulong n)
        {
            do
            {
                byte b = (byte)(n % 128); n /= 128;
                stream.WriteByte((byte)(b | (n == 0 ? 128 : 0)));
                if (n == 0) return;
                n--;
            } while (true);
        }
        private static void Cmd(Stream stream, int kind, int length) { Num(stream, (ulong)((length - 1) * 4 + kind)); }
        private static byte[] BpsFixture(byte[] source, byte[] target, Action<Stream> body, bool metadata = false)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                byte[] magic = Bytes("BPS1"); stream.Write(magic, 0, magic.Length);
                Num(stream, (ulong)source.Length); Num(stream, (ulong)target.Length);
                byte[] meta = metadata ? Bytes("<test/>") : new byte[0]; Num(stream, (ulong)meta.Length); stream.Write(meta, 0, meta.Length);
                body(stream);
                U32(stream, Crc(source)); U32(stream, Crc(target)); U32(stream, Crc(stream.ToArray()));
                return stream.ToArray();
            }
        }
        private static void RoundTrips()
        {
            Random random = new Random(4827);
            for (int iteration = 0; iteration < 250; iteration++)
            {
                byte[] source = new byte[random.Next(0, 1500)], target;
                random.NextBytes(source);
                target = new byte[random.Next(0, 1800)];
                Buffer.BlockCopy(source, 0, target, 0, Math.Min(source.Length, target.Length));
                for (int j = 0; j < 20 && target.Length > 0; j++) target[random.Next(target.Length)] = (byte)random.Next(256);
                if (iteration % 7 == 0) random.NextBytes(target);
                foreach (PatchFormat format in Enum.GetValues(typeof(PatchFormat)))
                {
                    byte[] patch = PatchEngine.Create(source, target, format);
                    Equal(PatchEngine.Apply(source, patch), target, format + " randomized round trip");
                    if (format == PatchFormat.UPS) Equal(PatchEngine.Apply(target, patch), source, "UPS reverse");
                }
            }
            Console.WriteLine("PASS: 750 randomized IPS/BPS/UPS round trips and 250 UPS reverse cases.");
        }
        private static void FormatEdges()
        {
            byte[] source = Bytes("abcdef"), target = Bytes("cdeabcXXXXXXcde");
            byte[] patch = BpsFixture(source, target, stream =>
            {
                Cmd(stream, 2, 3); Num(stream, 4); // SourceCopy +2
                Cmd(stream, 2, 3); Num(stream, 11); // SourceCopy -5
                Cmd(stream, 1, 1); stream.WriteByte((byte)'X');
                Cmd(stream, 3, 5); Num(stream, 12); // TargetCopy +6, overlapping
                Cmd(stream, 3, 3); Num(stream, 23); // TargetCopy -11
            }, true);
            Equal(PatchEngine.Apply(source, patch), target, "BPS copy commands/negative cursors/metadata");
            byte[] sourceRead = BpsFixture(source, Bytes("abc"), stream => Cmd(stream, 0, 3));
            Equal(PatchEngine.Apply(source, sourceRead), Bytes("abc"), "BPS SourceRead");
            Reject(() => PatchEngine.Apply(source, BpsFixture(source, Bytes("x"), stream => { Cmd(stream, 3, 1); Num(stream, 0); })), "BPS unwritten target");
            Reject(() => PatchEngine.Apply(source, BpsFixture(source, Bytes("x"), stream => { Cmd(stream, 2, 1); Num(stream, 2UL * 100); })), "BPS source bounds");
            Reject(() => PatchEngine.Apply(source, BpsFixture(source, Bytes("x"), stream => Cmd(stream, 0, 2))), "BPS output bounds");
            Reject(() => PatchEngine.Apply(source, BpsFixture(source, Bytes("x"), stream => Cmd(stream, 0, 1))), "BPS target CRC");
            Reject(() => PatchEngine.Apply(source, BpsFixture(source, Bytes("xx"), stream => { Cmd(stream, 1, 2); stream.WriteByte(1); })), "BPS truncated literal");
            using (MemoryStream over = new MemoryStream())
            {
                byte[] header = Bytes("BPS1"); over.Write(header, 0, 4);
                for (int i = 0; i < 12; i++) over.WriteByte(127);
                U32(over, Crc(source)); U32(over, 0); U32(over, Crc(over.ToArray()));
                Reject(() => PatchEngine.Apply(source, over.ToArray()), "BPS variable integer overflow");
            }
            // Independent IPS bytes: write AB at 2, then fill 4 bytes with Z at 8, truncate to 10.
            byte[] ips = { 80,65,84,67,72, 0,0,2, 0,2, 65,66, 0,0,8, 0,0, 0,4,90, 69,79,70, 0,0,10 };
            Equal(PatchEngine.Apply(new byte[4], ips), new byte[] { 0,0,65,66,0,0,0,0,90,90 }, "IPS literal/RLE/zero extension/truncate");
            byte[] legacyEof = { 80,65,84,67,72, 69,79,70, 0,1,99, 69,79,70 };
            byte[] legacyOutput = PatchEngine.Apply(new byte[0], legacyEof);
            Assert(legacyOutput.Length == 0x454F47 && legacyOutput[0x454F46] == 99, "WinIPS legacy EOF-address record");
            byte[] sentinelSource = new byte[0x454F46 + 70000], sentinelTarget = (byte[])sentinelSource.Clone();
            for (int i = 0x454F46; i < sentinelTarget.Length; i++) sentinelTarget[i] = 7;
            Equal(PatchEngine.Apply(sentinelSource, PatchEngine.Create(sentinelSource, sentinelTarget, PatchFormat.IPS)), sentinelTarget, "IPS EOF offset and split records");
            foreach (PatchFormat format in new[] { PatchFormat.BPS, PatchFormat.UPS })
            {
                byte[] good = PatchEngine.Create(source, target, format);
                byte[] bad = (byte[])good.Clone(); bad[4] ^= 1;
                Reject(() => PatchEngine.Apply(source, bad), "Patch CRC");
                Reject(() => PatchEngine.Apply(Bytes("abcdeg"), good), "Wrong source CRC");
                Reject(() => PatchEngine.Apply(new byte[2], good), "Wrong source size");
            }
            Reject(() => PatchEngine.Apply(source, Bytes("PATCH")), "IPS missing EOF");
            Reject(() => PatchEngine.Apply(source, Bytes("BPS1")), "BPS missing data");
            Reject(() => PatchEngine.Apply(source, Bytes("UPS1")), "UPS missing data");
            Reject(() => PatchEngine.Apply(source, Bytes("garbage")), "Unknown format");
            // Truncate every valid patch at every position; none may be accepted.
            foreach (PatchFormat format in Enum.GetValues(typeof(PatchFormat)))
            {
                byte[] good = PatchEngine.Create(source, target, format);
                for (int length = 0; length < good.Length; length++)
                {
                    byte[] truncated = new byte[length]; Buffer.BlockCopy(good, 0, truncated, 0, length);
                    Reject(() => PatchEngine.Apply(source, truncated), format + " truncated at " + length);
                }
            }
            Console.WriteLine("PASS: BPS all actions, copy overlap, relative offsets, metadata, IPS RLE/EOF/truncate, malformed data, CRCs.");
        }
        private static void Expansion()
        {
            byte[] source = new byte[16 * 1024 * 1024], target = new byte[32 * 1024 * 1024];
            for (int i = 0; i < source.Length; i++) source[i] = (byte)(i % 251);
            Buffer.BlockCopy(source, 0, target, 0, source.Length);
            for (int i = source.Length; i < target.Length; i++) target[i] = 255;
            target[100] ^= 9; target[20 * 1024 * 1024] = 25; target[target.Length - 1] = 77;
            foreach (PatchFormat format in new[] { PatchFormat.BPS, PatchFormat.UPS })
            {
                byte[] patch = PatchEngine.Create(source, target, format);
                Equal(PatchEngine.Apply(source, patch), target, format + " 16 -> 32 MiB");
                File.WriteAllBytes(Path.Combine(Root, "expanded." + format.ToString().ToLowerInvariant()), patch);
                if (format == PatchFormat.UPS) Equal(PatchEngine.Apply(target, patch), source, "UPS 32 -> 16 MiB");
                if (format == PatchFormat.BPS) Assert(patch.Length < 1024, "BPS repeated expansion is compact");
            }
            Reject(() => PatchEngine.Create(source, target, PatchFormat.IPS), "IPS unrepresentable expansion");
            byte[] lowerChange = (byte[])target.Clone(); lowerChange[8] = 44;
            Equal(PatchEngine.Apply(target, PatchEngine.Create(target, lowerChange, PatchFormat.IPS)), lowerChange, "IPS low offset edit on 32 MiB input preserves tail");
            File.WriteAllBytes(Path.Combine(Root, "original16.bin"), source);
            File.WriteAllBytes(Path.Combine(Root, "modified32.bin"), target);
            Console.WriteLine("PASS: BPS/UPS 16 -> 32 MiB exact bytes, UPS reverse, IPS boundary handling.");
        }
        private static void FileSafety()
        {
            string input = Path.Combine(Root, "元 ROM.bin"), modified = Path.Combine(Root, "変更後 ROM.bin"), patch = Path.Combine(Root, "パッチ.bps"), output = Path.Combine(Root, "出力 ROM.bin");
            byte[] source = Bytes("original data"), target = Bytes("new and larger data!");
            File.WriteAllBytes(input, source); File.WriteAllBytes(modified, target);
            File.SetAttributes(input, FileAttributes.ReadOnly);
            PatchFiles.Create(input, modified, patch, PatchFormat.BPS);
            byte[] patchBefore = File.ReadAllBytes(patch);
            PatchFiles.Apply(input, patch, output);
            Equal(File.ReadAllBytes(input), source, "Read-only original preserved");
            Equal(File.ReadAllBytes(modified), target, "Modified ROM preserved");
            Equal(File.ReadAllBytes(output), target, "Unicode path output");
            Reject(() => PatchFiles.Apply(input, patch, input), "Original output rejected");
            Reject(() => PatchFiles.Apply(input, patch, input.ToUpperInvariant()), "Case alias rejected");
            Reject(() => PatchFiles.Apply(input, patch, patch), "Patch output rejected");
            Reject(() => PatchFiles.Apply(input, patch, output), "Existing output rejected");
            Reject(() => PatchFiles.Create(input, modified, modified, PatchFormat.UPS), "Creation cannot replace modified ROM");
            Reject(() => PatchFiles.Create(input, modified, patch, PatchFormat.BPS), "Existing patch rejected");
            string missing = Path.Combine(Root, "missing.bin");
            Reject(() => PatchFiles.Apply(missing, patch, Path.Combine(Root, "fail.bin")), "Missing input");
            Assert(!File.Exists(missing), "Missing input never created");
            byte[] corrupted = (byte[])patchBefore.Clone(); corrupted[4] ^= 1;
            string bad = Path.Combine(Root, "bad.bps"), badOutput = Path.Combine(Root, "bad-output.bin");
            File.WriteAllBytes(bad, corrupted);
            Reject(() => PatchFiles.Apply(input, bad, badOutput), "Corrupt patch no output");
            Assert(!File.Exists(badOutput), "No output for failed validation");
            Assert(Directory.GetFiles(Root, ".mulch-*.tmp").Length == 0, "No incomplete output artifacts");
            Equal(File.ReadAllBytes(input), source, "Original unchanged after failures");
            Equal(File.ReadAllBytes(patch), patchBefore, "Patch unchanged after failures");
            Console.WriteLine("PASS: new-file-only output, read-only/Unicode input, failure atomicity, unchanged input and patch.");
        }

        private static void External(string executable, string arguments)
        {
            using (Process process = Process.Start(new ProcessStartInfo(executable, arguments) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
            {
                string stdout = process.StandardOutput.ReadToEnd(), stderr = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(60000)) { process.Kill(); throw new Exception("External patcher timeout"); }
                Assert(process.ExitCode == 0, "External patcher failed: " + stdout + stderr);
            }
        }
        private static string Quote(string path) { return "\"" + path + "\""; }
        private static void Cli()
        {
            string executable = typeof(MainForm).Assembly.Location;
            string original = Path.Combine(Root, "元 ROM.bin"), modified = Path.Combine(Root, "変更後 ROM.bin");
            string patch = Path.Combine(Root, "cli.bps"), output = Path.Combine(Root, "cli-output.bin");
            External(executable, "--create bps " + Quote(original) + " " + Quote(modified) + " " + Quote(patch));
            External(executable, "--apply " + Quote(patch) + " " + Quote(original) + " " + Quote(output));
            Equal(File.ReadAllBytes(output), File.ReadAllBytes(modified), "CLI real creation/application");
            using (Process process = Process.Start(new ProcessStartInfo(executable,
                "--apply " + Quote(patch) + " " + Quote(original) + " " + Quote(original))
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true }))
            {
                process.StandardError.ReadToEnd(); process.StandardOutput.ReadToEnd(); process.WaitForExit();
                Assert(process.ExitCode == 1, "CLI failures return exit code 1");
            }
            Equal(File.ReadAllBytes(original), Bytes("original data"), "CLI original preserved");
            Console.WriteLine("PASS: actual release EXE CLI creation/application, Unicode paths and failure exit code.");
        }
        private static void Interop(string flips, string winips, string nupsCore)
        {
            string original = Path.Combine(Root, "original16.bin"), modified = Path.Combine(Root, "modified32.bin");
            byte[] expected = File.ReadAllBytes(modified);
            foreach (string kind in new[] { "bps", "ups" })
            {
                string output = Path.Combine(Root, "flips-expanded-" + kind + ".bin");
                External(flips, "--apply --exact " + Quote(Path.Combine(Root, "expanded." + kind)) + " " + Quote(original) + " " + Quote(output));
                Equal(File.ReadAllBytes(output), expected, "Flips applies Mulch " + kind + " 16 -> 32 MiB");
            }
            string flipsBps = Path.Combine(Root, "flips-created.bps");
            External(flips, "--create --bps --exact " + Quote(original) + " " + Quote(modified) + " " + Quote(flipsBps));
            Equal(PatchEngine.Apply(File.ReadAllBytes(original), File.ReadAllBytes(flipsBps)), expected, "Mulch applies Flips delta BPS 16 -> 32 MiB");
            string smallOriginal = Path.Combine(Root, "small-original.bin"), smallModified = Path.Combine(Root, "small-modified.bin");
            byte[] a = new byte[128 * 1024], b = new byte[140 * 1024];
            new Random(423).NextBytes(a); Buffer.BlockCopy(a, 0, b, 0, a.Length);
            for (int i = 65500; i < 72000; i++) b[i] = 100;
            File.WriteAllBytes(smallOriginal, a); File.WriteAllBytes(smallModified, b);
            string mulchIps = Path.Combine(Root, "mulch-created.ips");
            PatchFiles.Create(smallOriginal, smallModified, mulchIps, PatchFormat.IPS);
            External(flips, "--apply " + Quote(mulchIps) + " " + Quote(smallOriginal) + " " + Quote(Path.Combine(Root, "flips-ips.bin")));
            Equal(File.ReadAllBytes(Path.Combine(Root, "flips-ips.bin")), b, "Flips applies Mulch IPS");
            External(winips, "/a " + Quote(mulchIps) + " " + Quote(smallOriginal));
            Equal(File.ReadAllBytes(Path.ChangeExtension(mulchIps, ".bin")), b, "WinIPS applies Mulch IPS");
            string flipsIps = Path.Combine(Root, "flips-created.ips");
            External(flips, "--create --ips " + Quote(smallOriginal) + " " + Quote(smallModified) + " " + Quote(flipsIps));
            Equal(PatchEngine.Apply(a, File.ReadAllBytes(flipsIps)), b, "Mulch applies Flips IPS");
            External(winips, "/c " + Quote(smallOriginal) + " " + Quote(smallModified));
            Equal(PatchEngine.Apply(a, File.ReadAllBytes(Path.ChangeExtension(smallModified, ".ips"))), b, "Mulch applies WinIPS IPS");
            Assembly core = Assembly.Load(File.ReadAllBytes(nupsCore));
            Type ups = core.GetType("Nintenlord.Hacking.Core.UPSfile");
            object nupsPatch = ups.GetConstructor(new[] { typeof(byte[]), typeof(byte[]) }).Invoke(new object[] { a, b });
            string nupsUps = Path.Combine(Root, "nups-created.ups");
            ups.GetMethod("WriteToFile").Invoke(nupsPatch, new object[] { nupsUps });
            Equal(PatchEngine.Apply(a, File.ReadAllBytes(nupsUps)), b, "Mulch applies NUPS UPS");
            string mulchUps = Path.Combine(Root, "mulch-created.ups");
            PatchFiles.Create(smallOriginal, smallModified, mulchUps, PatchFormat.UPS);
            object loaded = ups.GetConstructor(new[] { typeof(string) }).Invoke(new object[] { mulchUps });
            Equal((byte[])ups.GetMethod("Apply", new[] { typeof(byte[]) }).Invoke(loaded, new object[] { a }), b, "NUPS applies Mulch UPS");
            Console.WriteLine("PASS: bidirectional Flips BPS/IPS, WinIPS IPS, NUPS UPS compatibility; real 16 -> 32 MiB expansion through Flips.");
        }
        private static void Gui(string screenshot)
        {
            Application.EnableVisualStyles();
            using (MainForm form = new MainForm(new string[0]))
            {
                Assert(form.Text == "Mulch Patcher", "GUI title");
                form.Show(); Application.DoEvents();
                BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
                Func<string, object> field = name => typeof(MainForm).GetField(name, fields).GetValue(form);
                TextBox first = (TextBox)field("first"), second = (TextBox)field("second"), output = (TextBox)field("output"), log = (TextBox)field("log");
                RadioButton create = (RadioButton)field("create"), apply = (RadioButton)field("apply");
                ComboBox format = (ComboBox)field("format");
                Assert(format.Text == "BPS" && !format.Enabled, "BPS default / format automatic when applying");
                first.Text = Path.Combine(Root, "expanded.bps"); second.Text = Path.Combine(Root, "original16.bin");
                Assert(output.Text == Path.Combine(Root, "expanded.bin"), "WinIPS-style default output");
                string custom = Path.Combine(Root, "my-output.bin"); output.Text = custom;
                first.Text = Path.Combine(Root, "expanded.ups");
                Assert(output.Text == custom, "Manual output path preserved");
                create.Checked = true;
                Assert(first.Text.Length == 0 && second.Text.Length == 0 && output.Text.Length == 0, "Mode change clears input roles");
                Assert(format.Enabled, "Creation format can be selected");
                first.Text = Path.Combine(Root, "元 ROM.bin"); second.Text = Path.Combine(Root, "変更後 ROM.bin");
                Assert(output.Text == Path.Combine(Root, "変更後 ROM.bps"), "Creation default filename");
                format.Text = "UPS";
                Assert(output.Text == Path.Combine(Root, "変更後 ROM.ups"), "Format selection changes extension");
                MethodInfo execute = typeof(MainForm).GetMethod("Execute", fields);
                WaitGui((Task)execute.Invoke(form, new object[0]));
                string created = output.Text;
                Assert(File.Exists(created) && log.Text.Contains("作成・検証完了"), "GUI creates and verifies UPS patch");
                apply.Checked = true;
                first.Text = created; second.Text = Path.Combine(Root, "元 ROM.bin");
                string applied = output.Text;
                WaitGui((Task)execute.Invoke(form, new object[0]));
                Equal(File.ReadAllBytes(applied), File.ReadAllBytes(Path.Combine(Root, "変更後 ROM.bin")), "GUI apply exact output");
                Assert(log.Text.Contains("適用完了"), "GUI apply success message");
                WaitGui((Task)execute.Invoke(form, new object[0]));
                Assert(log.Text.Contains("処理できませんでした") && log.Text.Contains("存在"), "GUI refuses existing output");
                // Screenshot a clean initial dialog, without machine-specific paths.
                format.Text = "BPS";
                create.Checked = true; apply.Checked = true;
                log.Text = "ファイルを選択するか、この画面へドラッグ＆ドロップしてください。\r\n元ROMと既存ファイルは上書きしません。";
                if (screenshot != null)
                {
                    using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                    { form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height)); bitmap.Save(screenshot); }
                }
                form.Close();
            }
            Console.WriteLine("PASS: Windows Forms startup, default paths, mode/format switching, real GUI creation/application, error handling.");
        }
        private static void WaitGui(Task task)
        {
            Stopwatch timer = Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                Application.DoEvents(); Thread.Sleep(5);
                if (timer.Elapsed.TotalSeconds > 30) throw new Exception("GUI task timeout");
            }
            task.GetAwaiter().GetResult();
        }
        [STAThread]
        private static int Main(string[] args)
        {
            Directory.CreateDirectory(Root);
            try
            {
                Assert(Crc(Bytes("123456789")) == 0xCBF43926, "Independent CRC32 reference");
                RoundTrips(); FormatEdges(); Expansion(); FileSafety(); Cli();
                if (args.Length >= 4 && args[0] == "--interop") Interop(args[1], args[2], args[3]);
                string screenshot = args.Length >= 6 && args[4] == "--screenshot" ? args[5] : null;
                Gui(screenshot);
                Console.WriteLine("ALL PASS: " + assertions + " assertions. Test artifacts: " + Root);
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); Console.Error.WriteLine("Test artifacts: " + Root); return 1; }
        }
    }
}
