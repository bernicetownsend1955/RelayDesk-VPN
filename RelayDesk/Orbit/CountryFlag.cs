using System.Windows;
using System.Windows.Media;

namespace RelayDesk.Orbit;
public sealed class CountryFlag:FrameworkElement
{
    string code="";public string Code {get=>code;set{code=value;InvalidateVisual();}}
    public CountryFlag(){Width=24;Height=24;IsHitTestVisible=false;}
    protected override void OnRender(DrawingContext dc)
    {
        double w=ActualWidth,h=ActualHeight;dc.PushClip(new EllipseGeometry(new Point(w/2,h/2),w/2,h/2));
        SolidColorBrush Ink(string color)=>new((Color)ColorConverter.ConvertFromString(color));
        void Fill(string color)=>dc.DrawRectangle(Ink(color),null,new Rect(0,0,w,h));
        void Stripes(params string[] colors){for(int i=0;i<colors.Length;i++)dc.DrawRectangle(Ink(colors[i]),null,new Rect(0,i*h/colors.Length,w,h/colors.Length+1));}
        void Cross(string ground,string cross){Fill(ground);dc.DrawRectangle(Ink(cross),null,new Rect(w*.29,0,w*.17,h));dc.DrawRectangle(Ink(cross),null,new Rect(0,h*.42,w,h*.17));}
        void Star(double x,double y,double r,Brush brush){var geometry=new StreamGeometry();using(var path=geometry.Open()){for(int i=0;i<10;i++){double a=-Math.PI/2+i*Math.PI/5,s=i%2==0?r:r*.42;var point=new Point(x+Math.Cos(a)*s,y+Math.Sin(a)*s);if(i==0)path.BeginFigure(point,true,true);else path.LineTo(point,false,false);}}dc.DrawGeometry(brush,null,geometry);}
        void Jack(double width,double height){dc.DrawRectangle(Ink("#18366A"),null,new Rect(0,0,width,height));dc.DrawLine(new Pen(Brushes.White,height*.21),new Point(0,0),new Point(width,height));dc.DrawLine(new Pen(Brushes.White,height*.21),new Point(width,0),new Point(0,height));dc.DrawLine(new Pen(Ink("#C8102E"),height*.07),new Point(0,0),new Point(width,height));dc.DrawLine(new Pen(Ink("#C8102E"),height*.07),new Point(width,0),new Point(0,height));dc.DrawRectangle(Brushes.White,null,new Rect(0,height*.35,width,height*.3));dc.DrawRectangle(Brushes.White,null,new Rect(width*.38,0,width*.24,height));dc.DrawRectangle(Ink("#C8102E"),null,new Rect(0,height*.42,width,height*.16));dc.DrawRectangle(Ink("#C8102E"),null,new Rect(width*.44,0,width*.12,height));}
        switch(Code)
        {
            case "SE":Cross("#006AA7","#FECC00");break;case "FI":Cross("#FFFFFF","#003580");break;case "DE":Stripes("#191919","#D12424","#F4CE45");break;case "NL":Stripes("#AE1C28","#FFFFFF","#21468B");break;case "PL":Stripes("#FFFFFF","#DC143C");break;case "RU":Stripes("#FFFFFF","#2355AB","#D52B1E");break;case "EE":Stripes("#4891D9","#171717","#FFFFFF");break;
            case "LT":Stripes("#FDB913","#006A44","#C1272D");break;
            case "FR":for(int i=0;i<3;i++)dc.DrawRectangle(Ink(new[]{"#253D91","#FFFFFF","#DE323C"}[i]),null,new Rect(i*w/3,0,w/3+1,h));break;
            case "JP":Fill("#FFFFFF");dc.DrawEllipse(Ink("#BC002D"),null,new Point(w/2,h/2),w*.25,h*.25);break;
            case "CH":Fill("#D52B1E");dc.DrawRectangle(Brushes.White,null,new Rect(w*.4,h*.18,w*.2,h*.64));dc.DrawRectangle(Brushes.White,null,new Rect(w*.18,h*.4,w*.64,h*.2));break;
            case "US":for(int i=0;i<13;i++)dc.DrawRectangle(i%2==0?Ink("#B22234"):Brushes.White,null,new Rect(0,h*i/13,w,h/13+1));dc.DrawRectangle(Ink("#3C3B6E"),null,new Rect(0,0,w*.55,h*.54));for(int j=0;j<4;j++)for(int i=0;i<4;i++)dc.DrawEllipse(Brushes.White,null,new Point(w*(.09+i*.12),h*(.06+j*.13)),.45,.45);break;
            case "SG":Stripes("#EF3340","#FFFFFF");dc.DrawEllipse(Brushes.White,null,new Point(w*.32,h*.27),w*.14,h*.17);dc.DrawEllipse(Ink("#EF3340"),null,new Point(w*.39,h*.24),w*.12,h*.14);for(int i=0;i<5;i++)Star(w*.59+Math.Cos(i*Math.Tau/5)*w*.12,h*.25+Math.Sin(i*Math.Tau/5)*h*.12,w*.035,Brushes.White);break;
            case "TR":Fill("#E30A17");dc.DrawEllipse(Brushes.White,null,new Point(w*.43,h*.5),w*.23,h*.23);dc.DrawEllipse(Ink("#E30A17"),null,new Point(w*.5,h*.46),w*.19,h*.19);Star(w*.7,h*.5,w*.13,Brushes.White);break;
            case "GB":Jack(w,h);break;
            case "AU":Fill("#18366A");Jack(w*.6,h*.5);Star(w*.7,h*.7,w*.1,Brushes.White);Star(w*.8,h*.3,w*.08,Brushes.White);Star(w*.35,h*.75,w*.1,Brushes.White);break;
            case "CA":Fill("#FFFFFF");dc.DrawRectangle(Ink("#D52B1E"),null,new Rect(0,0,w*.24,h));dc.DrawRectangle(Ink("#D52B1E"),null,new Rect(w*.76,0,w*.24,h));Star(w*.5,h*.49,w*.28,Ink("#D52B1E"));dc.DrawLine(new Pen(Ink("#D52B1E"),1.2),new Point(w*.5,h*.5),new Point(w*.5,h*.82));break;
            case "KR":Fill("#FFFFFF");dc.DrawEllipse(Ink("#CD2E3A"),null,new Point(w*.5,h*.46),w*.24,h*.24);dc.PushClip(new RectangleGeometry(new Rect(0,h*.46,w,h*.5)));dc.DrawEllipse(Ink("#0047A0"),null,new Point(w*.5,h*.46),w*.24,h*.24);dc.Pop();for(int i=0;i<3;i++){dc.DrawLine(new Pen(Brushes.Black,1),new Point(w*.17,h*(.22+i*.06)),new Point(w*.32,h*(.17+i*.06)));dc.DrawLine(new Pen(Brushes.Black,1),new Point(w*.69,h*(.67+i*.06)),new Point(w*.84,h*(.62+i*.06)));}break;
            case "BR":Fill("#009739");var g=new StreamGeometry();using(var c=g.Open()){c.BeginFigure(new Point(w*.5,h*.1),true,true);c.LineTo(new Point(w*.95,h*.5),false,false);c.LineTo(new Point(w*.5,h*.9),false,false);c.LineTo(new Point(w*.05,h*.5),false,false);}dc.DrawGeometry(Ink("#FFDF00"),null,g);dc.DrawEllipse(Ink("#002776"),null,new Point(w*.5,h*.5),w*.23,h*.23);break;
            default:Fill("#303030");var text=new FormattedText(Code.Length==2?Code:"?",System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),10,Brushes.White,VisualTreeHelper.GetDpi(this).PixelsPerDip);dc.DrawText(text,new Point((w-text.Width)/2,(h-text.Height)/2));break;
        }
        dc.Pop();dc.DrawEllipse(null,new Pen(new SolidColorBrush(Color.FromArgb(45,255,255,255)),.7),new Point(w/2,h/2),w/2-.5,h/2-.5);
    }
}
