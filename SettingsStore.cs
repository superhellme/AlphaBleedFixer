using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace AlphaBleedFixer
{
    internal sealed class AppSettings
    {
        public int Padding { get; set; } = 32;
        public bool Recursive { get; set; } = true;
        public bool CreateBackup { get; set; } = true;
    }

    internal static class SettingsStore
    {
        private const string FileName = "AlphaBleedFixer.ini";

        public static string FilePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName); }
        }

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            if (!File.Exists(FilePath))
            {
                return settings;
            }

            try
            {
                foreach (var rawLine in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    var key = line.Substring(0, separator).Trim();
                    var value = line.Substring(separator + 1).Trim();
                    int padding;
                    bool flag;
                    if (string.Equals(key, "Padding", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out padding)
                        && padding >= 1 && padding <= 128)
                    {
                        settings.Padding = padding;
                    }
                    else if (string.Equals(key, "Recursive", StringComparison.OrdinalIgnoreCase)
                        && bool.TryParse(value, out flag))
                    {
                        settings.Recursive = flag;
                    }
                    else if (string.Equals(key, "CreateBackup", StringComparison.OrdinalIgnoreCase)
                        && bool.TryParse(value, out flag))
                    {
                        settings.CreateBackup = flag;
                    }
                }
            }
            catch
            {
                return new AppSettings();
            }

            return settings;
        }

        public static void Save(AppSettings settings)
        {
            var lines = new[]
            {
                "# Alpha Bleed Fixer settings",
                "Padding=" + settings.Padding.ToString(CultureInfo.InvariantCulture),
                "Recursive=" + settings.Recursive.ToString().ToLowerInvariant(),
                "CreateBackup=" + settings.CreateBackup.ToString().ToLowerInvariant()
            };
            File.WriteAllLines(FilePath, lines, new UTF8Encoding(false));
        }
    }
}
