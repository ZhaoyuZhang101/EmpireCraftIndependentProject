// Ported from the EmpireCraft mod: Scripts/Regimes/FixedFaction.cs#FactionType @ 55d6d4f8. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{

public enum FactionType
{
    原始,
    尊王,  //王室为主
    诸侯,  //诸侯为主
    中央,  //西方王室
    自治,  //西方自治
    攘夷,  //主战
    绥靖,  //主和
    血脉, //国王为单一血脉
    僭主, //霸者为王
    神权, //宗教至上
    共和,  //反移民，发展自己
    民主,  //移民，吸血国外
    革命,  //一党，革命输出
    融入,  //融入帝国中最大的文明
    同化,  //同化掉其他地区的文明
    共产,  //社会主义
    无,
    外戚,  //后党(朋党，见 CliqueSystem)；追加在末尾
    宦官   //阉党
}
}
