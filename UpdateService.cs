using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
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
        private const string LatestReleaseApiUrl = "https://api.github.com/repos/superhellme/AlphaBleedFixer/releases/latest";
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
            string json;
            using (var client = CreateWebClient())
            {
                json = await client.DownloadStringTaskAsync(new Uri(LatestReleaseApiUrl));
            }

            GitHubRelease release;
            var serializer = new DataContractJsonSerializer(typeof(GitHubRelease));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                release = (GitHubRelease)serializer.ReadObject(stream);
            }
            if (release == null || release.draft || release.prerelease || string.IsNullOrWhiteSpace(release.tag_name))
            {
                return null;
            }

            Version latestVersion;
            var versionText = release.tag_name.Trim().TrimStart('v', 'V');
            if (!Version.TryParse(versionText, out latestVersion) || latestVersion <= CurrentVersion)
            {
                return null;
            }

            var asset = (release.assets ?? new GitHubAsset[0])
                .FirstOrDefault(candidate => string.Equals(candidate.name, ReleaseAssetName, StringComparison.OrdinalIgnoreCase));
            if (asset == null || string.IsNullOrWhiteSpace(asset.browser_download_url))
            {
                throw new InvalidDataException("リリースに " + ReleaseAssetName + " がありません。");
            }

            var downloadUri = new Uri(asset.browser_download_url);
            if (!string.Equals(downloadUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(downloadUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("アップデートのダウンロード先が不正です。");
            }

            return new AvailableUpdate
            {
                Version = latestVersion,
                VersionLabel = "v" + latestVersion.ToString(3),
                DownloadUrl = asset.browser_download_url,
                ReleasePageUrl = release.html_url
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
            client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
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

        [DataContract]
        private sealed class GitHubRelease
        {
            [DataMember(Name = "tag_name")]
            public string tag_name { get; set; }

            [DataMember(Name = "html_url")]
            public string html_url { get; set; }

            [DataMember(Name = "draft")]
            public bool draft { get; set; }

            [DataMember(Name = "prerelease")]
            public bool prerelease { get; set; }

            [DataMember(Name = "assets")]
            public GitHubAsset[] assets { get; set; }
        }

        [DataContract]
        private sealed class GitHubAsset
        {
            [DataMember(Name = "name")]
            public string name { get; set; }

            [DataMember(Name = "browser_download_url")]
            public string browser_download_url { get; set; }
        }
    }
}
