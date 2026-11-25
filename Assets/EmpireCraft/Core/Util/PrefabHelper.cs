// Ported from the EmpireCraft mod: Scripts/HelperFunc/PrefabHelper.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Core
{
    public static class PrefabHelper
    {
#if EC_PENDING
        public static GameObject FindPrefabByName(string name)
        {
            // 这会返回所有已经加载到内存的 GameObject 资源
            var all = Resources.FindObjectsOfTypeAll<GameObject>();
            // 按名字匹配
            return all.FirstOrDefault(prefab => prefab.name == name);
        }
#endif
    }
}
