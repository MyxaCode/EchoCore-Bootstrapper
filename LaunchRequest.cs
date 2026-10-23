using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace EchoCore.Bootstrapper
{
    internal enum LaunchKind
    {
        Install,
        Player,
        Studio,
        Uninstall,
    }

    internal sealed class LaunchRequest
    {
        public LaunchKind Kind { get; private set; }
        public string Year { get; private set; } = string.Empty;
        public List<string> Arguments { get; } = new List<string>();

        public static LaunchRequest Parse(string[] args)
        {
            var request = new LaunchRequest { Kind = LaunchKind.Install };
            if (args.Length == 0)
                return request;

            var first = args[0];
            if (first == "--uninstall")
            {
                request.Kind = LaunchKind.Uninstall;
                return request;
            }

            if (first == "--studio")
            {
                request.Kind = LaunchKind.Studio;
                if (args.Length > 1)
                    request.Year = ClientYear(args[1]);
                return request;
            }

            if (first.StartsWith("echocore-player:", StringComparison.OrdinalIgnoreCase))
            {
                request.Kind = LaunchKind.Player;
                request.Arguments.AddRange(PlayerArguments(Uri.UnescapeDataString(first.Substring("echocore-player:".Length)), out var year));
                request.Year = year;
                return request;
            }

            if (first.StartsWith("echocore-studio:", StringComparison.OrdinalIgnoreCase))
            {
                request.Kind = LaunchKind.Studio;
                var arguments = Split(Uri.UnescapeDataString(first.Substring("echocore-studio:".Length)));
                if (arguments.Count > 0 && arguments[0].StartsWith("year:", StringComparison.OrdinalIgnoreCase))
                {
                    request.Year = ClientYear(arguments[0].Substring("year:".Length));
                    arguments.RemoveAt(0);
                }
                request.Arguments.AddRange(arguments);
                return request;
            }

            return request;
        }

        private static string ClientYear(string clientVersion)
        {
            var digits = new string(clientVersion.TakeWhile(char.IsDigit).ToArray());
            return digits.Length == 4 && digits != "2021" ? digits : string.Empty;
        }

        private static IEnumerable<string> PlayerArguments(string payload, out string year)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in payload.Split('+'))
            {
                var separator = part.IndexOf(':');
                if (separator > 0)
                    fields[part.Substring(0, separator)] = part.Substring(separator + 1);
            }

            if (!fields.TryGetValue("gameinfo", out var ticket) || !fields.TryGetValue("placelauncherurl", out var placeLauncher))
                throw new InvalidOperationException("The link from the website is incomplete. Press Play again.");

            year = fields.TryGetValue("clientversion", out var clientVersion) ? ClientYear(clientVersion) : string.Empty;

            var launcherUri = new Uri(placeLauncher);
            var negotiate = launcherUri.GetLeftPart(UriPartial.Authority) + "/Login/Negotiate.ashx";
            return new[] { "-a", negotiate, "-t", ticket, "-j", placeLauncher };
        }

        private static List<string> Split(string commandLine)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            var quoted = false;
            var hasToken = false;
            foreach (var c in commandLine)
            {
                if (c == '"')
                {
                    quoted = !quoted;
                    hasToken = true;
                }
                else if (char.IsWhiteSpace(c) && !quoted)
                {
                    if (hasToken)
                        result.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
                else
                {
                    current.Append(c);
                    hasToken = true;
                }
            }
            if (hasToken)
                result.Add(current.ToString());
            return result;
        }
    }
}
