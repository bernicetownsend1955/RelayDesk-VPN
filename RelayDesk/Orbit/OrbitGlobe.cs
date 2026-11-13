using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace RelayDesk.Orbit;

public sealed class OrbitScene:FrameworkElement
{
    readonly record struct Dot(double X,double Y,double Z,bool Land);
    sealed record StarLayer(DrawingVisual Visual,TranslateTransform Shift,double Speed,int Index);
    readonly List<Dot> dots=[];
    readonly List<StarLayer> stars=[];
    readonly VisualCollection visuals;
    readonly DrawingVisual disk=new(),signal=new();
    readonly Viewport3D globe=new(){IsHitTestVisible=false,ClipToBounds=false};
    readonly MatrixTransform3D rotation=new();
    readonly CompositorLoop loop;
    readonly SolidColorBrush ink=new(Colors.White);
    readonly Pen beam,edge=new(new SolidColorBrush(Color.FromRgb(38,38,38)),.8);
    Window? owner;Place? place,previousPlace;
    FormattedText? label,previousLabel;
    string scene="earth";
    bool motion=true,connected,layoutDirty=true;
    double yaw=.25,pitch=.55,toYaw=.25,toPitch=.55,yawVelocity,pitchVelocity,selectedAt=-10,activation;
    double width,height,radius,cx,cy,controlInset=290,earthOpacity=1,skyOpacity=.52,earthTarget=1,skyTarget=.52;
    public double CameraYaw=>yaw;
    public double CameraVelocity=>yawVelocity;
    public double TargetError=>Math.Abs(toYaw-yaw)+Math.Abs(toPitch-pitch);
    public int RenderCount=>loop.Count;
    public double RenderMilliseconds{get;private set;}
    public int LandPointCount=>dots.Count(d=>d.Land);
    public int RenderTier=>RenderCapability.Tier>>16;
    public double ControlInset{get=>controlInset;set{if(Math.Abs(controlInset-value)<.1)return;controlInset=value;layoutDirty=true;InvalidateArrange();InvalidateVisual();}}
    public string Scene{get=>scene;set{if(scene==value)return;scene=value;UpdateSceneOpacity();Playback();}}
    public bool Connected
    {
        get=>connected;set
        {
            if(connected==value)return;connected=value;
            var color=value?Color.FromRgb(117,199,147):Colors.White;
            if(motion)ink.BeginAnimation(SolidColorBrush.ColorProperty,new ColorAnimation(ink.Color,color,TimeSpan.FromMilliseconds(300)));
            else {ink.BeginAnimation(SolidColorBrush.ColorProperty,null);ink.Color=color;activation=value?1:0;}
            label=Label(place);DrawSignal(loop.Time);
        }
    }
    public bool AnimationEnabled
    {
        get=>motion;set
        {
            if(motion==value)return;motion=value;
            if(!motion){yaw=toYaw;pitch=toPitch;yawVelocity=pitchVelocity=0;activation=connected?1:0;ink.BeginAnimation(SolidColorBrush.ColorProperty,null);ink.Color=connected?Color.FromRgb(117,199,147):Colors.White;}
            UpdateRotation();UpdateSceneOpacity();Playback();DrawSignal(loop.Time);
        }
    }
    public OrbitScene()
    {
        IsHitTestVisible=false;ClipToBounds=true;beam=new Pen(ink,1.3);
        visuals=new VisualCollection(this);loop=new CompositorLoop(Frame);
        for(int i=0;i<6;i++){var layer=new StarLayer(new DrawingVisual(),new TranslateTransform(),.25+i*.14,i);layer.Visual.Transform=layer.Shift;stars.Add(layer);visuals.Add(layer.Visual);}
        visuals.Add(disk);visuals.Add(globe);visuals.Add(signal);
        if(App.UseOrbitUi){BuildEarth();BuildGlobe();}
        Loaded+=(_,_)=>{owner=Window.GetWindow(this);if(owner!=null)owner.StateChanged+=OwnerChanged;Playback();};
        Unloaded+=(_,_)=>{loop.Dispose();if(owner!=null)owner.StateChanged-=OwnerChanged;owner=null;};
        IsVisibleChanged+=(_,_)=>Playback();
    }
    protected override int VisualChildrenCount=>visuals?.Count??0;
    protected override Visual GetVisualChild(int index)=>visuals[index];
    static double Smooth(double t){t=Math.Clamp(t,0,1);return t*t*t*(10+t*(-15+6*t));}
    static void Follow(ref double position,ref double velocity,double target,double dt)
    {const double omega=2.35;double offset=position-target,c=velocity+omega*offset,decay=Math.Exp(-omega*dt);position=target+(offset+c*dt)*decay;velocity=(velocity-omega*c*dt)*decay;}
    void OwnerChanged(object? sender,EventArgs e)=>Playback();
    void Playback()=>loop.SetRunning(IsLoaded&&IsVisible&&owner?.WindowState!=WindowState.Minimized&&motion&&(scene!="none"||earthOpacity>.001||skyOpacity>.001));
    void UpdateSceneOpacity()
    {
        earthTarget=scene=="earth"?1:0;skyTarget=scene=="none"?0:scene=="earth"?.52:1;
        if(!motion){earthOpacity=earthTarget;skyOpacity=skyTarget;}
        ApplySceneOpacity();
    }
    void ApplySceneOpacity(){globe.Opacity=earthOpacity;disk.Opacity=earthOpacity;signal.Opacity=earthOpacity;foreach(var star in stars)star.Visual.Opacity=skyOpacity;}
    public void Focus(Place? target)
    {
        if(place==target)return;previousPlace=place;previousLabel=label;place=target;selectedAt=loop.Time;
        if(target==null){toYaw=yaw;toPitch=pitch;}
        else{double desired=target.Longitude*Math.PI/180;toYaw=yaw+Math.Atan2(Math.Sin(desired-yaw),Math.Cos(desired-yaw));toPitch=Math.Clamp(target.Latitude*Math.PI/180-.34,-1.2,1.2);}
        label=Label(place);if(!motion){yaw=toYaw;pitch=toPitch;yawVelocity=pitchVelocity=0;UpdateRotation();}DrawSignal(loop.Time);
    }
    FormattedText? Label(Place? p)=>p==null?null:new FormattedText(p.Country+" · примерно"+(connected?" · VPN":""),CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,ink,VisualTreeHelper.GetDpi(this).PixelsPerDip);
    void Frame(double time,double dt)
    {
        var watch=Stopwatch.StartNew();
        if(place!=null){Follow(ref yaw,ref yawVelocity,toYaw,dt);Follow(ref pitch,ref pitchVelocity,toPitch,dt);}
        else{double speed=.028;double blend=1-Math.Exp(-dt*2);yawVelocity+=(speed-yawVelocity)*blend;yaw+=yawVelocity*dt;}
        activation+=( (connected?1:0)-activation)*(1-Math.Exp(-dt*9));
        double fade=1-Math.Exp(-dt*10);earthOpacity+=(earthTarget-earthOpacity)*fade;skyOpacity+=(skyTarget-skyOpacity)*fade;
        if(Math.Abs(earthOpacity-earthTarget)<.001)earthOpacity=earthTarget;if(Math.Abs(skyOpacity-skyTarget)<.001)skyOpacity=skyTarget;ApplySceneOpacity();
        UpdateRotation();
        foreach(var star in stars){star.Shift.X=width>0?(time*star.Speed*4)%width:0;star.Shift.Y=height>0?(time*star.Speed*.8)%height:0;}
        DrawSignal(time);RenderMilliseconds=watch.Elapsed.TotalMilliseconds;if(scene=="none"&&earthOpacity<.001&&skyOpacity<.001)Playback();
    }
    void UpdateRotation()
    {
        double a=Math.Cos(yaw),b=Math.Sin(yaw),c=Math.Cos(pitch),d=Math.Sin(pitch);
        rotation.Matrix=new Matrix3D(a,-b*d,b*c,0, 0,c,d,0, -b,-a*d,a*c,0, 0,0,0,1);
    }
    (double X,double Y,double Z) Project(Place p)
    {
        double lat=p.Latitude*Math.PI/180,lon=p.Longitude*Math.PI/180,x=Math.Cos(lat)*Math.Sin(lon),y=Math.Sin(lat),z=Math.Cos(lat)*Math.Cos(lon);
        double a=x*Math.Cos(yaw)-z*Math.Sin(yaw),b=x*Math.Sin(yaw)+z*Math.Cos(yaw);
        return(a,y*Math.Cos(pitch)-b*Math.Sin(pitch),y*Math.Sin(pitch)+b*Math.Cos(pitch));
    }
    void DrawSignal(double time)
    {
        using var dc=signal.RenderOpen();
        void Marker(Place target,FormattedText? title,double opacity,double growth)
        {
            var p=Project(target);opacity*=Smooth(p.Z/.16);if(opacity<.001)return;
            var anchor=new Point(cx+p.X*radius,cy-p.Y*radius);double length=radius*.65*growth;
            dc.PushOpacity(opacity);dc.DrawLine(beam,anchor,new Point(anchor.X,anchor.Y-length));
            dc.DrawEllipse(ink,null,anchor,3.5+activation,3.5+activation);dc.DrawEllipse(null,beam,anchor,9,4);
            dc.DrawEllipse(ink,null,new Point(anchor.X,anchor.Y-length),2+activation,2+activation);
            if(title!=null)
            {dc.PushOpacity(Smooth((growth-.5)/.5));double x=anchor.X-title.Width/2,y=anchor.Y-length-32;dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(19,19,19)),edge,new Rect(x-10,y-6,title.Width+20,29),6,6);dc.DrawText(title,new Point(x,y));dc.Pop();}
            dc.Pop();
        }
        double age=time-selectedAt;
        if(previousPlace!=null&&motion&&age<.4)Marker(previousPlace,previousLabel,1-Smooth(age/.4),1);
        if(place!=null)Marker(place,label,motion?Smooth(age/.6):1,motion?Smooth((age-.25)/2.1):1);
    }
    protected override Size MeasureOverride(Size available){globe.Measure(new Size(600,600));return new Size();}
    protected override Size ArrangeOverride(Size size)
    {
        if(size.Width!=width||size.Height!=height)layoutDirty=true;width=size.Width;height=size.Height;
        radius=Math.Min(width*.32,Math.Max(60,(height-ControlInset-80)/2));cx=width*.5;cy=80+radius;
        globe.Arrange(new Rect(cx-radius,cy-radius,radius*2,radius*2));return size;
    }
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Black,null,new Rect(RenderSize));if(layoutDirty){BuildSky();using(var diskDrawing=disk.RenderOpen())diskDrawing.DrawEllipse(new SolidColorBrush(Color.FromRgb(5,5,5)),edge,new Point(cx,cy),radius,radius);layoutDirty=false;UpdateSceneOpacity();}UpdateRotation();DrawSignal(loop.Time);
    }
    void BuildSky()
    {
        if(width<1||height<1)return;
        for(int layer=0;layer<stars.Count;layer++)
        {
            var random=new Random(26011+layer);var tile=new DrawingVisual();
            using(var drawing=tile.RenderOpen())for(int i=0;i<250;i++)
            {
                double x=random.NextDouble()*width,y=random.NextDouble()*height,r=random.NextDouble()<.97?.4+random.NextDouble()*.75:1.35;
                byte shade=(byte)random.Next(45,205);var brush=new SolidColorBrush(Color.FromRgb(shade,shade,shade));brush.Freeze();
                drawing.DrawEllipse(brush,null,new Point(x,y),r,r);
            }
            var texture=new RenderTargetBitmap((int)Math.Ceiling(width),(int)Math.Ceiling(height),96,96,PixelFormats.Pbgra32);texture.Render(tile);texture.Freeze();
            using var dc=stars[layer].Visual.RenderOpen();for(int dx=-1;dx<=1;dx++)for(int dy=-1;dy<=1;dy++)dc.DrawImage(texture,new Rect(dx*width,dy*height,width,height));
        }
    }
    void BuildGlobe()
    {
        const int tw=3072,th=1536;
        var image=new DrawingVisual();using(var dc=image.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(5,5,5)),null,new Rect(0,0,tw,th));
            var land=new SolidColorBrush(Color.FromRgb(237,237,237));var ocean=new SolidColorBrush(Color.FromRgb(43,43,43));
            foreach(var dot in dots)
            {
                double lat=Math.Asin(dot.Y),lon=Math.Atan2(dot.X,dot.Z),r=dot.Land?2.35:1.25;
                var point=new Point((lon+Math.PI)/Math.Tau*tw,(Math.PI/2-lat)/Math.PI*th);
                dc.DrawEllipse(dot.Land?land:ocean,null,point,r/Math.Max(.08,Math.Cos(lat)),r);
            }
        }
        var texture=new RenderTargetBitmap(tw,th,96,96,PixelFormats.Pbgra32);texture.Render(image);texture.Freeze();
        var mesh=new MeshGeometry3D();const int slices=128,stacks=72;
        for(int row=0;row<=stacks;row++)for(int col=0;col<=slices;col++)
        {
            double lat=Math.PI/2-Math.PI*row/stacks,lon=-Math.PI+Math.Tau*col/slices,x=Math.Cos(lat)*Math.Sin(lon),y=Math.Sin(lat),z=Math.Cos(lat)*Math.Cos(lon);
            mesh.Positions.Add(new Point3D(x,y,z));mesh.Normals.Add(new Vector3D(x,y,z));mesh.TextureCoordinates.Add(new Point((double)col/slices,(double)row/stacks));
        }
        for(int row=0;row<stacks;row++)for(int col=0;col<slices;col++)
        {int a=row*(slices+1)+col,b=a+slices+1;foreach(int index in new[]{a,b,a+1,a+1,b,b+1})mesh.TriangleIndices.Add(index);}
        mesh.Freeze();var brush=new ImageBrush(texture){ViewportUnits=BrushMappingMode.RelativeToBoundingBox,Stretch=Stretch.Fill};brush.Freeze();
        var material=new DiffuseMaterial(brush);material.Freeze();
        var world=new Model3DGroup();world.Children.Add(new AmbientLight(Color.FromRgb(120,120,120)));world.Children.Add(new DirectionalLight(Color.FromRgb(150,150,150),new Vector3D(-.15,-.1,-1)));
        world.Children.Add(new GeometryModel3D(mesh,material){Transform=rotation});
        globe.Camera=new OrthographicCamera(new Point3D(0,0,3),new Vector3D(0,0,-1),new Vector3D(0,1,0),2);
        globe.Children.Add(new ModelVisual3D{Content=world});RenderOptions.SetBitmapScalingMode(globe,BitmapScalingMode.HighQuality);
    }
    void BuildEarth()
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(n=>n.EndsWith("land.geojson")).Select(n=>Assembly.GetExecutingAssembly().GetManifestResourceStream(n)!).First();
        using var data=JsonDocument.Parse(stream);var shape=new StreamGeometry{FillRule=FillRule.EvenOdd};
        using(var drawing=shape.Open())
        {
            void Polygon(JsonElement polygon)
            {
                foreach(var points in polygon.EnumerateArray())
                {var ring=points.EnumerateArray().ToArray();if(ring.Length<3)continue;Point Convert(JsonElement p)=>new((p[0].GetDouble()+180)*4,(90-p[1].GetDouble())*4);drawing.BeginFigure(Convert(ring[0]),true,true);for(int i=1;i<ring.Length;i++)drawing.LineTo(Convert(ring[i]),false,false);}
            }
            foreach(var feature in data.RootElement.GetProperty("features").EnumerateArray())
            {var geometry=feature.GetProperty("geometry");var coordinates=geometry.GetProperty("coordinates");if(geometry.GetProperty("type").GetString()=="Polygon")Polygon(coordinates);else foreach(var polygon in coordinates.EnumerateArray())Polygon(polygon);}
        }
        shape.Freeze();var visual=new DrawingVisual();using(var dc=visual.RenderOpen())dc.DrawGeometry(Brushes.White,null,shape);
        var mask=new RenderTargetBitmap(1440,720,96,96,PixelFormats.Pbgra32);mask.Render(visual);var pixels=new byte[1440*720*4];mask.CopyPixels(pixels,1440*4,0);
        const int count=29000;double angle=Math.PI*(3-Math.Sqrt(5));
        for(int i=0;i<count;i++)
        {double y=1-2*(i+.5)/count,lat=Math.Asin(y),lon=(i*angle)%Math.Tau-Math.PI,r=Math.Sqrt(1-y*y);int mx=Math.Clamp((int)((lon*180/Math.PI+180)*4),0,1439),my=Math.Clamp((int)((90-lat*180/Math.PI)*4),0,719);bool land=pixels[(my*1440+mx)*4+3]>100;if(land||i%5==0)dots.Add(new(r*Math.Sin(lon),y,r*Math.Cos(lon),land));}
    }
}
