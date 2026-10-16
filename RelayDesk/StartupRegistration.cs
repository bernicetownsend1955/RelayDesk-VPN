using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;

namespace RelayDesk;

public static class StartupRegistration
{
    public static string UserSid { get { using var identity = WindowsIdentity.GetCurrent(); return identity.User?.Value ?? throw new InvalidOperationException("Не определена учётная запись Windows."); } }
    public static string TaskName => "RelayDesk.Startup." + UserSid;
    public static string TaskXml(string executable, string userSid)
    {
        executable = Path.GetFullPath(executable);
        if (!executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !System.Text.RegularExpressions.Regex.IsMatch(userSid, @"^S-1-\d+(?:-\d+)+$")) throw new FormatException("Некорректные параметры автозапуска.");
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        XElement E(string name, object value) => new(ns + name, value);
        return new XElement(ns + "Task", new XAttribute("version", "1.2"),
            E("RegistrationInfo", E("Description", "Запуск RelayDesk при входе пользователя в Windows")),
            E("Triggers", E("LogonTrigger", new object[] { E("Enabled", true), E("UserId", userSid), E("Delay", "PT15S") })),
            E("Principals", new XElement(ns + "Principal", new XAttribute("id", "Author"), E("UserId", userSid), E("LogonType", "InteractiveToken"), E("RunLevel", "HighestAvailable"))),
            E("Settings", new object[] { E("MultipleInstancesPolicy", "IgnoreNew"), E("DisallowStartIfOnBatteries", false), E("StopIfGoingOnBatteries", false), E("StartWhenAvailable", true), E("Enabled", true), E("ExecutionTimeLimit", "PT0S") }),
            new XElement(ns + "Actions", new XAttribute("Context", "Author"), E("Exec", new object[] { E("Command", executable), E("Arguments", "--windows-startup"), E("WorkingDirectory", Path.GetDirectoryName(executable)!) }))).ToString();
    }
    public static void Apply(bool enabled, string executable, string? taskName = null)
    {
        var name = taskName ?? TaskName;
        if (name != TaskName && !name.StartsWith("RelayDesk.Startup.Test.", StringComparison.Ordinal)) throw new InvalidOperationException("Недопустимое имя задачи автозапуска.");
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        try
        {
            service.Connect(); dynamic folder = service.GetFolder("\\");
            try
            {
                if (enabled)
                {
                    if (!File.Exists(executable)) throw new FileNotFoundException("Приложение для автозапуска не найдено.");
                    var registered = folder.RegisterTask(name, TaskXml(executable, UserSid), 6, UserSid, null, 3, null);
                    Marshal.FinalReleaseComObject(registered);
                }
                else
                {
                    try { folder.DeleteTask(name, 0); }
                    catch (Exception ex) when (ex is COMException or FileNotFoundException && (uint)ex.HResult == 0x80070002) { }
                }
            }
            finally { Marshal.FinalReleaseComObject(folder); }
        }
        finally { Marshal.FinalReleaseComObject(service); }
    }
}
