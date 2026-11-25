// Ported from the EmpireCraft mod: Scripts/Enums/EmpireHeirLawType.cs @ 55d6d4f8. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Core
{
    public enum EmpireHeirLawType
    {
        eldest_child, //长嗣
        smallest_child, //幼子
        siblings, //直系同辈
        grand_child_generation, //直系孙辈
        random, //随机
        officer,
        none
    }
}
