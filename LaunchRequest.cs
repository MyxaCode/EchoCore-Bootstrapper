using System;
using System.Collections.Generic;
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
                return request;
            }

            if (first.StartsWith("echocore-player:", StringComparison.OrdinalIgnoreCase))
            {
                request.Kind = LaunchKind.Player;
                request.Arguments.AddRange(PlayerArguments(Uri.UnescapeDataString(first.Substring("echocore-player:".Length))));
                return request;
            }

            if (first.StartsWith("echocore-studio:", StringComparison.OrdinalIgnoreCase))
            {
                request.Kind = LaunchKind.Studio;
                request.Arguments.AddRange(Split(Uri.UnescapeDataString(first.Substring("echocore-studio:".Length))));
                return request;
            }

            return request;
        }

        private static IEnumerable<string> PlayerArguments(string payload)
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

            var launcherUri = new Uri(placeLauncher);
            var negotiate = launcherUri.GetLeftPart(UriPartial.Authority) + "/Login/Negotiate.ashx";
            return new[] { "-a", negotiate, "-t", ticket, "-j", placeLauncher };
        }

        private static IEnumerable<string> Split(string commandLine)
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
