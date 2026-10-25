using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using RelayDesk;

static class Program
{
    static int count;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); count++; }
    static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        { if (child is T match) yield return match; foreach (var nested in Find<T>(child)) yield return nested; }
    }
    [STAThread] static int Main()
    {
        var folder = Path.Combine(Path.GetTempPath(), "RelayDesk-ui-routes-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("RELAYDESK_DATA_DIR", folder);
        var store = new Store(folder);
        store.Profiles.Add(new Profile { Name = "UI route fixture", Config = "{\"inbounds\":[{\"type\":\"tun\",\"auto_route\":true,\"address\":[\"172.19.0.1/30\"]},{\"type\":\"mixed\",\"listen_port\":2080}],\"outbounds\":[{\"type\":\"direct\",\"tag\":\"direct\"},{\"type\":\"socks\",\"tag\":\"proxy\",\"server\":\"127.0.0.1\",\"server_port\":1080}],\"route\":{\"rules\":[{\"action\":\"hijack-dns\",\"protocol\":\"dns\"}],\"final\":\"proxy\"}}" });
        store.Settings.CheckComponentUpdates = false; store.Settings.ZapretArgs = "--dpi-desync=fake"; store.Save();
        var app = new Application(); app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/RelayDesk;component/Theme.xaml") });
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        var workspace = new Workspace(); var window = new Window { Content = workspace, Width = 1260, Height = 940 };
        window.Show(); workspace.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        try
        {
            var repository=(TextBox)workspace.FindName("AppRepository");
            Check(repository.IsReadOnly && repository.Text==AppUpdates.DefaultRepository,"Update repository is fixed and read-only");
            Check(new Preferences().CheckComponentUpdates,"Automatic update checks are enabled by default");
            var routeWindow = new ConnectionRouteWindow(store.Profiles[0], "domain", "example.org", ["proxy"], false);
            var previousPage = app.Resources["Page"];
            var previousInk = app.Resources["Ink"];
            foreach (var dark in new[] { true, false, true })
            {
                var background = new System.Windows.Media.SolidColorBrush(dark ? System.Windows.Media.Color.FromRgb(20,23,22) : System.Windows.Media.Color.FromRgb(245,246,245));
                var foreground = new System.Windows.Media.SolidColorBrush(dark ? System.Windows.Media.Colors.White : System.Windows.Media.Colors.Black);
                app.Resources["Page"] = background; app.Resources["Ink"] = foreground;
                routeWindow.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Check(ReferenceEquals(routeWindow.Background, routeWindow.Resources["Page"]) && ReferenceEquals(routeWindow.Foreground, routeWindow.Resources["Ink"])
                    && routeWindow.FontFamily.Source == "Segoe UI" && routeWindow.FontSize == 14,
                    "Route dialog retains graphite palette and typography when legacy theme changes to " + (dark ? "dark" : "light"));
            }
            routeWindow.Close();
            app.Resources["Page"] = previousPage; app.Resources["Ink"] = previousInk;
            var nav = (ListBox)workspace.FindName("Navigation");
            var item = (ListBoxItem)nav.Items[1];
            var mouse = new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Source = item };
            nav.RaiseEvent(mouse);
            Check(mouse.Handled && nav.SelectedIndex == 1 && Mouse.Captured != nav, "Navigation click selects one item and prevents ListBox drag capture");
            var tabs = (TabControl)workspace.FindName("Tabs");
            Check(((TextBox)workspace.FindName("ProfileName")).FontWeight == FontWeights.Normal,
                "Selected tab does not make form contents semibold");
            ((CheckBox)workspace.FindName("PauseConnections")).IsChecked = true;
            tabs.SelectedItem = workspace.FindName("ConnectionTab");
            var grid = (DataGrid)workspace.FindName("ConnectionsTable");
            var source = grid.ItemsSource;
            var rowsField = typeof(Workspace).GetField("connectionRows",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var refresh = typeof(Workspace).GetMethod("FilterConnections",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var rows = new List<ConnectionRow> {
                new("1","Z.exe","z.example:443","TCP","Напрямую","direct","","","") { ProcessPath=@"C:\Apps\Z.exe",Host="z.example",TotalBytes=9 },
                new("2","A.exe","a.example:443","TCP","Напрямую","direct","","","") { ProcessPath=@"C:\Apps\A.exe",Host="a.example",TotalBytes=100 }
            };
            rowsField.SetValue(workspace,rows); refresh.Invoke(workspace,null);
            var view = CollectionViewSource.GetDefaultView(source);
            view.SortDescriptions.Add(new SortDescription("Application",ListSortDirection.Ascending));
            grid.Columns[0].SortDirection = ListSortDirection.Ascending;
            grid.SelectedItem = rows[0];
            for (int i=0;i<5;i++) refresh.Invoke(workspace,null);
            Check(ReferenceEquals(source,grid.ItemsSource) && view.SortDescriptions.Count == 1 && view.Cast<ConnectionRow>().First().Application == "A.exe", "Connection sorting survives repeated refresh with stable view");
            Check(grid.Columns[0].SortDirection == ListSortDirection.Ascending,"Sort direction indicator survives refresh");
            Check((grid.SelectedItem as ConnectionRow)?.Id == "1", "Selected connection survives refresh");
            view.SortDescriptions.Clear(); view.SortDescriptions.Add(new SortDescription("TotalBytes",ListSortDirection.Descending)); refresh.Invoke(workspace,null);
            Check(view.Cast<ConnectionRow>().First().TotalBytes == 100, "Traffic sorts numerically instead of lexically");
            ((TextBox)workspace.FindName("ConnectionFilter")).Text = "Z.exe";
            Check(view.Cast<ConnectionRow>().Count() == 1 && view.SortDescriptions.Count == 1, "Filtering retains sort description");
            ((TextBox)workspace.FindName("ConnectionFilter")).Text = "";
            foreach (bool application in new[] { false, true })
            {
                workspace.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var dialog = app.Windows.OfType<ConnectionRouteWindow>().Single();
                    Find<ComboBox>(dialog).Single().SelectedIndex = 1;
                    Find<Button>(dialog).Single(b => Equals(b.Content,"Сохранить правило")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));
                var method = typeof(Workspace).GetMethod(application ? "ConnectionAppClick" : "ConnectionHostClick",BindingFlags.Instance|BindingFlags.NonPublic)!;
                method.Invoke(workspace,[new Button { DataContext=rows[0] },new RoutedEventArgs()]);
                workspace.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            }
            var saved = new Store(folder).Profiles.Single();
            Check(saved.QuickRoutes.Any(r => r.Kind == "domain" && r.Value == "z.example" && r.Outbound == "proxy") && saved.QuickRoutes.Any(r => r.Kind == "process_path" && r.Value == @"C:\Apps\Z.exe"), "Connection clicks save domain and process rules to selected profile");
            Check(saved.Config == store.Profiles[0].Config, "Quick rule editing preserves original JSON");
            Check(((TextBlock)workspace.FindName("ConnectionActionStatus")).Text.Contains("сохранено"), "Rule feedback remains visible outside the refreshed status");
            foreach (bool remove in new[] { false, true })
            {
                workspace.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var dialog = app.Windows.OfType<ConnectionRouteWindow>().Single();
                    Find<ComboBox>(dialog).Single().SelectedIndex = 0;
                    Find<Button>(dialog).Single(b => Equals(b.Content,remove ? "Удалить правило" : "Сохранить правило")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));
                typeof(Workspace).GetMethod("ConnectionHostClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(workspace,[new Button { DataContext=rows[0] },new RoutedEventArgs()]);
                workspace.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var rules = new Store(folder).Profiles.Single().QuickRoutes;
                Check(remove ? !rules.Any(r => r.Kind == "domain") : rules.Any(r => r.Kind == "domain" && r.Direct),remove ? "Connection dialog removes a saved quick rule" : "Connection dialog switches a proxy rule to direct");
            }
            workspace.Dispatcher.BeginInvoke(new Action(() =>
            {
                var dialog = app.Windows.OfType<ConnectionRouteWindow>().Single();
                var choice = Find<ComboBox>(dialog).Single(); choice.SelectedIndex = choice.Items.Count - 1;
                Check(Equals(choice.SelectedItem,"Через zapret"), "Connections expose a separate zapret destination");
                Find<Button>(dialog).Single(b => Equals(b.Content,"Сохранить правило")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            typeof(Workspace).GetMethod("ConnectionHostClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(workspace,[new Button { DataContext=rows[0] },new RoutedEventArgs()]);
            Check(new Store(folder).Profiles.Single().QuickRoutes.Any(r => r.Zapret && r.Value == "z.example"), "Zapret choice persists independently from direct and proxy routes");
            workspace.Dispatcher.BeginInvoke(new Action(() =>
            {
                var dialog = app.Windows.OfType<SubscriptionGroupsWindow>().Single();
                Find<TextBox>(dialog).Single().Text = "Work";
                Find<Button>(dialog).Single(b => Equals(b.Content,"Создать")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Find<CheckBox>(dialog).Single().IsChecked = true;
                Find<Button>(dialog).Single(b => Equals(b.Content,"Сохранить")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            typeof(Workspace).GetMethod("EditSubscriptionGroups",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(workspace,[new Button(),new RoutedEventArgs()]);
            Check(new Store(folder).Profiles.Single().SubscriptionGroup == "Work" && new Store(folder).Settings.SubscriptionGroups.Contains("Work"), "Group editor saves group and membership");
            var groupFilter = (ComboBox)workspace.FindName("SubscriptionGroupFilter"); groupFilter.SelectedItem = "Work";
            Check(((ListBox)workspace.FindName("ProfileList")).Items.Count == 1, "Group filter shows group members");
            groupFilter.SelectedIndex = 1;
            Check(((ListBox)workspace.FindName("ProfileList")).Items.Count == 0, "Ungrouped filter excludes assigned profiles");
            groupFilter.SelectedIndex = 0;
            Check(((ListBox)workspace.FindName("ProfileList")).Items.Count == 1, "All profiles restores the full list");
            foreach (var remove in new[] { false, true })
            {
                workspace.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var dialog = app.Windows.OfType<SubscriptionGroupsWindow>().Single();
                    Find<ComboBox>(dialog).Single().SelectedItem = remove ? "Renamed" : "Work";
                    if (!remove) Find<TextBox>(dialog).Single().Text = "Renamed";
                    Find<Button>(dialog).Single(b => Equals(b.Content,remove ? "Удалить группу" : "Переименовать")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Find<Button>(dialog).Single(b => Equals(b.Content,"Сохранить")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));
                typeof(Workspace).GetMethod("EditSubscriptionGroups",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(workspace,[new Button(),new RoutedEventArgs()]);
                var storedGroups = new Store(folder);
                Check(remove ? storedGroups.Profiles.Single().SubscriptionGroup == "" && storedGroups.Settings.SubscriptionGroups.Count == 0 : storedGroups.Profiles.Single().SubscriptionGroup == "Renamed",
                    remove ? "Deleting group retains profiles and removes membership" : "Renaming group updates stored membership");
            }
            var subscriptionDialog = new SubscriptionWindow([],"happ://add/https://subscriptions.example.org/private","Fixture group");
            subscriptionDialog.Dispatcher.BeginInvoke(new Action(() => Find<Button>(subscriptionDialog).Single(b => Equals(b.Content,"Загрузить предпросмотр")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
            subscriptionDialog.ShowDialog();
            Check(subscriptionDialog.Source?.Url == "https://subscriptions.example.org/private" && subscriptionDialog.Source.Group == "Fixture group" && subscriptionDialog.Source.SendDeviceId,"Subscription dialog accepts Happ URL and offers persistent HWID");
            Check(new Store(folder).Settings.Subscriptions.Count == 0,"Subscription dialog alone does not save or import before download and preview confirmation");
            tabs.SelectedItem = workspace.FindName("ProfilesTab");
            workspace.Dispatcher.Invoke(() => { },DispatcherPriority.ApplicationIdle);
            Check(((ListBox)workspace.FindName("ProfileList")).IsVisible && ((ListBox)workspace.FindName("ProfileList")).MaxHeight == double.PositiveInfinity,"Profile library provides full-height list outside sidebar");
            ((TextBox)workspace.FindName("ProfileSearch")).Text="not-present";
            Check(((ListBox)workspace.FindName("ProfileList")).Items.Count==0,"Profile search filters library without changing selection");
            ((TextBox)workspace.FindName("ProfileSearch")).Text="";
            Check(((ListBox)workspace.FindName("ProfileList")).Items.Count==1,"Clearing profile search restores library");
            tabs.SelectedItem=workspace.FindName("ProfilesTab");
            var latencySection=Find<Expander>(workspace).Single(e=>Equals(e.Header,"Задержка серверов (TCP)")); latencySection.IsExpanded=true;
            ((Button)workspace.FindName("StartServerPing")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for(int attempt=0;attempt<1000 && !((Button)workspace.FindName("StartServerPing")).IsEnabled;attempt++) { workspace.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle); Thread.Sleep(10); }
            Check(((DataGrid)workspace.FindName("ServerPingsTable")).Items.Count==1 && ((Button)workspace.FindName("StartServerPing")).IsEnabled,"Profiles server latency button produces profile result and restores controls");
            var persisted = new Store(folder);
            var lastServer = new Profile { Name = "Last connected fixture", Core = "Xray", Config = "{\"outbounds\":[{\"protocol\":\"freedom\",\"tag\":\"direct\"}]}" };
            persisted.Profiles.Add(lastServer); persisted.Settings.LastConnectedProfileId=lastServer.Id; persisted.Save();
            var reopened = new Workspace();
            Check(((ListBox)reopened.FindName("ProfileList")).SelectedItem is Profile restored && restored.Id==lastServer.Id,"Reopening restores last connected server instead of first profile");
            foreach (bool zapretRoute in new[] { false, true })
            {
                reopened.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var dialog=app.Windows.OfType<ConnectionRouteWindow>().Single();
                    var choices=Find<ComboBox>(dialog).Single(); choices.SelectedIndex=zapretRoute ? choices.Items.Count-1 : 0;
                    Find<Button>(dialog).Single(b=>Equals(b.Content,"Сохранить правило")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));
                typeof(Workspace).GetMethod("ConnectionHostClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(reopened,[new Button { DataContext=new ConnectionRow("xray-fixture","Не определено ядром","example.org:443","TCP","Напрямую","direct","","","") { Host="example.org" } },new RoutedEventArgs()]);
                reopened.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
                var xraySaved=new Store(folder).Profiles.Single(p=>p.Id==lastServer.Id);
                Check(xraySaved.QuickRoutes.Any(r=>r.Value=="example.org" && (zapretRoute ? r.Zapret : r.Direct)) && xraySaved.Config==lastServer.Config,"Xray connection address click saves "+(zapretRoute ? "zapret" : "direct")+" route without changing source JSON");
            }
            persisted.Settings.LastConnectedProfileId="deleted-profile"; persisted.Save();
            var fallback = new Workspace();
            Check(((ListBox)fallback.FindName("ProfileList")).SelectedItem is Profile first && first.Id==persisted.Profiles[0].Id,"Deleted last server falls back to available profile");
            var voiceTest=(CheckBox)fallback.FindName("VoiceDiscoveryEnabled");
            Check(voiceTest.IsChecked!=true && !((TextBox)fallback.FindName("VoiceEndpoint")).IsEnabled && !((TextBox)fallback.FindName("VoiceSsrc")).IsEnabled,"Legacy UDP test defaults off and session fields are disabled for WebRTC");
            voiceTest.IsChecked=true;
            Check(((TextBox)fallback.FindName("VoiceEndpoint")).IsEnabled && ((TextBox)fallback.FindName("VoiceSsrc")).IsEnabled,"Explicit legacy UDP opt-in enables endpoint and SSRC fields");
            var voiceProcessing=(CheckBox)fallback.FindName("VoiceUdpDesync");
            Check(voiceProcessing.IsChecked==true,"Voice processing control preserves enabled behavior on upgrade");
            voiceProcessing.IsChecked=false;
            typeof(Workspace).GetMethod("SaveAutomation",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(fallback,[true]);
            Check(!new Store(folder).Settings.VoiceUdpDesync,"Voice UDP control saves independently from legacy discovery");
            Console.WriteLine($"COMPLETE: {count} UI checks passed. {folder}"); return 0;
        }
        catch (Exception ex) { Console.WriteLine(ex); return 1; }
        finally { window.Close(); workspace.Dispatcher.Invoke(() => { },DispatcherPriority.ApplicationIdle); app.Shutdown(); }
    }
}
