using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace RelayDesk;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = "RelayDesk " + AppUpdates.CurrentVersion;
        Width = Math.Min(1260, SystemParameters.WorkArea.Width - 32);
        Height = Math.Min(940, SystemParameters.WorkArea.Height - 32);
        MinWidth = Math.Min(1080, Width);
        MinHeight = Math.Min(760, Height);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        try { Content = new Workspace(); }
        catch (Exception ex)
        {
            ProgramJournal.Write(ex.ToString(), "startup");
            MessageBox.Show("Не удалось открыть RelayDesk: " + ex.Message + "\nФайлы профилей не перезаписаны.");
            Application.Current.Shutdown(1);
        }
    }
}
