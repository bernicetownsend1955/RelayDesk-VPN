using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace RelayDesk;
public partial class ModernTheme : ResourceDictionary
{
    public ModernTheme(){InitializeComponent();}
    public static bool Motion = SystemParameters.ClientAreaAnimation;
    public static void Animate(DependencyObject target, DependencyProperty property, double to, int milliseconds=200)
    {
        if(target is IAnimatable animated) animated.BeginAnimation(property,new DoubleAnimation(to,TimeSpan.FromMilliseconds(Motion?milliseconds:0)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});
    }
    static void Hover(object sender,double opacity,int duration)
    {
        if(sender is Button b && b.Template.FindName("Hover",b) is Border hover)Animate(hover,UIElement.OpacityProperty,opacity,duration);
    }
    void ButtonEnter(object sender,MouseEventArgs e)
    {
        Hover(sender,.055,130);
    }
    void ButtonLeave(object sender,MouseEventArgs e)
    {
        Hover(sender,0,150);
    }
    void ButtonDown(object sender,MouseButtonEventArgs e)=>Hover(sender,.1,70);
    void ButtonUp(object sender,MouseButtonEventArgs e)=>Hover(sender,.055,110);
}
public partial class App
{
    public static bool Motion {get=>ModernTheme.Motion;set=>ModernTheme.Motion=value;}
    public static void Animate(DependencyObject target,DependencyProperty property,double to,int milliseconds=200)=>ModernTheme.Animate(target,property,to,milliseconds);
}
