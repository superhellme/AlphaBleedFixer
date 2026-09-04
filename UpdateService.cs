using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace AlphaBleedFixer
{
    internal sealed class AvailableUpdate
    {
        public Version Version { get; set; }
        public string VersionLabel { get; set; }
        public string DownloadUrl { get; set; }
        public string ReleasePageUrl { get; set; }
    }

    internal static class UpdateService
    {
        private const string LatestReleasePageUrl = "https://github.com/superhellme/AlphaBleedFixer/releases/latest";
        private const string ReleaseTagPathPrefix = "/superhellme/AlphaBleedFixer/releases/tag/";
        private const string ReleaseDownloadBaseUrl = "https://github.com/superhellme/AlphaBleedFixer/releases/download/";
        private const string ReleaseAssetName = "AlphaBleedFixer.zip";
        private const string UserAgent = "AlphaBleedFixer-Updater";
        private const string SettingsFileName = "AlphaBleedFixer.ini";

        public static Version CurrentVersion
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version; }
        }

        public static string CurrentVersionLabel
        {
            get { return "v" + CurrentVersion.ToString(3); }
        }

        public static async Task<AvailableUpdate> FindUpdateAsync()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(LatestReleasePageUrl);
            request.Method = "HEAD";
            request.AllowAutoRedirect = false;
            request.UserAgent = UserAgent;
            request.Timeout = 15000;

            Uri releaseUri;
            var responseTask = request.GetResponseAsync();
            if (await Task.WhenAny(responseTask, Task.Delay(15000)) != responseTask)
            {
                request.Abort();
                throw new TimeoutException("GitHubの更新確認がタイムアウトしました。");
            }

            using (var response = (HttpWebResponse)await responseTask)
            {
                var statusCode = (int)response.StatusCode;
                if (statusCode < 300 || statusCode >= 400 || string.IsNullOrWhiteSpace(response.Headers[HttpResponseHeader.Location]))
                {
                    throw new InvalidDataException("GitHubから最新リリースの場所を取得できませんでした。");
                }
                releaseUri = new Uri(new Uri(LatestReleasePageUrl), response.Headers[HttpResponseHeader.Location]);
            }

            if (!string.Equals(releaseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(releaseUri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                || !releaseUri.AbsolutePath.StartsWith(ReleaseTagPathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("GitHubから不正なリリースURLが返されました。");
            }

            var tag = Uri.UnescapeDataString(releaseUri.AbsolutePath.Substring(ReleaseTagPathPrefix.Length));
            if (string.IsNullOrWhiteSpace(tag) || tag.IndexOf('/') >= 0)
            {
                throw new InvalidDataException("最新リリースのタグを取得できませんでした。");
            }

            Version latestVersion;
            var versionText = tag.Trim().TrimStart('v', 'V');
            if (!Version.TryParse(versionText, out latestVersion) || latestVersion <= CurrentVersion)
            {
                return null;
            }

            var downloadUrl = ReleaseDownloadBaseUrl + Uri.EscapeDataString(tag) + "/" + ReleaseAssetName;

            return new AvailableUpdate
            {
                Version = latestVersion,
                VersionLabel = "v" + latestVersion.ToString(3),
                DownloadUrl = downloadUrl,
                ReleasePageUrl = releaseUri.AbsoluteUri
            };
        }

        public static async Task PrepareAndLaunchUpdaterAsync(AvailableUpdate update)
        {
            if (update == null)
            {
                throw new ArgumentNullException("update");
            }

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var updateId = Guid.NewGuid().ToString("N");
            var updateDirectory = Path.Combine(Path.GetTempPath(), "AlphaBleedFixer-update-" + updateId);
            var payloadDirectory = Path.Combine(updateDirectory, "payload");
            var archivePath = Path.Combine(updateDirectory, ReleaseAssetName);
            var scriptPath = Path.Combine(Path.GetTempPath(), "AlphaBleedFixer-apply-update-" + updateId + ".ps1");

            Directory.CreateDirectory(payloadDirectory);
            try
            {
                using (var client = CreateWebClient())
                {
                    await client.DownloadFileTaskAsync(new Uri(update.DownloadUrl), archivePath);
                }

                ExtractArchiveSafely(archivePath, payloadDirectory);
                var newExecutablePath = Path.Combine(payloadDirectory, "AlphaBleedFixer.exe");
                if (!File.Exists(newExecutablePath))
                {
                    throw new InvalidDataException("アップデートZIPにAlphaBleedFixer.exeがありません。");
                }

                File.WriteAllText(scriptPath, CreateUpdaterScript(), new UTF8Encoding(false));
                var executablePath = Assembly.GetExecutingAssembly().Location;
                var targetDirectory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var process = Process.GetCurrentProcess();
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " + Quote(scriptPath)
                        + " -ProcessId " + process.Id
                        + " -SourceDirectory " + Quote(payloadDirectory)
                        + " -TargetDirectory " + Quote(targetDirectory)
                        + " -ExecutablePath " + Quote(executablePath)
                        + " -CleanupDirectory " + Quote(updateDirectory),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(startInfo);
            }
            catch
            {
                TryDeleteDirectory(updateDirectory);
                TryDeleteFile(scriptPath);
                throw;
            }
        }

        private static WebClient CreateWebClient()
        {
            var client = new WebClient();
            client.Headers[HttpRequestHeader.UserAgent] = UserAgent;
            return client;
        }

        private static void ExtractArchiveSafely(string archivePath, string destinationDirectory)
        {
            var destinationRoot = Path.GetFullPath(destinationDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                foreach (var entry in archive.Entries)
                {
                    var targetPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
                    if (!targetPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("アップデートZIPに不正なパスが含まれています。");
                    }

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(targetPath);
                        continue;
                    }

                    var parent = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }
                    using (var input = entry.Open())
                    using (var output = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        input.CopyTo(output);
                    }
                }
            }
        }

        private static string CreateUpdaterScript()
        {
            return @"param(
    [int]$ProcessId,
    [string]$SourceDirectory,
    [string]$TargetDirectory,
    [string]$ExecutablePath,
    [string]$CleanupDirectory
)
$ErrorActionPreference = 'Stop'
try {
    Wait-Process -Id $ProcessId -ErrorAction SilentlyContinue
    Get-ChildItem -LiteralPath $SourceDirectory | Where-Object { $_.Name -ne 'AlphaBleedFixer.ini' } | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $TargetDirectory -Recurse -Force
    }
    $errorLog = Join-Path $TargetDirectory 'AlphaBleedFixer-update-error.log'
    if (Test-Path -LiteralPath $errorLog) {
        Remove-Item -LiteralPath $errorLog -Force
    }
    Start-Process -FilePath $ExecutablePath
}
catch {
    $_ | Out-File -LiteralPath (Join-Path $TargetDirectory 'AlphaBleedFixer-update-error.log') -Encoding UTF8
    Start-Process -FilePath $ExecutablePath
}
finally {
    if (Test-Path -LiteralPath $CleanupDirectory) {
        Remove-Item -LiteralPath $CleanupDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
}
";
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

    }
}
