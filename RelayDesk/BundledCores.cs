using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace RelayDesk;

public static class BundledCores
{
    public sealed record Bundle(string Id, string Version, string Executable, List<BundleFile> Files);
    public sealed record BundleFile(string Path, string Sha256);
    public static bool Bind(Store store, string applicationFolder)
    {
        var root = Path.Combine(applicationFolder,"bundled-cores");
        var manifest = Path.Combine(root,"manifest.json"); if (!File.Exists(manifest)) return false;
        var bundles = JsonSerializer.Deserialize<List<Bundle>>(File.ReadAllText(manifest)) ?? throw new InvalidDataException("Пустой список встроенных ядер.");
        bool changed = false;
        foreach (var bundle in bundles)
        {
            if (bundle.Id is not ("sing-box" or "Xray" or "mihomo" or "zapret")) throw new InvalidDataException("Неизвестное встроенное ядро.");
            var existing = bundle.Id == "zapret" ? store.Settings.ZapretExe : store.Settings.Engines.GetValueOrDefault(bundle.Id, "");
            if (File.Exists(existing)) continue;
            var executable = ComponentUpdates.Destination(root,bundle.Executable);
            if (!bundle.Files.Any(f => Path.GetFullPath(ComponentUpdates.Destination(root,f.Path)) == Path.GetFullPath(executable))) throw new InvalidDataException("EXE отсутствует в списке встроенного ядра.");
            foreach (var file in bundle.Files)
            {
                var path = ComponentUpdates.Destination(root,file.Path);
                if (!File.Exists(path) || !Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Повреждены встроенные файлы " + bundle.Id + ". Переустанови приложение.");
            }
            if (bundle.Id == "zapret")
            {
                if (string.IsNullOrWhiteSpace(store.Settings.ZapretArgs))
                {
                    var strategies = FlowsealStrategies.Load(executable);
                    store.Settings.ZapretArgs = strategies[0].Arguments;
                    if (string.IsNullOrWhiteSpace(store.Settings.StrategyList)) store.Settings.StrategyList = FlowsealStrategies.Format(strategies);
                }
                store.Settings.ZapretExe = executable;
            }
            else store.Settings.Engines[bundle.Id] = executable;
            store.Settings.ComponentInstalls[bundle.Id] = new(bundle.Id == "zapret" ? "flowseal:"+bundle.Version : bundle.Version,executable,existing,DateTimeOffset.UtcNow);
            changed = true;
        }
        if (changed) store.Save();
        return changed;
    }
}
