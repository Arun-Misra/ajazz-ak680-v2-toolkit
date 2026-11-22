using System.Collections.Generic;

namespace AK680V2Toolkit;

/// <summary>
/// Shared key layout for the AK680 V2. LED ids are the `value` fields from the
/// device's own keyList. Row lengths: 15 + 15 + 14 + 14 + 10 = 68 physical keys.
/// </summary>
public static class KeyMap
{
    public static readonly string[][] Rows =
    {
        new[] { "Esc","1 !","2 @","3 #","4 $","5 %","6 ^","7 &","8 *","9 (","0 )","ˉ -","= +","Backspace","Home" },
        new[] { "Tab","Q","W","E","R","T","Y","U","I","O","P","[ {","] }","\\ |","Del" },
        new[] { "Caps","A","S","D","F","G","H","J","K","L","; :","' \"","Enter","PgUp" },
        new[] { "L-Shift","Z","X","C","V","B","N","M",", <",". >","/ ?","R-Shift","↑","PgDn" },
        new[] { "L-Ctrl","L-Win","L-Alt","Spacebar","R-Alt","R-Ctrl","Fn","←","↓","→" }
    };

    public static readonly Dictionary<string, int> IdByLabel = new()
    {
        ["Esc"]=0,["1 !"]=17,["2 @"]=18,["3 #"]=19,["4 $"]=20,["5 %"]=21,["6 ^"]=22,["7 &"]=23,["8 *"]=24,
        ["9 ("]=25,["0 )"]=26,["ˉ -"]=27,["= +"]=28,["Backspace"]=92,["Home"]=104,
        ["Tab"]=32,["Q"]=33,["W"]=34,["E"]=35,["R"]=36,["T"]=37,["Y"]=38,["U"]=39,["I"]=40,["O"]=41,
        ["P"]=42,["[ {"]=43,["] }"]=44,["\\ |"]=60,["Del"]=106,
        ["Caps"]=48,["A"]=49,["S"]=50,["D"]=51,["F"]=52,["G"]=53,["H"]=54,["J"]=55,["K"]=56,["L"]=57,
        ["; :"]=58,["' \""]=59,["Enter"]=76,["PgUp"]=105,
        ["L-Shift"]=64,["Z"]=65,["X"]=66,["C"]=67,["V"]=68,["B"]=69,["N"]=70,["M"]=71,[", <"]=72,
        [". >"]=73,["/ ?"]=74,["R-Shift"]=75,["↑"]=90,["PgDn"]=108,
        ["L-Ctrl"]=80,["L-Win"]=81,["L-Alt"]=82,["Spacebar"]=83,["R-Alt"]=84,["Fn"]=85,["R-Ctrl"]=87,
        ["←"]=88,["↓"]=89,["→"]=91
    };

    public static int Id(string label) => IdByLabel.TryGetValue(label, out int id) ? id : -1;
}
