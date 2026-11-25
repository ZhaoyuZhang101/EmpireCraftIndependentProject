// Ported from the EmpireCraft mod: Scripts/Enums/SuccessionLawType.cs @ 55d6d4f8. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Core
{
    public enum SuccessionLawType
    {
        嫡长子继承法, //嫡妻所出长子优先继承
        幼子继承法, //幼子继承
        兄终弟及, //兄长身故后由弟弟继承
        分割继承法, //诸子分封,领地被拆分给多个继承人
        强者继承法 //宗室内实力最强者继承
    }
}
