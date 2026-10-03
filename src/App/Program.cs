using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MulchPatcher
{
    internal static class Program
    {
        [DllImport("kernel32.dll")] private static extern bool AttachConsole(uint processId);
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0].StartsWith("--"))
            {
                AttachConsole(0xFFFFFFFF);
                try
                {
                    if (args.Length == 4 && args[0] == "--apply")
                    {
                        PatchResult result = PatchFiles.Apply(args[2], args[1], args[3]);
                        Console.WriteLine("OK: " + result.Format + " " + result.SourceSize + " -> " + result.TargetSize + " bytes / " + result.OutputPath);
                    }
                    else if (args.Length == 5 && args[0] == "--create")
                    {
                        PatchFormat format;
                        if (!Enum.TryParse(args[1], true, out format) || !Enum.IsDefined(typeof(PatchFormat), format))
                            throw new ArgumentException("Format must be ips, bps, or ups.");
                        PatchResult result = PatchFiles.Create(args[2], args[3], args[4], format);
                        Console.WriteLine("OK: " + result.Format + " / " + result.OutputPath);
                    }
                    else if (args.Length == 1 && (args[0] == "--help" || args[0] == "--version"))
                    {
                        Console.WriteLine("Mulch Patcher 1.0.0\n--apply <patch> <original> <new-output>\n--create <ips|bps|ups> <original> <modified> <new-patch>\nExisting files are never overwritten.");
                    }
                    else throw new ArgumentException("Invalid arguments. Use --help.");
                    return 0;
                }
                catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args));
            return 0;
        }
    }
}
