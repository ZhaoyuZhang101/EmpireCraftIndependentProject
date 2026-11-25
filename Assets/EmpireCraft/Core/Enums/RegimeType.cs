// Ported from the EmpireCraft mod: Scripts/Regimes/RegimeSystem.cs#RegimeType @ 55d6d4f8. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{

[Flags]
public enum RegimeType
{
    LvLing,        //律令      - 唐
    Feudalism,     //封建      - 神罗
    ZhouFeudalism, //分封      - 周
    Modern,      //共和      - 现代美国
    Arabic,        //阿拉伯政体 - 阿拉伯世界
    YouMu,         //游牧政体   - 蒙古汗国
    Republic,
    Origin,
    ClassicalRepublic, //古典城邦共和；追加在末尾以保持旧存档枚举值稳定
    Bakufu, //幕府：天皇为虚君、征夷大将军掌实权、大名世袭各藩(日本)
    HuaxiaOrigin, //华夏原始政体：诸部议事、推举共主、禅让传贤(见 ShanrangSystem)，世袭王权以后家天下；追加在末尾
    NomadFeudalism, // 本族定居后由部族领主分土世袭；不附带西方教廷、选帝侯与神罗制度
    Transmigrant, // 穿越政体：元老院为最高权力、执委会主席由元老院选出，无君主天命(见 TransmigrantSystem)；追加在末尾
    Colonial, // 殖民地政府：宗主国任免总督的海外附属政权(见 ColonySystem)；追加在末尾
    Company, // 特许公司：金融以后宗主国特许设立，董事会选总督、利润分红给宗主国(见 ColonySystem)；追加在末尾
    OriginFeudalism,
}
}
