using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Threading;

namespace RelayDesk;

public sealed class AnimatedBrandTitle : FrameworkElement
{
    const string Brand="RelayDesk";
    readonly Stopwatch time=new();
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(40)};
    readonly Typeface typeface=new(new FontFamily("Segoe UI"),FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal);
    bool enabled=true;
    Window? owner;
    public bool AnimationEnabled {get=>enabled;set{enabled=value;Playback();}}
    public AnimatedBrandTitle()
    {
        Width=148;Height=34;IsHitTestVisible=false;
        timer.Tick+=(_,_)=>InvalidateVisual();
        Loaded+=(_,_)=>{owner=Window.GetWindow(this);if(owner!=null)owner.StateChanged+=OwnerChanged;Playback();};
        Unloaded+=(_,_)=>{timer.Stop();time.Stop();if(owner!=null)owner.StateChanged-=OwnerChanged;owner=null;};
        IsVisibleChanged+=(_,_)=>Playback();
    }
    void OwnerChanged(object? sender,EventArgs e)=>Playback();
    void Playback()
    {
        if(enabled&&IsLoaded&&IsVisible&&owner?.WindowState!=WindowState.Minimized){time.Start();timer.Start();}
        else{time.Stop();timer.Stop();}
        InvalidateVisual();
    }
    FormattedText Text(string value,Brush brush)=>new(value,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,typeface,24,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip);
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var ink=new SolidColorBrush(Color.FromRgb(236,238,242));
        if(!enabled){dc.DrawText(Text(Brand,ink),new Point(0,1));return;}
        double t=time.Elapsed.TotalSeconds%14;
        if(t<.2)ink.Opacity=t/.2;
        if(t<6.1)
        {
            int length=t<2?Math.Clamp(1+(int)(t/.21),1,Brand.Length):t<3.8?Brand.Length:Math.Clamp(Brand.Length-(int)((t-3.8)/.14),0,Brand.Length);
            string value=Brand[..length];var text=Text(value,ink);dc.DrawText(text,new Point(0,1));
            // Cursor accompanies typing/deleting, then rests while the word is held.
            if((t<2||t>=3.8)&&t<5.45&&Math.Sin(t*Math.PI*2.4)>-.4)
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(150,210,218,230)),null,new Rect(text.WidthIncludingTrailingWhitespace+3,6,1,22));
            return;
        }
        // Second treatment: a staggered appearance followed by one silver light pass.
        for(int i=0;i<Brand.Length;i++)
        {
            double progress=Math.Clamp((t-6.1-i*.065)/.45,0,1);
            double opacity=progress*progress*(3-2*progress);
            double outro=Math.Clamp((t-13.3)/.7,0,1);opacity*=1-outro*outro*(3-2*outro);
            double prefix=i==0?0:Text(Brand[..i],ink).WidthIncludingTrailingWhitespace;
            double light=t>=7.4&&t<=9?Math.Exp(-Math.Pow((i-(t-7.4)/1.6*(Brand.Length+3)+1)/1.35,2)):0;
            byte value=(byte)(218+light*37);
            var brush=new SolidColorBrush(Color.FromArgb((byte)(255*opacity),value,value,(byte)Math.Min(255,value+4)));
            dc.DrawText(Text(Brand[i].ToString(),brush),new Point(prefix,1));
        }
    }
    protected override AutomationPeer OnCreateAutomationPeer()=>new BrandPeer(this);
    sealed class BrandPeer(AnimatedBrandTitle owner):FrameworkElementAutomationPeer(owner)
    {
        protected override string GetNameCore()=>Brand;
        protected override AutomationControlType GetAutomationControlTypeCore()=>AutomationControlType.Text;
    }
}
