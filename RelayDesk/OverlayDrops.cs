using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace RelayDesk;
public sealed record OverlayDrop(string[] Files,string Text);
public static class OverlayDrops
{
    public static bool Supports(IDataObject data)=>data.GetDataPresent(DataFormats.FileDrop)||data.GetDataPresent(DataFormats.UnicodeText)||data.GetDataPresent(DataFormats.Text)||data.GetDataPresent("UniformResourceLocatorW")||data.GetDataPresent("UniformResourceLocator");
    public static OverlayDrop Read(IDataObject data)
    {
        var files=data.GetDataPresent(DataFormats.FileDrop)?(data.GetData(DataFormats.FileDrop) as string[]??[]):[];
        var text=data.GetDataPresent(DataFormats.UnicodeText)?data.GetData(DataFormats.UnicodeText) as string:data.GetData(DataFormats.Text) as string;
        if(string.IsNullOrWhiteSpace(text))
        {
            foreach(var format in new[]{"UniformResourceLocatorW","UniformResourceLocator"})
            {
                if(!data.GetDataPresent(format))continue;
                var value=data.GetData(format);
                if(value is string link)text=link;
                else if(value is Stream stream){if(stream.CanSeek)stream.Position=0;var bytes=new byte[16_385];int length=0,read;while(length<bytes.Length&&(read=stream.Read(bytes,length,bytes.Length-length))>0)length+=read;if(length>16_384)throw new FormatException("Слишком длинная ссылка.");text=(format.EndsWith('W')?System.Text.Encoding.Unicode:System.Text.Encoding.UTF8).GetString(bytes,0,length).TrimEnd('\0');}
                if(!string.IsNullOrWhiteSpace(text))break;
            }
        }
        return new(files.Take(8).ToArray(),(text??"").Length>120_000?throw new FormatException("Слишком большой текст."):text??"");
    }
    public static RouteRule Rule(string value)
    {
        value=value.Trim().Trim('"');
        if(File.Exists(value))
        {
            string extension=Path.GetExtension(value).ToLowerInvariant();
            if(extension==".lnk")
            {
                object? shell=null,shortcut=null;
                try{shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);shortcut=shell!.GetType().InvokeMember("CreateShortcut",System.Reflection.BindingFlags.InvokeMethod,null,shell,[value]);value=shortcut!.GetType().InvokeMember("TargetPath",System.Reflection.BindingFlags.GetProperty,null,shortcut,null)?.ToString()??"";}
                finally{if(shortcut!=null)Marshal.FinalReleaseComObject(shortcut);if(shell!=null)Marshal.FinalReleaseComObject(shell);}
            }
            else if(extension==".url")
            {
                if(new FileInfo(value).Length>16_384)throw new FormatException("Ярлык сайта слишком большой.");
                value=File.ReadLines(value).FirstOrDefault(l=>l.StartsWith("URL=",StringComparison.OrdinalIgnoreCase))?[4..]??"";
            }
            if(value.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)){var path=RouteRules.Normalize("process_path",value);return new(){Kind="process_path",Value=path,Name=Path.GetFileNameWithoutExtension(path),Route="Через VPN"};}
        }
        if(Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.Scheme is "http" or "https")
        {
            var kind=System.Net.IPAddress.TryParse(uri.Host,out _)?"ip_cidr":"domain_suffix";
            var normalized=RouteRules.Normalize(kind,kind=="ip_cidr"?uri.Host:value);return new(){Kind=kind,Value=normalized,Name=uri.Host,Route="Через VPN"};
        }
        throw new FormatException("Перетащи EXE, ярлык приложения или ссылку HTTP/HTTPS.");
    }
}
