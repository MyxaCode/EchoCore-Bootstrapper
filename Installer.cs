using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace EchoCore.Bootstrapper
{
    internal sealed class Installer
    {
        private const string SetupUrl = "https://www.echocr.xyz/setup/";
        private const string PlayerExecutable = "RobloxPlayerBeta.exe";
        private const string StudioExecutable = "RobloxStudioBeta.exe";

        private static readonly string Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EchoCore");
        private static readonly string VersionsRoot = Path.Combine(Root, "Versions");
        private static readonly string InstalledBootstrapper = Path.Combine(Root, "EchoCoreBootstrapper.exe");

        private readonly ProgressWindow _window;
        private readonly HttpClient _http;

        public Installer(ProgressWindow window)
        {
            _window = window;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
            _http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("EchoCoreBootstrapper/1.1");
        }

        public async Task RunAsync(LaunchRequest request)
        {
            Directory.CreateDirectory(VersionsRoot);
            _window.SetStatus("Checking for updates...");
            var manifest = await GetManifestAsync();

            switch (request.Kind)
            {
                case LaunchKind.Player:
                    Launch(await EnsurePackageAsync("player" + request.Year, manifest), PlayerExecutable, request.Arguments);
                    break;
                case LaunchKind.Studio:
                    Launch(await EnsurePackageAsync("studio" + request.Year, manifest), StudioExecutable, request.Arguments);
                    break;
                default:
                    await EnsurePackageAsync("player", manifest);
                    await EnsurePackageAsync("studio", manifest);
                    InstallSelf();
                    Register(manifest);
                    _window.ShowMessage("EchoCore is installed. Press Play on www.echocr.xyz to join a game.");
                    break;
            }

            if (request.Kind != LaunchKind.Install && !File.Exists(InstalledBootstrapper))
            {
                InstallSelf();
                Register(manifest);
            }
            else if (request.Kind != LaunchKind.Install)
            {
                CreateStudioShortcuts(manifest);
            }
        }

        private async Task<Dictionary<string, Package>> GetManifestAsync()
        {
            var json = await _http.GetStringAsync(SetupUrl + "manifest.json");
            return new JavaScriptSerializer().Deserialize<Dictionary<string, Package>>(json);
        }

        private async Task<string> EnsurePackageAsync(string name, Dictionary<string, Package> manifest)
        {
            var year = name.Substring(6);
            if (!manifest.TryGetValue(name, out var package))
                throw new InvalidOperationException(year.Length > 0
                    ? "The " + year + " client is not available yet."
                    : "The server did not offer the " + name + " files.");

            var folder = Path.Combine(VersionsRoot, name + "-" + package.version);
            if (File.Exists(Path.Combine(folder, ".complete")))
                return folder;

            var title = (name.StartsWith("player") ? "EchoCore" : "EchoCore Studio") + (year.Length > 0 ? " " + year : string.Empty);
            var archive = Path.Combine(VersionsRoot, package.file + ".download");
            await DownloadAsync(SetupUrl + package.file, archive, package.size, "Downloading " + title + "...");

            _window.SetStatus("Verifying " + title + "...");
            var hash = await Task.Run(() => Sha256(archive));
            if (!string.Equals(hash, package.sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(archive);
                throw new InvalidOperationException("The downloaded files are damaged. Please try again.");
            }

            _window.SetStatus("Installing " + title + "...");
            var staging = folder + ".staging";
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
            await Task.Run(() => ZipFile.ExtractToDirectory(archive, staging));
            File.Delete(archive);
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
            Directory.Move(staging, folder);
            File.WriteAllText(Path.Combine(folder, ".complete"), package.version);

            RemoveOldVersions(name, folder);
            return folder;
        }

        private async Task DownloadAsync(string url, string destination, long expectedSize, string status)
        {
            _window.SetStatus(status);
            using (var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? expectedSize;
                using (var input = await response.Content.ReadAsStreamAsync())
                using (var output = File.Create(destination))
                {
                    var buffer = new byte[81920];
                    long received = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await output.WriteAsync(buffer, 0, read);
                        received += read;
                        if (total > 0)
                            _window.SetProgress((int)(received * 100 / total));
                    }
                }
            }
            _window.SetProgress(-1);
        }

        private static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2")));
        }

        private static void RemoveOldVersions(string name, string current)
        {
            foreach (var folder in Directory.GetDirectories(VersionsRoot, name + "-*"))
            {
                if (string.Equals(folder, current, StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    Directory.Delete(folder, true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private static void Launch(string folder, string executable, IEnumerable<string> arguments)
        {
            var path = Path.Combine(folder, executable);
            var joined = string.Join(" ", arguments.Select(Quote));
            if (MediumIntegrity.IsHigh())
            {
                try
                {
                    MediumIntegrity.Start(path, Quote(path) + " " + joined, folder);
                    return;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
            }
            var info = new ProcessStartInfo
            {
                FileName = path,
                Arguments = joined,
                WorkingDirectory = folder,
                UseShellExecute = false,
            };
            Process.Start(info);
        }

        private static string Quote(string argument)
        {
            if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
                return argument;
            return "\"" + argument.Replace("\"", "\\\"") + "\"";
        }

        private static void InstallSelf()
        {
            var current = Process.GetCurrentProcess().MainModule.FileName;
            if (string.Equals(Path.GetFullPath(current), Path.GetFullPath(InstalledBootstrapper), StringComparison.OrdinalIgnoreCase))
                return;
            Directory.CreateDirectory(Root);
            File.Copy(current, InstalledBootstrapper, true);
        }

        private static void Register(Dictionary<string, Package> manifest)
        {
            var command = "\"" + InstalledBootstrapper + "\" \"%1\"";
            RegisterProtocol("echocore-player", "EchoCore Player", command);
            RegisterProtocol("echocore-studio", "EchoCore Studio", command);

            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\EchoCore"))
            {
                key.SetValue("DisplayName", "EchoCore");
                key.SetValue("Publisher", "EchoCore");
                key.SetValue("DisplayIcon", InstalledBootstrapper);
                key.SetValue("InstallLocation", Root);
                key.SetValue("UninstallString", "\"" + InstalledBootstrapper + "\" --uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }

            CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "EchoCore Studio", "--studio");
            CreateStudioShortcuts(manifest);
        }

        private static string StudioShortcutName(string year)
        {
            return "EchoCore Studio" + (year.Length > 0 ? " " + year : string.Empty);
        }

        private static void CreateStudioShortcuts(Dictionary<string, Package> manifest)
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\EchoCore"))
            {
                var created = new HashSet<string>(((key.GetValue("DesktopStudios") as string) ?? string.Empty)
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                foreach (var name in manifest.Keys.Where(k => k.StartsWith("studio", StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal))
                {
                    var year = name.Substring("studio".Length);
                    if (year.Length > 0 && (year.Length != 4 || !year.All(char.IsDigit)))
                        continue;
                    if (!created.Add(name))
                        continue;
                    CreateShortcut(desktop, StudioShortcutName(year), year.Length > 0 ? "--studio " + year : "--studio");
                }
                key.SetValue("DesktopStudios", string.Join(",", created.OrderBy(n => n, StringComparer.Ordinal)));
            }
        }

        private static void RegisterProtocol(string scheme, string title, string command)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + scheme))
            {
                key.SetValue("", "URL:" + title);
                key.SetValue("URL Protocol", "");
                using (var icon = key.CreateSubKey("DefaultIcon"))
                    icon.SetValue("", "\"" + InstalledBootstrapper + "\",0");
                using (var open = key.CreateSubKey(@"shell\open\command"))
                    open.SetValue("", command);
            }
        }

        private static void CreateShortcut(string folder, string name, string arguments)
        {
            var path = Path.Combine(folder, name + ".lnk");
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                return;
            dynamic shell = Activator.CreateInstance(shellType);
            dynamic shortcut = shell.CreateShortcut(path);
            shortcut.TargetPath = InstalledBootstrapper;
            shortcut.Arguments = arguments;
            shortcut.WorkingDirectory = Root;
            shortcut.IconLocation = InstalledBootstrapper + ",0";
            shortcut.Save();
        }

        public static void Uninstall()
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\echocore-player", false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\echocore-studio", false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\EchoCore", false);
            var shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "EchoCore Studio.lnk");
            if (File.Exists(shortcut))
                File.Delete(shortcut);
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (Directory.Exists(desktop))
            {
                foreach (var file in Directory.GetFiles(desktop, "EchoCore Studio*.lnk"))
                    File.Delete(file);
            }
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\EchoCore", false);
            if (Directory.Exists(VersionsRoot))
                Directory.Delete(VersionsRoot, true);

            var cleanup = new StringBuilder()
                .Append("/c timeout /t 2 /nobreak >nul & del /f /q \"").Append(InstalledBootstrapper).Append('"')
                .Append(" & rmdir \"").Append(Root).Append('"')
                .ToString();
            Process.Start(new ProcessStartInfo("cmd.exe", cleanup) { CreateNoWindow = true, UseShellExecute = false });
        }

        private sealed class Package
        {
            public string version { get; set; }
            public string file { get; set; }
            public string sha256 { get; set; }
            public long size { get; set; }
        }
    }
}
