using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;

namespace RelayDesk;

public static class BrowserAddressDrag
{
    static readonly SemaphoreSlim reader=new(1,1);
    // Read only the address control under the initial mouse press, never page
    // content or browser profiles. Run UI Automation on a background MTA thread.
    public static string? Read(int x,int y)
    {
        if(!reader.Wait(0))return null;
        try
        {
            var point=new NativePoint{X=x,Y=y};var root=GetAncestor(WindowFromPoint(point),2);var name=new System.Text.StringBuilder(100);GetClassName(root,name,100);
            if(!name.ToString().StartsWith("Chrome",StringComparison.Ordinal)||!GetWindowRect(root,out var rect)||y>rect.Top+160*GetDpiForWindow(root)/96.0)return null;
            var element=AutomationElement.FromPoint(new Point(x,y));
            for(int i=0;i<3&&element!=null;i++,element=TreeWalker.ControlViewWalker.GetParent(element))
            {
                var info=element.Current;
                if(info.ClassName=="SmartboxButtonView::DomainLabel")return Normalize(info.Name.Split(' ',StringSplitOptions.RemoveEmptyEntries).LastOrDefault()??"");
                bool address=IsAddressControl(info.ClassName,info.AutomationId,info.Name);
                if(address&&element.TryGetCurrentPattern(ValuePattern.Pattern,out var pattern))return Normalize(((ValuePattern)pattern).Current.Value);
            }
        }
        catch(Exception){/* Address controls can disappear as the browser starts a drag. */}
        finally{reader.Release();}
        return null;
    }
    public static bool IsAddressControl(string className,string id,string name)=>className.Contains("Omnibox",StringComparison.OrdinalIgnoreCase)||className.Contains("Smartbox",StringComparison.OrdinalIgnoreCase)||id.Contains("address",StringComparison.OrdinalIgnoreCase)||name.Contains("Адресная строка",StringComparison.OrdinalIgnoreCase)||name.Contains("Address and search",StringComparison.OrdinalIgnoreCase);
    public static string? Normalize(string value)
    {
        value=value.Trim();if(value.Length is 0 or >16_384||value.Any(char.IsWhiteSpace))return null;
        if(!value.Contains("://",StringComparison.Ordinal))value="https://"+value;
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https")||uri.UserInfo.Length>0)return null;
        try{_=OverlayDrops.Rule(value);return value;}catch(Exception){return null;}
    }
    [StructLayout(LayoutKind.Sequential)]struct NativePoint{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]struct NativeRect{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr window,uint flag);
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out NativeRect rect);
    [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr window,System.Text.StringBuilder name,int size);
}
