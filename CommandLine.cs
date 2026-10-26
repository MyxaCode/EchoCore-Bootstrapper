using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace EchoCore.Bootstrapper
{
    internal static class CommandLine
    {
        internal static string Join(IEnumerable<string> arguments) => string.Join(" ", arguments.Select(Quote));
        internal static string Quote(string argument)
        {
            if (argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c == '"')) return argument;
            var result = new StringBuilder("\"");
            var slashes = 0;
            foreach (var c in argument)
            {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                result.Append(c); slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
    }
}
