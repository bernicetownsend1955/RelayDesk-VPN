using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;
using FormatException=System.FormatException;

namespace RelayDesk;
public sealed class SharedNetworkSettings
{
    public ConnectionSettings? Connection {get;set;}
    public List<QuickRoute> Rules {get;set;}=[];
    public string Fallback {get;set;}="source";
    public string Preset {get;set;}="rules";
}
public sealed class QrShare
{
    public int Version {get;set;}=1;
    public Profile? Profile {get;set;}
    public SharedNetworkSettings? Settings {get;set;}
    public static QrShare Create(Profile? profile,Preferences settings,bool includeSettings)
    {
        Profile? copy=profile==null?null:SharedRouting.Copy(profile);
        if(copy!=null){copy.Id=Guid.NewGuid().ToString("N");copy.Executable="";copy.SubscriptionId="";copy.SubscriptionGroup="";copy.QuickRoutes=[];}
        return new(){Profile=copy,Settings=includeSettings?new(){Connection=settings.Connection,Rules=SharedRouting.Normalize(settings.SharedQuickRoutes??[]),Fallback=settings.FallbackRoute,Preset=settings.RoutingPreset}:null};
    }
    public string Encode()=>"relaydesk-share-v1:"+Convert.ToBase64String(Compress(JsonSerializer.SerializeToUtf8Bytes(this)));
    static byte[] Compress(byte[] data){using var output=new MemoryStream();using(var zip=new BrotliStream(output,CompressionLevel.SmallestSize,true))zip.Write(data);return output.ToArray();}
    public static QrShare Decode(string text)
    {
        const string prefix="relaydesk-share-v1:";
        if(!text.StartsWith(prefix,StringComparison.Ordinal)||text.Length>100_000)throw new FormatException("Некорректный пакет RelayDesk.");
        using var input=new MemoryStream(Convert.FromBase64String(text[prefix.Length..]));using var zip=new BrotliStream(input,CompressionMode.Decompress);using var output=new MemoryStream();
        var buffer=new byte[8192];int count;while((count=zip.Read(buffer))>0){if(output.Length+count>2_000_000)throw new FormatException("Пакет больше 2 МБ.");output.Write(buffer,0,count);}
        var result=JsonSerializer.Deserialize<QrShare>(output.ToArray())??throw new FormatException("Пакет пуст.");
        if(result.Version!=1||result.Profile==null&&result.Settings==null)throw new FormatException("Версия или содержимое пакета не поддерживается.");return result;
    }
}
public sealed class QrParts
{
    readonly Dictionary<string,(DateTime Created,int Count,Dictionary<int,string> Parts)> pending=[];
    public static string[] Split(string text)
    {
        if(Encoding.UTF8.GetByteCount(text)<=1000)return [text];
        var encoded=Convert.ToBase64String(Encoding.UTF8.GetBytes(text));int count=(encoded.Length+899)/900;
        if(count>128)throw new FormatException("Данные не помещаются в 128 QR-кодов. Экспортируй конфигурацию файлом.");
        string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(encoded))).ToLowerInvariant();
        return Enumerable.Range(0,count).Select(i=>$"relaydesk-qr-v1:{hash}:{i+1}:{count}:"+encoded.Substring(i*900,Math.Min(900,encoded.Length-i*900))).ToArray();
    }
    public (string? Text,int Read,int Total) Accept(string text)
    {
        if(text.Length>120_000)throw new FormatException("QR-код содержит слишком много данных.");
        if(!text.StartsWith("relaydesk-qr-v1:",StringComparison.Ordinal))return(text,1,1);
        var fields=text.Split(':',5);
        if(fields.Length!=5||fields[1].Length!=64||!fields[1].All(char.IsAsciiHexDigit)||!int.TryParse(fields[2],out int index)||!int.TryParse(fields[3],out int total)||total is <2 or >128||index<1||index>total||fields[4].Length>900||!fields[4].All(c=>char.IsAsciiLetterOrDigit(c)||c is '+' or '/' or '='))throw new FormatException("Повреждённая часть QR-пакета.");
        foreach(var key in pending.Where(p=>DateTime.UtcNow-p.Value.Created>TimeSpan.FromMinutes(15)).Select(p=>p.Key).ToArray())pending.Remove(key);
        if(!pending.TryGetValue(fields[1],out var group)){if(pending.Count>=4)throw new FormatException("Сначала закончи чтение уже начатого пакета.");group=(DateTime.UtcNow,total,new());pending.Add(fields[1],group);}
        if(group.Count!=total||group.Parts.TryGetValue(index,out var duplicate)&&duplicate!=fields[4])throw new FormatException("Части QR-пакета противоречат друг другу.");
        group.Parts[index]=fields[4];pending[fields[1]]=(DateTime.UtcNow,total,group.Parts);if(group.Parts.Count<total)return(null,group.Parts.Count,total);
        var payload=string.Concat(Enumerable.Range(1,total).Select(i=>group.Parts[i]));pending.Remove(fields[1]);
        if(!Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).Equals(fields[1],StringComparison.OrdinalIgnoreCase))throw new FormatException("Контрольная сумма QR-пакета не совпала.");
        return(new UTF8Encoding(false,true).GetString(Convert.FromBase64String(payload)),total,total);
    }
}
public static class QrImages
{
    public static BitmapSource PrepareForDecode(BitmapSource image){if(image.IsFrozen)return image;var copy=image.CloneCurrentValue();copy.Freeze();return copy;}
    public static BitmapSource Encode(string text)
    {
        var matrix=new ZXing.QrCode.QRCodeWriter().encode(text,BarcodeFormat.QR_CODE,0,0,new Dictionary<EncodeHintType,object>{{EncodeHintType.CHARACTER_SET,"UTF-8"},{EncodeHintType.MARGIN,4},{EncodeHintType.ERROR_CORRECTION,ZXing.QrCode.Internal.ErrorCorrectionLevel.M}});
        int scale=Math.Max(2,512/matrix.Width),size=matrix.Width*scale;var pixels=new byte[size*size*4];
        for(int y=0;y<size;y++)for(int x=0;x<size;x++){int at=(y*size+x)*4;byte value=matrix[x/scale,y/scale]?(byte)0:(byte)255;pixels[at]=pixels[at+1]=pixels[at+2]=value;pixels[at+3]=255;}
        var image=BitmapSource.Create(size,size,96,96,PixelFormats.Bgra32,null,pixels,size*4);image.Freeze();return image;
    }
    public static string Decode(BitmapSource image)
    {
        if((long)image.PixelWidth*image.PixelHeight>40_000_000)throw new FormatException("Изображение слишком большое. Выбери область с QR-кодом.");
        var converted=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);int stride=converted.PixelWidth*4;var bytes=new byte[stride*converted.PixelHeight];converted.CopyPixels(bytes,stride,0);
        var reader=new BarcodeReaderGeneric{AutoRotate=true,Options=new DecodingOptions{TryHarder=true,TryInverted=true,PossibleFormats=[BarcodeFormat.QR_CODE]}};
        var result=reader.Decode(new RGBLuminanceSource(bytes,converted.PixelWidth,converted.PixelHeight,RGBLuminanceSource.BitmapFormat.BGRA32));
        return result?.Text??throw new FormatException("QR-код не найден. Выбери изображение или область с одним QR-кодом.");
    }
    public static BitmapSource Load(string path)
    {
        if(new FileInfo(path).Length>20_000_000)throw new FormatException("Изображение больше 20 МБ.");
        using var file=File.OpenRead(path);var decoder=BitmapDecoder.Create(file,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);var image=decoder.Frames[0];image.Freeze();return image;
    }
    public static void Save(BitmapSource image,string path){var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(path);png.Save(file);}
}
