using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RelayDesk.Orbit;
public sealed record Place(string Code,string Country,double Latitude,double Longitude,string[] Aliases);
public static class Locations
{
    public static readonly Place[] Countries=[
        new("LT","Литва",55.2,23.9,["lithuania","литв","vilnius"]),
        new("SE","Швеция",62,15,["sweden","швеци","stockholm"]),new("NL","Нидерланды",52.2,5.3,["netherlands","нидерланд","holland","amsterdam"]),new("DE","Германия",51,10,["germany","германи","frankfurt","berlin"]),new("FI","Финляндия",64,26,["finland","финлянд","helsinki"]),new("PL","Польша",52,19,["poland","польш","warsaw"]),new("FR","Франция",47,2,["france","франци","paris"]),new("GB","Великобритания",54,-2,["united kingdom","great britain","великобрит","london","england"]),new("US","США",39,-98,["united states","usa","сша","new york","america"]),new("CA","Канада",57,-106,["canada","канад","toronto"]),new("JP","Япония",37,138,["japan","япони","tokyo"]),new("SG","Сингапур",1.35,103.8,["singapore","сингапур"]),new("TR","Турция",39,35,["turkey","türkiye","турци","istanbul"]),new("RU","Россия",57,70,["russia","росси","moscow"]),new("AU","Австралия",-25,134,["australia","австрали","sydney"]),new("CH","Швейцария",47,8,["switzerland","швейцар","zurich"]),new("EE","Эстония",59,26,["estonia","эстони","tallinn"]),new("KR","Южная Корея",36,128,["korea","коре","seoul"]),new("BR","Бразилия",-14,-52,["brazil","бразил"])
    ];
    public static Place? Detect(string name)
    {
        var runes=name.EnumerateRunes().ToArray();
        for(int i=0;i<runes.Length-1;i++)if(runes[i].Value is >=0x1F1E6 and <=0x1F1FF&&runes[i+1].Value is >=0x1F1E6 and <=0x1F1FF){string code=""+(char)('A'+runes[i].Value-0x1F1E6)+(char)('A'+runes[i+1].Value-0x1F1E6);var found=Countries.FirstOrDefault(c=>c.Code==code);if(found!=null)return found;}
        foreach(var country in Countries)if(country.Aliases.Any(a=>name.Contains(a,StringComparison.OrdinalIgnoreCase))||Regex.IsMatch(name,@"(?<![A-Za-z])"+country.Code+@"(?![A-Za-z])",RegexOptions.IgnoreCase))return country;
        return null;
    }
}
