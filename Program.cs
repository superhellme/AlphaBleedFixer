using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace AlphaBleedFixer
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], "--batch", StringComparison.OrdinalIgnoreCase))
            {
                Environment.ExitCode = RunBatch(args.Skip(1).ToArray());
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args));
        }

        private static int RunBatch(string[] args)
        {
            var options = new ProcessOptions();
            var paths = new List<string>();

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (string.Equals(arg, "--recursive", StringComparison.OrdinalIgnoreCase))
                {
                    options.Recursive = true;
                }
                else if (string.Equals(arg, "--no-recursive", StringComparison.OrdinalIgnoreCase))
                {
                    options.Recursive = false;
                }
                else if (string.Equals(arg, "--no-backup", StringComparison.OrdinalIgnoreCase))
                {
                    options.CreateBackup = false;
                }
                else if (string.Equals(arg, "--padding", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    int padding;
                    if (!int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out padding) || padding < 1 || padding > 128)
                    {
                        return 2;
                    }
                    options.Padding = padding;
                }
                else
                {
                    paths.Add(arg);
                }
            }

            if (paths.Count == 0)
            {
                return 2;
            }

            try
            {
                var files = AlphaBleedProcessor.CollectPngFiles(paths, options.Recursive);
                var failed = 0;
                foreach (var file in files)
                {
                    var result = AlphaBleedProcessor.ProcessFile(file, options);
                    Console.WriteLine(result.ToLogLine());
                    if (result.Status == ProcessStatus.Failed)
                    {
                        failed++;
                    }
                }
                return failed == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }
    }
}
