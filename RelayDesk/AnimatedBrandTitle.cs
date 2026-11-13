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
    readonly RelayDesk.Orbit.CompositorLoop frames;
    readonly Typeface typeface=new(new FontFamily("Segoe UI"),FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal);
    readonly SolidColorBrush ink=new(Color.FromRgb(236,238,242));
    FormattedText[] letters=[];
    double[] positions=[],advances=[];
    double cachedDpi;
    bool enabled=true;
    Window? owner;
    public int FrameCount=>frames.Count;
    public bool AnimationEnabled {get=>enabled;set{enabled=value;Playback();}}
    public AnimatedBrandTitle()
    {
        Width=148;Height=34;IsHitTestVisible=false;
        frames=new RelayDesk.Orbit.CompositorLoop((_,_)=>InvalidateVisual());
        Loaded+=(_,_)=>{owner=Window.GetWindow(this);if(owner!=null)owner.StateChanged+=OwnerChanged;Playback();};
        Unloaded+=(_,_)=>{frames.SetRunning(false);time.Stop();if(owner!=null)owner.StateChanged-=OwnerChanged;owner=null;};
        IsVisibleChanged+=(_,_)=>Playback();
    }
    void OwnerChanged(object? sender,EventArgs e)=>Playback();
    void Playback()
    {
        if(enabled&&IsLoaded&&IsVisible&&owner?.WindowState!=WindowState.Minimized){time.Start();frames.SetRunning(true);}
        else{time.Stop();frames.SetRunning(false);}
        InvalidateVisual();
    }
    FormattedText Text(string value,Brush brush)=>new(value,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,typeface,24,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip);
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if(letters.Length==0||cachedDpi!=dpi)
        {cachedDpi=dpi;letters=Brand.Select(c=>Text(c.ToString(),ink)).ToArray();positions=Enumerable.Range(0,Brand.Length+1).Select(i=>i==0?0:Text(Brand[..i],ink).WidthIncludingTrailingWhitespace).ToArray();advances=Enumerable.Range(0,Brand.Length).Select(i=>positions[i+1]-positions[i]).ToArray();}
        ink.Opacity=1;
        if(!enabled){dc.DrawText(Text(Brand,ink),new Point(0,1));return;}
        double t=time.Elapsed.TotalSeconds%14;
        if(t<.2)ink.Opacity=t/.2;
        if(t<6.1)
        {
            static double Ease(double v){v=Math.Clamp(v,0,1);return v*v*(3-2*v);}
            double cursor=0;
            for(int i=0;i<Brand.Length;i++)
            {
                double opacity=Ease((t-i*.19)/.19)*(1-Ease((t-3.8-(Brand.Length-1-i)*.14)/.2));
                dc.PushOpacity(opacity);dc.DrawText(letters[i],new Point(positions[i],1));dc.Pop();
                cursor+=advances[i]*opacity;
            }
            if((t<2||t>=3.8)&&t<5.45)
            {dc.PushOpacity(.35+.35*(1+Math.Sin(t*Math.PI*2.4))*.5);dc.DrawRectangle(ink,null,new Rect(cursor+3,6,1,22));dc.Pop();}
            return;
        }
        // Second treatment: a staggered appearance followed by one silver light pass.
        for(int i=0;i<Brand.Length;i++)
        {
            double progress=Math.Clamp((t-6.1-i*.065)/.45,0,1);
            double opacity=progress*progress*(3-2*progress);
            double outro=Math.Clamp((t-13.3)/.7,0,1);opacity*=1-outro*outro*(3-2*outro);
            double prefix=positions[i];
            double light=t>=7.4&&t<=9?Math.Exp(-Math.Pow((i-(t-7.4)/1.6*(Brand.Length+3)+1)/1.35,2)):0;
            dc.PushOpacity(opacity*(.92+.08*light));dc.DrawText(letters[i],new Point(prefix,1));dc.Pop();
        }
    }
    protected override AutomationPeer OnCreateAutomationPeer()=>new BrandPeer(this);
    sealed class BrandPeer(AnimatedBrandTitle owner):FrameworkElementAutomationPeer(owner)
    {
        protected override string GetNameCore()=>Brand;
        protected override AutomationControlType GetAutomationControlTypeCore()=>AutomationControlType.Text;
    }
}
