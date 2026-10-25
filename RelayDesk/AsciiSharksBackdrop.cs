using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace RelayDesk;

public sealed class AsciiSharksBackdrop : FrameworkElement
{
    readonly record struct Sample(double X,double Y,double Z,double Nx,double Ny,double Nz);
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(40)};
    readonly Stopwatch elapsed=new();
    readonly List<Sample> mesh=[];
    readonly GlyphTypeface font;
    readonly Dictionary<char,ushort> glyphs=[];
    bool animate=true;
    Window? owner;
    public bool AnimationEnabled {get=>animate;set{animate=value;UpdatePlayback();}}
    public AsciiSharksBackdrop()
    {
        IsHitTestVisible=false;
        var typeface=new Typeface("Consolas");
        if(!typeface.TryGetGlyphTypeface(out var found))new Typeface("Courier New").TryGetGlyphTypeface(out found);
        font=found!;
        foreach(char c in ".,:;irsX#@")glyphs[c]=font.CharacterToGlyphMap[c];
        BuildMesh();
        timer.Tick+=(_,_)=>InvalidateVisual();
        Loaded+=(_,_)=>{owner=Window.GetWindow(this);if(owner!=null)owner.StateChanged+=OwnerStateChanged;UpdatePlayback();};
        Unloaded+=(_,_)=>{timer.Stop();elapsed.Stop();if(owner!=null)owner.StateChanged-=OwnerStateChanged;owner=null;};
        IsVisibleChanged+=(_,_)=>UpdatePlayback();
    }
    void OwnerStateChanged(object? sender,EventArgs e)=>UpdatePlayback();
    void UpdatePlayback()
    {
        bool running=animate && IsLoaded && IsVisible && owner?.WindowState!=WindowState.Minimized;
        if(running){elapsed.Start();timer.Start();}else{timer.Stop();elapsed.Stop();}
        InvalidateVisual();
    }
    static double Radius(double x)
    {
        (double X,double R)[] profile=[(-1.15,.035),(-.8,.075),(-.5,.155),(-.1,.24),(.3,.245),(.65,.185),(.9,.1),(1.08,.005)];
        for(int i=1;i<profile.Length;i++)if(x<=profile[i].X)
        {double t=(x-profile[i-1].X)/(profile[i].X-profile[i-1].X);return profile[i-1].R+(profile[i].R-profile[i-1].R)*t;}
        return .005;
    }
    void BuildMesh()
    {
        for(int i=0;i<=100;i++)
        {
            double x=-1.15+i*2.23/100,r=Radius(x);
            for(int j=0;j<52;j++)
            {
                double angle=j*Math.Tau/52;
                mesh.Add(new(x,r*Math.Cos(angle),r*.72*Math.Sin(angle),.1,Math.Cos(angle),Math.Sin(angle)));
            }
        }
        void Fin((double X,double Y,double Z) a,(double X,double Y,double Z) b,(double X,double Y,double Z) c,int steps=30)
        {
            double ux=b.X-a.X,uy=b.Y-a.Y,uz=b.Z-a.Z,vx=c.X-a.X,vy=c.Y-a.Y,vz=c.Z-a.Z;
            double nx=uy*vz-uz*vy,ny=uz*vx-ux*vz,nz=ux*vy-uy*vx,length=Math.Sqrt(nx*nx+ny*ny+nz*nz);
            for(int i=0;i<=steps;i++)for(int j=0;j<=steps-i;j++)
            {double u=(double)i/steps,v=(double)j/steps;mesh.Add(new(a.X+u*ux+v*vx,a.Y+u*uy+v*vy,a.Z+u*uz+v*vz,nx/length,ny/length,nz/length));}
        }
        Fin((.25,0,.16),(-.18,0,.7),(-.55,0,.13));
        foreach(int side in new[]{-1,1})
        {
            Fin((.38,side*.17,-.055),(-.32,side*.77,-.12),(-.4,side*.18,-.075));
            Fin((-.48,side*.11,-.05),(-.8,side*.34,-.1),(-.89,side*.07,-.04),22);
        }
        Fin((-1.1,0,0),(-1.63,0,.54),(-1.39,0,.02));
        Fin((-1.1,0,0),(-1.5,0,-.34),(-1.39,0,.02));
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if(ActualWidth<1||ActualHeight<1)return;
        dc.DrawRectangle(Brushes.Black,null,new Rect(0,0,ActualWidth,ActualHeight));
        const double cellX=5.3,cellY=7.5;
        int columns=(int)(ActualWidth/cellX)+1,rows=(int)(ActualHeight/cellY)+1;
        var depth=new float[columns*rows];Array.Fill(depth,float.NegativeInfinity);
        var characters=new char[depth.Length];
        double time=elapsed.Elapsed.TotalSeconds;
        double scene=Math.Min(ActualWidth/1500,ActualHeight/950);
        const string ramp=".,:;irsX#@";
        for(int shark=0;shark<3;shark++)
        {
            double phase=time*Math.Tau/38+shark*Math.Tau/3;
            double centerX=ActualWidth*.52+Math.Cos(phase)*ActualWidth*.38;
            double centerY=ActualHeight*.49+Math.Sin(phase)*ActualHeight*.36;
            double heading=Math.Atan2(Math.Cos(phase)*ActualHeight*.36,-Math.Sin(phase)*ActualWidth*.38);
            double pitch=.6*Math.Sin(phase*2+shark*.7),roll=.3+Math.Sin(phase+shark)*.65;
            double ch=Math.Cos(heading),sh=Math.Sin(heading),cp=Math.Cos(pitch),sp=Math.Sin(pitch),cr=Math.Cos(roll),sr=Math.Sin(roll);
            double size=(shark==0?190:shark==1?145:170)*scene;
            foreach(var p in mesh)
            {
                double bend=Math.Max(0,-p.X+.15);
                double wave=Math.Sin(time*2.1-p.X*3.5+shark)*bend*bend*.09;
                double py=p.Y+wave;
                double y=py*cr-p.Z*sr,z=py*sr+p.Z*cr;
                double x=p.X*cp+z*sp;z=-p.X*sp+z*cp;
                double sx=centerX+(x*ch-y*sh)*size,sy=centerY+(x*sh+y*ch)*size;
                int col=(int)(sx/cellX),row=(int)(sy/cellY);
                if(col<0||col>=columns||row<0||row>=rows)continue;
                int at=row*columns+col;
                float distance=(float)(z+shark*1.5);
                if(distance<depth[at])continue;
                double ny=p.Ny*cr-p.Nz*sr,nz=p.Ny*sr+p.Nz*cr;
                double nx=p.Nx*cp+nz*sp;nz=-p.Nx*sp+nz*cp;
                double light=.2+.75*Math.Abs(nx*.2-ny*.35+nz*.8);
                // Slightly sparse gill bands help distinguish the head from the tail.
                if(p.X>.45&&p.X<.68&&Math.Abs(p.Y)>.1&&Math.Sin(p.X*150)>.6)light*=.4;
                int shade=Math.Clamp((int)(light*(ramp.Length-1)),0,ramp.Length-1);
                characters[at]=ramp[shade];depth[at]=distance;
            }
        }
        var indices=new List<ushort>();var advances=new List<double>();var offsets=new List<Point>();
        for(int row=0;row<rows;row++)for(int col=0;col<columns;col++)
        {
            char c=characters[row*columns+col];if(c=='\0')continue;
            indices.Add(glyphs[c]);advances.Add(0);offsets.Add(new Point(col*cellX,-(row*cellY+7)));
        }
        if(indices.Count>0)
        {
            var run=new GlyphRun((float)VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var init=(System.ComponentModel.ISupportInitialize)run;init.BeginInit();run.GlyphTypeface=font;run.FontRenderingEmSize=8;
            run.GlyphIndices=indices;run.AdvanceWidths=advances;run.GlyphOffsets=offsets;run.BaselineOrigin=new Point(0,0);init.EndInit();
            dc.PushClip(new RectangleGeometry(new Rect(0,0,ActualWidth,ActualHeight)));dc.DrawGlyphRun(Brushes.White,run);dc.Pop();
        }
    }
}
