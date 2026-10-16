using System.IO;
using System.Text.RegularExpressions;

namespace RelayDesk;

public static class FlowsealStrategies
{
    public static List<Strategy> Load(string executable)
    {
        if (!File.Exists(executable)) throw new InvalidOperationException("Укажи существующий bin/winws.exe на вкладке «Ядра».");
        var bin = Path.GetDirectoryName(Path.GetFullPath(executable))!;
        if (!Path.GetFileName(bin).Equals("bin", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Выбери bin/winws.exe из распакованного набора Flowseal.");
        var root = Directory.GetParent(bin)!.FullName;
        var result = new List<Strategy>(); var seen = new HashSet<string>();
        foreach (var file in Directory.GetFiles(root, "general*.bat").Order(StringComparer.OrdinalIgnoreCase))
        {
            var args = ParseTcp(File.ReadAllText(file), bin);
            if (args.Length > 0 && seen.Add(args)) result.Add(new("Flowseal / " + Path.GetFileNameWithoutExtension(file), args));
        }
        if (result.Count == 0) throw new InvalidOperationException("В наборе не найдены поддерживаемые TCP-стратегии Flowseal.");
        return result.OrderBy(s => s.Name.Contains("(ALT11)", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ToList();
    }
    public static List<Strategy> Resolve(string executable, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Replace("\r", "").Trim() == ZapretAudit.DefaultStrategies.Trim()) return Load(executable);
        return ZapretAudit.Parse(text);
    }
    public static string ParseTcp(string text, string bin)
    {
        text = text.Replace("^\r\n", " ").Replace("^\n", " ").Replace("^!", "!");
        var blocks = Regex.Split(text, @"\s--new\b");
        var selected = blocks.FirstOrDefault(b => Regex.IsMatch(b, @"--filter-tcp=(?:80,)?443(?:\s|$)") && b.Contains("%LISTS%list-general.txt", StringComparison.OrdinalIgnoreCase));
        if (selected == null) return "";
        var args = new List<string>();
        foreach (Match match in Regex.Matches(selected, @"--dpi-desync[\w-]*(?:=(?:""[^""]*""|[^\s^]+))?"))
        {
            var arg = match.Value;
            var split = arg.IndexOf('=');
            if (split >= 0)
            {
                var value = arg[(split + 1)..].Trim('"');
                if (value.StartsWith("%BIN%", StringComparison.OrdinalIgnoreCase))
                {
                    value = Path.GetFullPath(Path.Combine(bin, value[5..]));
                    if (!value.StartsWith(Path.GetFullPath(bin).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(value)) return "";
                }
                if (value.IndexOfAny(['%', '&', '|', '>', '<', '\r', '\n']) >= 0) return "";
                arg = arg[..(split + 1)] + value;
            }
            args.Add(arg);
        }
        var result = string.Join('\n', args);
        if (args.Count == 0) return "";
        _ = DiscordScope.Arguments(new Preferences { ZapretDiscordOnly = true }, result);
        return result;
    }
    public static string Format(IEnumerable<Strategy> strategies) => string.Join("\n---\n", strategies.Select(s => s.Name + "\n" + s.Arguments));
}
