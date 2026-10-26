using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;

namespace EchoCore.Bootstrapper
{
    // Audio has no window. The native game controls capture, which starts muted.
    internal static class VoiceWorker
    {
        internal static bool ValidToken(string token)
        {
            return token != null && token.Length > 40 && token.Length <= 2048 && token.Count(c => c == '.') == 1 &&
                token.All(c => c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_' || c == '-' || c == '.');
        }

        internal static Ticket ReadTicket(string token)
        {
            if (!ValidToken(token)) return null;
            try
            {
                var body = token.Substring(0, token.IndexOf('.')).Replace('-', '+').Replace('_', '/');
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(body.PadRight((body.Length + 3) / 4 * 4, '=')));
                var ticket = new JavaScriptSerializer().Deserialize<Ticket>(json);
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                return ticket != null && ticket.version == 2 && ticket.userId > 0 && ticket.userId <= 9007199254740991 &&
                    Guid.TryParse(ticket.roomId, out var room) && room == Guid.Empty && ticket.expiresAt > now && ticket.expiresAt <= now + 12 * 3600
                    ? ticket : null;
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException || e is InvalidOperationException || e is OverflowException)
            { return null; }
        }

        internal static void Start(string folder, int playerPid, string token)
        {
            var ticket = ReadTicket(token);
            var executable = Path.Combine(folder, "Voice", "EchoCoreVoice.exe");
            if (ticket == null || !File.Exists(executable)) return;
            string profile = null;
            try
            {
                profile = Path.Combine(Path.GetTempPath(), "ecvoice-" + Guid.NewGuid().ToString("N") + ".json");
                var security = new FileSecurity();
                var owner = WindowsIdentity.GetCurrent().User;
                security.SetAccessRuleProtection(true, false);
                security.SetOwner(owner);
                security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, AccessControlType.Allow));
                using (var file = new FileStream(profile, FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096,
                    FileOptions.None, security))
                using (var writer = new StreamWriter(file, new UTF8Encoding(false)))
                    writer.Write(new JavaScriptSerializer().Serialize(new
                    {
                        server = "wss://www.echocr.xyz/voice", token, userId = ticket.userId, roomId = Guid.Empty,
                        expiresAt = ticket.expiresAt, gameProcessId = playerPid
                    }));
                var info = new ProcessStartInfo
                {
                    FileName = executable, Arguments = "--profile \"" + profile + "\"", WorkingDirectory = Path.GetDirectoryName(executable),
                    UseShellExecute = false, CreateNoWindow = true
                };
                using (var process = Process.Start(info)) { }
                profile = null; // The audio host consumes and removes the private profile.
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.ComponentModel.Win32Exception || e is System.Security.SecurityException)
            { /* Voice failure must not prevent the game from starting. */ }
            finally
            {
                if (profile != null) try { File.Delete(profile); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        internal sealed class Ticket
        {
            public int version { get; set; }
            public long userId { get; set; }
            public string roomId { get; set; }
            public long expiresAt { get; set; }
        }
    }
}
