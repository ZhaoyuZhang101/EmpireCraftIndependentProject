// Ported from the EmpireCraft mod: Scripts/HelperFunc/YearNameHelper.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Core
{
public static class YearNameHelper
{
#if EC_PENDING
    public static string generateName()
    {
        string CharacterSetPath = Path.Combine(GameData.Folder, "Locales", "Cultures", "YearName1.csv");
        string CharacterSet2Path = Path.Combine(GameData.Folder, "Locales", "Cultures", "YearName2.csv");
        List<string> names1 = OnomasticsHelper.getKeysFromPath(CharacterSetPath);
        List<string> names2 = OnomasticsHelper.getKeysFromPath(CharacterSet2Path);
        // 两个字不重(以前会出"华华"这样的叠字年号)
        string first = LM.Get(names1.GetRandom()), second = LM.Get(names2.GetRandom());
        for (int tries = 0; tries < 8 && second == first; tries++) second = LM.Get(names2.GetRandom());
        return first + second;
    }
#endif
}
}
