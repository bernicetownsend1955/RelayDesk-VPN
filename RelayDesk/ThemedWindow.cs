using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace RelayDesk;

public class ThemedWindow : Window
{
    public ThemedWindow()
    {
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
