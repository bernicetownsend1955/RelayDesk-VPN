using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace RelayDesk;

public static class CoreRecovery
{
    sealed record Candidate(int ProcessId, int ParentProcessId, string? ExecutablePath, string? CommandLine, long StartedTicks);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CommandLineToArgvW(string line, out int count);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("iphlpapi.dll")] static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);
    public static string PortOwner(int port)
    {
        int size = 0; _ = GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 3, 0);
        if (size < 4) return "";
        var table = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(table, ref size, false, 2, 3, 0) != 0) return "";
            var count = Marshal.ReadInt32(table);
            for (int i = 0; i < count && 4 + (i + 1) * 24 <= size; i++)
            {
                var rawPort = Marshal.ReadInt32(table, 4 + i * 24 + 8);
                if ((((rawPort & 255) << 8) | ((rawPort >> 8) & 255)) != port) continue;
                var pid = Marshal.ReadInt32(table, 4 + i * 24 + 20);
                try { using var process = Process.GetProcessById(pid); return $"PID {pid} ({process.ProcessName})"; }
                catch (ArgumentException) { return "PID " + pid; }
            }
            return "";
        }
        finally { Marshal.FreeHGlobal(table); }
    }
    public static string[] Arguments(string line)
    {
        var pointer = CommandLineToArgvW(line, out var count);
        if (pointer == IntPtr.Zero) return [];
        try { return Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, i * IntPtr.Size)) ?? "").ToArray(); }
        finally { LocalFree(pointer); }
    }
    public static bool OwnConfig(string folder, string command)
    {
        var args = Arguments(command);
        var index = Array.FindIndex(args, a => a is "-c" or "--config");
        if (index < 0 || index + 1 == args.Length) return false;
        try
        {
            var path = Path.GetFullPath(args[index + 1]);
            return string.Equals(Path.GetDirectoryName(path), Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) && System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path), @"^[0-9a-f]{32}\.(?:runtime|probe|check)\.json$");
        }
        catch (ArgumentException) { return false; }
    }
    public static void Recover(Store store, Action<string> log)
    {
        if (!EngineSession.IsAdmin() || Environment.GetEnvironmentVariable("RELAYDESK_DATA_DIR") != null) return;
        const string script = "$ErrorActionPreference='Stop'; $p=@(Get-CimInstance Win32_Process -Filter \"Name='sing-box.exe' OR Name='winws.exe'\" | Select-Object ProcessId,ParentProcessId,ExecutablePath,CommandLine,@{Name='StartedTicks';Expression={$_.CreationDate.ToUniversalTime().Ticks}}); ConvertTo-Json -InputObject $p -Compress";
        var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes("[Console]::OutputEncoding=[Text.Encoding]::UTF8;" + script)) }) info.ArgumentList.Add(arg);
        using var query = Process.Start(info)!;
        var output = query.StandardOutput.ReadToEndAsync(); var errors = query.StandardError.ReadToEndAsync();
        if (!query.WaitForExit(8000)) { query.Kill(true); log("Проверка прежних ядер превысила время ожидания. Чужие процессы не изменены."); return; }
        if (query.ExitCode != 0) { log("Не удалось проверить прежние ядра. Процессы не изменены."); return; }
        var candidates = JsonSerializer.Deserialize<List<Candidate>>(output.GetAwaiter().GetResult(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        var recoveredParents = new HashSet<int>();
        bool Orphan(Candidate candidate)
        {
            try
            {
                using var child = Process.GetProcessById(candidate.ProcessId);
                try { using var parent = Process.GetProcessById(candidate.ParentProcessId); return parent.StartTime > child.StartTime; }
                catch (ArgumentException) { return true; }
            }
            catch (ArgumentException) { return false; }
        }
        bool Stop(Candidate candidate)
        {
            using var process = Process.GetProcessById(candidate.ProcessId);
            if (!string.Equals(process.MainModule?.FileName, candidate.ExecutablePath, StringComparison.OrdinalIgnoreCase) || Math.Abs(process.StartTime.ToUniversalTime().Ticks - candidate.StartedTicks) > TimeSpan.TicksPerMillisecond) return false;
            process.Kill(true); process.WaitForExit(5000); log("Завершён оставшийся от прежнего RelayDesk процесс PID " + candidate.ProcessId + "."); return true;
        }
        var sing = store.Settings.Engines.GetValueOrDefault("sing-box", "");
        bool ComponentPath(Candidate candidate, string id, string configured) => !string.IsNullOrEmpty(candidate.ExecutablePath) && (string.Equals(candidate.ExecutablePath, configured, StringComparison.OrdinalIgnoreCase) || Path.GetFullPath(candidate.ExecutablePath).StartsWith(Path.Combine(store.Folder, "components", id) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        foreach (var candidate in candidates.Where(c => Path.GetFileName(c.ExecutablePath)?.Equals("sing-box.exe", StringComparison.OrdinalIgnoreCase) == true && ComponentPath(c, "sing-box", sing) && c.CommandLine != null && OwnConfig(store.Folder, c.CommandLine) && Orphan(c)))
        {
            try { if (Stop(candidate)) recoveredParents.Add(candidate.ParentProcessId); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException) { log("Прежнее ядро не завершено: " + ex.Message); }
        }
        foreach (var candidate in candidates.Where(c => recoveredParents.Contains(c.ParentProcessId) && Path.GetFileName(c.ExecutablePath)?.Equals("winws.exe", StringComparison.OrdinalIgnoreCase) == true && ComponentPath(c, "zapret", store.Settings.ZapretExe) && Orphan(c)))
            try { Stop(candidate); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException) { log("Прежний zapret не завершён: " + ex.Message); }
    }
}
