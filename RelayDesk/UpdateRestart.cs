using System.IO;
using System.Text.Json;

namespace RelayDesk;

public sealed record UpdateResume
{
    public string Token {get;set;}="";
    public DateTime CreatedUtc {get;set;}=DateTime.UtcNow;
    public bool Vpn {get;set;}
    public bool Zapret {get;set;}
    public string ProfileId {get;set;}="";
    public string SelectedProfileId {get;set;}="";
    public int Page {get;set;}
    public int Section {get;set;}=-1;
    public bool Hidden {get;set;}
    public bool Minimized {get;set;}
    public Dictionary<string,string> Draft {get;set;}=new();
}

public static class UpdateRestart
{
    public static string PathIn(string folder)=>Path.Combine(folder,"update-resume.json");
    public static void Save(string folder,UpdateResume state)
    {
        if(!Guid.TryParseExact(state.Token,"N",out _))throw new InvalidDataException("Некорректный запрос перезапуска.");
        Directory.CreateDirectory(folder);var path=PathIn(folder);
        File.WriteAllText(path+".tmp",JsonSerializer.Serialize(state,JsonTools.Options));File.Move(path+".tmp",path,true);
    }
    public static string PendingToken(string folder)
    {
        if(!File.Exists(PathIn(folder)))return "";
        var state=JsonSerializer.Deserialize<UpdateResume>(File.ReadAllText(PathIn(folder)));
        return state!=null&&DateTime.UtcNow-state.CreatedUtc<TimeSpan.FromMinutes(30)?state.Token:"";
    }
    public static UpdateResume? Take(string folder,string token)
    {
        if(!Guid.TryParseExact(token,"N",out _)||!File.Exists(PathIn(folder)))return null;
        var state=JsonSerializer.Deserialize<UpdateResume>(File.ReadAllText(PathIn(folder)));
        if(state==null||state.Token!=token||state.CreatedUtc>DateTime.UtcNow.AddMinutes(1)||DateTime.UtcNow-state.CreatedUtc>TimeSpan.FromMinutes(30))return null;
        File.Move(PathIn(folder),PathIn(folder)+".consumed",true);
        return state;
    }
}
