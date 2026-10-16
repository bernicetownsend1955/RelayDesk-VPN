using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace RelayDesk;

public partial class Workspace
{
    readonly SupportClient support = new();
    readonly DispatcherTimer supportTimer = new() { Interval = TimeSpan.FromMinutes(15) };
    readonly CancellationTokenSource supportCancel = new();
    bool supportBusy;
    string lastReport = "", supportJob = "";
    void InitializeSupport()
    {
        SupportHost.Text = store.Settings.SupportHost;
        AutoSupport.IsChecked = store.Settings.SendSupportLogs;
        if (File.Exists(TokenPath)) SupportStatus.Text = "Доступ к ВДС настроен. Отправка включается сохранённым переключателем; провайдер нейросети пока не выбран.";
        supportJob = store.Settings.SupportJob;
        supportTimer.Tick += async (_, _) => await SupportOperation(false, false);
        supportTimer.Start();
        if (store.Settings.SendSupportLogs && Environment.GetEnvironmentVariable("RELAYDESK_DATA_DIR") == null) _ = SupportOperation(false, false);
    }
    string TokenPath => Path.Combine(store.Folder, "support-token.dpapi");
    void SupportSave(object sender, RoutedEventArgs e) => Safe(() =>
    {
        store.Settings.SupportHost = SupportHost.Text.Trim();
        store.Settings.SendSupportLogs = AutoSupport.IsChecked == true;
        if (!string.IsNullOrWhiteSpace(SupportToken.Password))
        {
            File.WriteAllBytes(TokenPath, ProtectedData.Protect(Encoding.UTF8.GetBytes(SupportToken.Password.Trim()), null, DataProtectionScope.CurrentUser));
            SupportToken.Clear();
        }
        store.Save(); SupportStatus.Text = "Настройки сохранены. Отправка каждые 15 минут; запуск нейросети — по кнопке.";
        return Task.CompletedTask;
    });
    async void SendSupport(object sender, RoutedEventArgs e) => await SupportOperation(true, false);
    async void RequestRepair(object sender, RoutedEventArgs e) => await SupportOperation(true, true);
    async Task SupportOperation(bool manual, bool repair)
    {
        if (!store.Settings.ExperimentalDiagnostics || supportBusy || closing || (!manual && !store.Settings.SendSupportLogs)) return;
        if (!File.Exists(TokenPath)) { if (manual) SupportStatus.Text = "Сначала сохрани токен доступа к ВДС."; return; }
        if (repair && string.IsNullOrWhiteSpace(SupportIssue.Text)) { SupportStatus.Text = "Опиши ошибку: что нажал и что произошло."; return; }
        supportBusy = true;
        try
        {
            support.Configure(store.Settings.SupportHost, Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(TokenPath), null, DataProtectionScope.CurrentUser)));
            await Task.Delay(1200, supportCancel.Token);
            lastReport = await support.Upload(supportCancel.Token, repair);
            SupportStatus.Text = "Журнал отправлен на ВДС: " + DateTime.Now.ToString("HH:mm") + ". Нейросеть не запускалась.";
            if (repair)
            {
                var result = await support.Repair(lastReport, SupportIssue.Text, Guid.NewGuid().ToString("N"), supportCancel.Token);
                supportJob = result["id"]!.ToString(); store.Settings.SupportJob = supportJob; store.Save();
                SupportStatus.Text = result["message"]?.ToString() ?? result["state"]?.ToString();
            }
            else if (supportJob.Length != 0) await RefreshSupportJob();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { SupportStatus.Text = "Отправка не выполнена: " + ProgramJournal.Clean(ex.Message); ProgramJournal.Write(ex.Message, "support"); }
        finally { supportBusy = false; }
    }
    async Task RefreshSupportJob()
    {
        var job = await support.Status(supportJob, supportCancel.Token);
        SupportStatus.Text = job["message"]?.ToString() ?? job["state"]?.ToString();
        DownloadRepairButton.IsEnabled = job["state"]?.ToString() == "ready";
        if (DownloadRepairButton.IsEnabled) ActionMessage.Text = "На ВДС подготовлена сборка. Скачать: Настройки → Экспериментальные функции → Диагностика и исправления.";
    }
    async void CheckRepair(object sender, RoutedEventArgs e)
    {
        if (supportJob.Length == 0) { SupportStatus.Text = "Запросов на исправление ещё нет."; return; }
        await SupportOperation(true, false);
    }
    async void DownloadRepair(object sender, RoutedEventArgs e)
    {
        if (supportBusy || supportJob.Length == 0) return;
        if (MessageBox.Show(Window.GetWindow(this), "Скачать подготовленную сборку с ВДС? Текущая версия останется на месте. Архив не запускается автоматически.", "Скачать сборку", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        supportBusy = true;
        try { var file = await support.Download(supportJob, Path.Combine(store.Folder, "builds"), supportCancel.Token); SupportStatus.Text = "Архив проверен и сохранён: " + file; System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + file + "\"") { UseShellExecute = true }); }
        catch (Exception ex) { SupportStatus.Text = ProgramJournal.Clean(ex.Message); }
        finally { supportBusy = false; }
    }
}
