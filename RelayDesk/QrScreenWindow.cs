using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms=System.Windows.Forms;

namespace RelayDesk;
public sealed class QrScreenWindow:Window
{
    readonly BitmapSource screenshot;readonly Canvas canvas=new();readonly System.Windows.Shapes.Rectangle box=new(){Stroke=Brushes.White,StrokeThickness=2,Fill=new SolidColorBrush(Color.FromArgb(30,255,255,255))};Point? start;
    public BitmapSource? SelectedImage {get;private set;}
    public QrScreenWindow()
    {
        var bounds=Forms.SystemInformation.VirtualScreen;
        if((long)bounds.Width*bounds.Height>40_000_000)throw new InvalidOperationException("Слишком большая область экрана. Сохрани QR-код изображением и открой файл.");
        using var bitmap=new System.Drawing.Bitmap(bounds.Width,bounds.Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb);using(var graphics=System.Drawing.Graphics.FromImage(bitmap))graphics.CopyFromScreen(bounds.Left,bounds.Top,0,0,bounds.Size);
        var handle=bitmap.GetHbitmap();try{screenshot=System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(handle,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());screenshot.Freeze();}finally{DeleteObject(handle);}
        Title="Выдели QR-код · Esc — отмена";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;Topmost=true;ShowInTaskbar=false;Cursor=Cursors.Cross;Background=Brushes.Black;
        Left=SystemParameters.VirtualScreenLeft;Top=SystemParameters.VirtualScreenTop;Width=SystemParameters.VirtualScreenWidth;Height=SystemParameters.VirtualScreenHeight;
        var grid=new Grid();grid.Children.Add(new Image{Source=screenshot,Stretch=Stretch.Fill});grid.Children.Add(canvas);canvas.Children.Add(box);box.Visibility=Visibility.Collapsed;
        grid.Children.Add(new TextBlock{Text="Выдели один QR-код с белыми полями. Esc — отмена.",Foreground=Brushes.White,Background=new SolidColorBrush(Color.FromRgb(16,19,24)),Padding=new Thickness(14),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,20,0,0)});Content=grid;
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape)DialogResult=false;};
        MouseLeftButtonDown+=(_,e)=>{start=e.GetPosition(canvas);box.Visibility=Visibility.Visible;CaptureMouse();Draw(start.Value);};
        MouseMove+=(_,e)=>{if(start!=null)Draw(e.GetPosition(canvas));};
        MouseLeftButtonUp+=(_,e)=>
        {
            if(start==null)return;ReleaseMouseCapture();Draw(e.GetPosition(canvas));
            double left=Canvas.GetLeft(box),top=Canvas.GetTop(box);if(box.Width<12||box.Height<12){start=null;box.Visibility=Visibility.Collapsed;return;}
            int x=Math.Clamp((int)(left/ActualWidth*screenshot.PixelWidth),0,screenshot.PixelWidth-1),y=Math.Clamp((int)(top/ActualHeight*screenshot.PixelHeight),0,screenshot.PixelHeight-1);
            int w=Math.Clamp((int)(box.Width/ActualWidth*screenshot.PixelWidth),1,screenshot.PixelWidth-x),h=Math.Clamp((int)(box.Height/ActualHeight*screenshot.PixelHeight),1,screenshot.PixelHeight-y);
            SelectedImage=new CroppedBitmap(screenshot,new Int32Rect(x,y,w,h));SelectedImage.Freeze();DialogResult=true;
        };
    }
    void Draw(Point end){if(start==null)return;end=new(Math.Clamp(end.X,0,canvas.ActualWidth),Math.Clamp(end.Y,0,canvas.ActualHeight));Canvas.SetLeft(box,Math.Min(start.Value.X,end.X));Canvas.SetTop(box,Math.Min(start.Value.Y,end.Y));box.Width=Math.Abs(end.X-start.Value.X);box.Height=Math.Abs(end.Y-start.Value.Y);}
    [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr handle);
}
