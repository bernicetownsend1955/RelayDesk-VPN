using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace RelayDesk;

public class ThemedWindow : Window
{
    public ThemedWindow()
    {
        Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/RelayDesk;component/ModernTheme.xaml")});
        if(App.UseOrbitUi)Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/RelayDesk;component/OrbitTheme.xaml")});
        foreach(var pair in new[]{("Page","#101318"),("Surface","#1B1E25"),("Ink","#ECEEF2"),("Muted","#9DA3AF"),("Line","#3D4350"),("Accent","#D2DAE6"),("Selected","#303641"),("Action","#444C59")})
            Resources[pair.Item1]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Item2));
        if(App.UseOrbitUi)foreach(var pair in new[]{("Page","#0C0C0C"),("Surface","#171717"),("Ink","#EEEEEE"),("Muted","#929292"),("Line","#353535"),("Accent","#DDDDDD"),("Selected","#303030"),("Action","#444444")})Resources[pair.Item1]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Item2));
        Resources["Primary"]=Resources[typeof(System.Windows.Controls.Button)];
        SetResourceReference(StyleProperty, typeof(Window));
        SourceInitialized += (_, _) => UpdateCaption();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == BackgroundProperty) UpdateCaption();
    }

    void UpdateCaption()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || Background is not SolidColorBrush brush) return;
        var color = brush.Color;
        int dark = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) < 128 ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
