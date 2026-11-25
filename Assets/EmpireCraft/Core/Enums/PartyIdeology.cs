// Ported from the EmpireCraft mod: Scripts/GeneralSystems/PartySystem.cs#PartyIdeology @ 55d6d4f8. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{

// 政党意识形态，按政治光谱(经济 左—右 × 权力 自由—极权)排布
public enum PartyIdeology
{
    Anarchism,              // 无政府主义        左·自由
    SocialDemocracy,        // 社会民主主义      中左·自由
    Libertarianism,         // 自由意志主义      中·极自由
    SocialLiberalism,       // 社会自由主义      中·自由
    Capitalism,             // 资本主义          右·自由
    ConservativeLiberalism, // 保守自由主义      中右·自由
    Centrism,               // 中间派
    Socialism,              // 社会主义          中左·威权
    Communism,              // 共产主义          极左·极权
    ReligiousDemocracy,     // 教派民主主义      中·威权
    Authoritarianism,       // 权威主义          中·极权
    Conservatism,           // 保守主义          中右·威权
    Fascism,                // 法西斯主义        极右·极权
    // ---- 古代理念(用户 2026-10-16："古代理念也要改革不能全是保守，需要细分……还有诸子百家等也是理念")；追加在末尾 ----
    // 诸子百家(只有华夏有，按科技陆续出现，见 TechTree.json 的 ideology:<理念>)
    Confucianism,           // 儒家      官僚、地主
    Legalism,               // 法家      官僚、军人
    HuangLao,               // 黄老      农民、国人
    Mohism,                 // 墨家      工匠、国人
    Agrarianism,            // 农家      农民、野人
    Strategists,            // 兵家      军人
    Diplomatists,           // 纵横家    商人、士
    YinYang,                // 阴阳家    巫祝(有了礼制，由巫祝之术分出)
    // 政治立场(皇权、门阀各文化都有；外戚、宦官只在朝堂里，不在民间流传)
    ImperialPower,          // 皇权      帝党
    GreatClans,             // 门阀      世族
    ConsortKin,             // 外戚      后党
    Eunuchs,                // 宦官      阉党
    // 泛灵论(玩家建议："搞个最早的理念，比如泛灵论、原始宗教之类的，然后后面再开始根据各自的文化进行分支")：
    // 各文化有文字以前人人信的上古信仰，有了文字就分化为本文化的学说、教门
    Animism,                // 泛灵论    农民、奴隶
    // 宗亲(玩家建议"宦官天然是天子派……宗室从天子派里拆出来"，用户 2026-10-17 定)：只在朝堂里，朝中为宗室派
    RoyalKin,               // 宗亲      宗室派
    // 军功(用户 2026-10-20 选"常设军功派")：统兵的将领、武职官员，只在朝堂里，朝中为军功派(律令制模板的武官派即是)
    MilitaryMerit           // 军功      军功派
}
}
