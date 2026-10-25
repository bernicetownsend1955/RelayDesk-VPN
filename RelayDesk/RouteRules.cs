using System.Globalization;
using System.IO;
using System.Net;

namespace RelayDesk;

public sealed class RouteRule
{
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string Kind {get;set;}="process_path";
    public string Name {get;set;}="";
    public string Value {get;set;}="";
    public string Route {get;set;}="Автоматически";
    public string QuickKey {get;set;}="";
    public bool AutoResolve {get;set;}
    public string OutboundTag {get;set;}="";
    public RouteRule Copy()=>new(){Id=Id,Kind=Kind,Name=Name,Value=Value,Route=Route,QuickKey=QuickKey,AutoResolve=AutoResolve,OutboundTag=OutboundTag};
}
public static class RouteRules
{
    public static readonly string[] Kinds=["process_path","process_name","domain","domain_suffix","domain_keyword","ip_cidr"];
    public static readonly string[] Routes=["Через VPN","Через zapret","Напрямую"];
    public static string Label(string kind)=>kind switch{"process_path"=>"Приложение / EXE","process_name"=>"Имя процесса","browser_default"=>"Браузер по умолчанию","domain"=>"Точный домен","domain_suffix"=>"Домен и поддомены","domain_keyword"=>"Слово в домене","ip_cidr"=>"IP / подсеть","default"=>"Остальной трафик",_=>kind};
    public static string Example(string kind)=>kind switch
    {
        "process_path"=>"Выбери приложение из Windows или укажи полный путь к EXE.",
        "process_name"=>"Например: Discord.exe. Подходит для всех процессов с таким именем, независимо от папки.",
        "domain"=>"Например: discord.com. Совпадёт только discord.com; cdn.discord.com не попадёт в правило.",
        "domain_suffix"=>"Например: discord.com. Совпадут discord.com, cdn.discord.com и другие его поддомены. Можно вставить *.discord.com.",
        "domain_keyword"=>"Например: discord. Совпадёт любое имя с этим словом, включая mydiscord.example. Используй осторожно.",
        "ip_cidr"=>"Например: 1.1.1.1, 192.168.1.0/24 или 2001:db8::/32. IP без маски означает один адрес.",
        _=>""
    };
    public static string Normalize(string kind,string input)
    {
        string value=input.Trim();
        if(value.Length==0)throw new InvalidDataException("Введи значение правила.");
        if(kind=="process_path")
        {
            value=Environment.ExpandEnvironmentVariables(value.Trim('"'));
            if(!Path.IsPathFullyQualified(value)||!value.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||!File.Exists(value))throw new InvalidDataException("Выбери существующий EXE приложения, указав полный путь.");
            return Path.GetFullPath(value);
        }
        if(kind=="process_name")
        {
            if(value.IndexOfAny(['\\','/',':','*','?'])>=0||!value.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Укажи имя без пути, например Discord.exe.");
            return value;
        }
        if(kind=="domain_keyword")
        {
            if(value.Any(char.IsWhiteSpace)||value.IndexOfAny(['/',':','*','?'])>=0)throw new InvalidDataException("Введи слово без пробелов, URL и масок.");
            return value.ToLowerInvariant();
        }
        if(kind=="domain"||kind=="domain_suffix")
        {
            if(value.StartsWith("http://",StringComparison.OrdinalIgnoreCase)||value.StartsWith("https://",StringComparison.OrdinalIgnoreCase))
            {
                if(!Uri.TryCreate(value,UriKind.Absolute,out var url)||url.UserInfo.Length>0)throw new InvalidDataException("Не удалось прочитать адрес сайта.");
                value=url.Host;
            }
            if(kind=="domain_suffix")value=value.StartsWith("*.")?value[2..]:value.TrimStart('.');
            value=value.TrimEnd('.');
            try{value=new IdnMapping().GetAscii(value).ToLowerInvariant();}catch(ArgumentException){throw new InvalidDataException("Некорректное доменное имя.");}
            if(IPAddress.TryParse(value,out _)||Uri.CheckHostName(value)!=UriHostNameType.Dns||!value.Contains('.')||value.Length>253||value.Split('.').Any(s=>s.Length is 0 or >63||s.StartsWith('-')||s.EndsWith('-')||s.Any(c=>!(char.IsAsciiLetterOrDigit(c)||c=='-'))))throw new InvalidDataException("Введи домен вроде example.com. IP-адреса добавляй типом «IP / подсеть».");
            return value;
        }
        if(kind=="ip_cidr")
        {
            var parts=value.Split('/');
            if(parts.Length>2||parts[0].Contains('%')||!IPAddress.TryParse(parts[0],out var address))throw new InvalidDataException("Некорректный IP-адрес.");
            var bytes=address.GetAddressBytes();int bits=bytes.Length*8,prefix=bits;
            if(bytes.Length==4&&(parts[0].Split('.').Length!=4||parts[0].Split('.').Any(p=>!p.All(char.IsAsciiDigit)||p.Length>1&&p.StartsWith('0'))))throw new InvalidDataException("Для IPv4 укажи четыре десятичных числа, например 1.1.1.1.");
            if(parts.Length==2&&(!int.TryParse(parts[1],out prefix)||prefix<0||prefix>bits))throw new InvalidDataException("Маска должна быть от 0 до "+bits+".");
            for(int i=0;i<bytes.Length;i++){int keep=Math.Clamp(prefix-i*8,0,8);bytes[i]&=(byte)(255<<(8-keep));}
            return new IPAddress(bytes)+"/"+prefix;
        }
        throw new InvalidDataException("Неизвестный тип правила.");
    }
}
