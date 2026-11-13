using System.Diagnostics;
using System.Windows.Media;

namespace RelayDesk.Orbit;

// Shared frame source: no dispatcher polling and no duplicate work for the same presented frame.
public sealed class CompositorLoop(Action<double,double> frame):IDisposable
{
    readonly Stopwatch clock=new();
    TimeSpan lastStamp=TimeSpan.MinValue;
    double previous;
    bool running;
    public double Time=>clock.Elapsed.TotalSeconds;
    public int Count{get;private set;}
    public void SetRunning(bool value)
    {
        if(value==running)return;running=value;
        if(value){previous=Time;lastStamp=TimeSpan.MinValue;clock.Start();CompositionTarget.Rendering+=Tick;}
        else{CompositionTarget.Rendering-=Tick;clock.Stop();}
    }
    void Tick(object? sender,EventArgs args)
    {
        if(args is RenderingEventArgs rendered){if(rendered.RenderingTime==lastStamp)return;lastStamp=rendered.RenderingTime;}
        double now=Time,delta=Math.Clamp(now-previous,0,.05);previous=now;Count++;frame(now,delta);
    }
    public void Dispose()=>SetRunning(false);
}
