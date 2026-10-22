using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace RelayDesk;

public partial class Workspace
{
    void NavigationMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(Navigation, source) is ListBoxItem item && item.IsEnabled)
        { Navigation.SelectedItem = item; item.Focus(); e.Handled = true; }
    }
    void AnimateConnectionState(Button button, bool connected)
    {
        var previous = button.Tag;
        button.Tag = connected;
        if (previous is bool old && old != connected && SystemParameters.ClientAreaAnimation)
            button.BeginAnimation(OpacityProperty, new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(180)));
    }
    void DetailsExpanded(object sender, RoutedEventArgs e)
    {
        if (!SystemParameters.ClientAreaAnimation || e.OriginalSource is not Expander expander) return;
        if (expander.Template.FindName("Details", expander) is FrameworkElement content)
            content.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
    }
    void MainTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, Tabs) || ShowLog == null) return;
        ShowLog.IsChecked = Tabs.SelectedIndex == 0;
        UpdateLogLayout();
        if (Tabs.SelectedItem is TabItem tab && PageTitle != null) PageTitle.Text = tab.Header?.ToString() ?? "RelayDesk";
        if (SystemParameters.ClientAreaAnimation)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Tabs.Template.FindName("PART_SelectedContentHost", Tabs) is not FrameworkElement host) return;
                var offset = new TranslateTransform(); host.RenderTransform = offset;
                var duration = new Duration(TimeSpan.FromMilliseconds(160));
                host.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, duration));
                offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            }));
    }
    void ShowLogClick(object sender, RoutedEventArgs e) => UpdateLogLayout();
    void UpdateLogLayout()
    {
        bool visible = ShowLog.IsChecked == true;
        ContentGrid.RowDefinitions[4].Height = new GridLength(visible ? 140 : 40);
        ContentGrid.RowDefinitions[3].Height = new GridLength(visible ? 8 : 0);
        Log.Visibility = LogSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }
}
