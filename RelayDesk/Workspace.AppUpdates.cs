using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace RelayDesk;

public partial class Workspace
{
    AppRelease? appRelease;
    string EffectiveAppRepository => string.IsNullOrWhiteSpace(store.Settings.AppUpdateRepository) ? AppUpdates.DefaultRepository : store.Settings.AppUpdateRepository;
    void InitializeAppUpdates()
    {
        AppRepository.Text = EffectiveAppRepository;
        if (string.IsNullOrWhiteSpace(EffectiveAppRepository)) AppUpdateStatus.Text = "Репозиторий не указан. Проверка обновлений RelayDesk пока отключена.";
    }
    async Task<bool> CheckAppUpdates(CancellationToken cancellation)
    {
        appRelease = null; AppReleaseBanner.Visibility = AppNotesPanel.Visibility = Visibility.Collapsed;
        if (string.IsNullOrWhiteSpace(EffectiveAppRepository)) { AppUpdateStatus.Text = "Репозиторий не указан. Проверка обновлений RelayDesk пока отключена."; return true; }
        try
        {
            appRelease = await new AppUpdates().Check(EffectiveAppRepository, AppUpdates.CurrentVersion, cancellation);
            AppUpdateStatus.Text = appRelease.Available ? "Доступна версия " + appRelease.Version : "Установлена актуальная версия " + AppUpdates.CurrentVersion;
            var heading = "RelayDesk " + appRelease.Version;
            AppReleaseTitle.Text = appRelease.Title.Equals(heading, StringComparison.OrdinalIgnoreCase) ? heading : heading + " — " + appRelease.Title;
            AppReleaseSummary.Text = AppUpdates.PlainText(appRelease.Notes, 220);
            AppReleaseNotes.Text = appRelease.Notes.Length > 0 ? appRelease.Notes : "Описание изменений не опубликовано.";
            AppReleaseBanner.Visibility = appRelease.Available ? Visibility.Visible : Visibility.Collapsed;
            AppNotesPanel.Visibility = appRelease.Available ? Visibility.Visible : Visibility.Collapsed;
            return true;
        }
        catch (Exception ex) when (!cancellation.IsCancellationRequested) { AppUpdateStatus.Text = "Не удалось проверить RelayDesk: " + ex.Message; AppNotesPanel.Visibility = Visibility.Collapsed; return false; }
    }
    void SaveAppRepositoryClick(object sender, RoutedEventArgs e) => Safe(async () =>
    {
        if (updatesBusy || installingUpdate) throw new InvalidOperationException("Дождись завершения проверки или установки.");
        var value = AppRepository.Text.Trim();
        store.Settings.AppUpdateRepository = value.Length == 0 ? "" : AppUpdates.Repository(value);
        store.Save(); await CheckUpdates();
    });
    void InstallAppClick(object sender, RoutedEventArgs e) => Safe(async () =>
    {
        if (updatesBusy || installingUpdate || session.Busy || automationBusy || closing) throw new InvalidOperationException("Дождись завершения текущей операции.");
        if (appRelease?.Available != true) throw new InvalidOperationException("Сначала проверь обновления RelayDesk.");
        if (HasChanges()) throw new InvalidOperationException("Сохрани изменения профиля перед обновлением RelayDesk.");
        var release = appRelease;
        if (new AppUpdateConsentWindow(release) { Owner = Window.GetWindow(this) }.ShowDialog() != true) return;
        updatesBusy = installingUpdate = true; RefreshUpdateButtons(); UpdateStatus();
        Tabs.SelectedIndex = 6;
        foreach (System.Windows.Controls.TabItem tab in Tabs.Items) tab.IsEnabled = tab.Header?.ToString() == "Обновления";
        Navigation.IsEnabled = ProfileList.IsEnabled = false;
        updateCancel.Dispose(); updateCancel = new();
        try
        {
            var installer = await new AppUpdates().Download(release, store.Folder, text => AppUpdateStatus.Text = text, updateCancel.Token);
            if (closing) return;
            var helper = Path.Combine(AppContext.BaseDirectory, "Apply-Update.ps1");
            if (!File.Exists(helper)) throw new FileNotFoundException("Не найден помощник установки обновления.");
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", helper, "-ParentId", Environment.ProcessId.ToString(), "-Installer", installer, "-Sha256", release.File.Hash, "-DataFolder", store.Folder }) start.ArgumentList.Add(argument);
            using var registration = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\RelayDesk-Desktop-A7B02E29-4D16-47F0-B8F3-3B829E1F024D_is1");
            var installed = registration?.GetValue("InstallLocation") as string;
            if (!string.IsNullOrWhiteSpace(installed) && string.Equals(Path.GetFullPath(installed).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            { start.ArgumentList.Add("-InstallDirectory"); start.ArgumentList.Add(installed); }
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Не удалось запустить помощник установки.");
            AppUpdateStatus.Text = "Установщик проверен. Закрываю RelayDesk для обновления.";
            // ClosingWindow waits for updatesBusy before stopping the owned cores.
            updatesBusy = installingUpdate = false;
            Window.GetWindow(this).Close();
        }
        finally
        {
            updatesBusy = installingUpdate = false;
            if (!closing) { foreach (System.Windows.Controls.TabItem tab in Tabs.Items) tab.IsEnabled = true; Navigation.IsEnabled = ProfileList.IsEnabled = true; }
            RefreshUpdateButtons(); UpdateStatus();
        }
    });
}
