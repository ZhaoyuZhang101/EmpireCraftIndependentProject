// Ported from the EmpireCraft mod: Scripts/GeneralSystems/WarChronicle.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 战报(示意图 ④)：一场仗里的宣战、参战退出、攻城失城、统帅阵亡、兵败如山倒、目标达成、收场，记进这场战争(存进存档)，
// 每场至多 Max 条，满了丢最早的(宣战那条留着)。战争窗口按类筛：攻城、统帅、外交。
// 伤亡(示意图 ①)：军团制下原版只记实体单位的阵亡，军团的战损记进各场仗(见 CorpsSystem.Casualties)；
// 一国同时打几场仗时按场平分。
public static class WarChronicle
{
    public const int Declare = 0, City = 1, Commander = 2, Collapse = 3, Diplomacy = 4, Goal = 5, End = 6;
    public const int Max = 80;

#if EC_PENDING
    public static void Record(War war, int kind, string text)
    {
        if (war?.data == null || string.IsNullOrWhiteSpace(text)) return;
        List<WarChronicleEntry> list = war.GetOrCreate().chronicle ??= new List<WarChronicleEntry>();
        list.Add(new WarChronicleEntry { time = EmpireWorld.Current?.Now() ?? 0d, kind = kind, text = text });
        if (list.Count > Max) list.RemoveAt(list.Count > 1 && list[0].kind == Declare ? 1 : 0);
    }
#endif

    // a、b 两国在对立两边的进行中的仗都记一条
#if EC_PENDING
    public static void Record(Realm a, Realm b, int kind, string text)
    {
        if (a?.Record == null || b?.Record == null || a == b) return;
        foreach (War war in Between(a, b).ToList()) Record(war, kind, text);
    }
#endif

    // a 参与的、对面有 b 的进行中的仗
#if EC_PENDING
    public static IEnumerable<War> Between(Realm a, Realm b) =>
        (a.getWars() ?? Enumerable.Empty<War>()).Where(war => war?.data != null && !war.hasEnded() &&
                                                             (war.isAttacker(a) && war.isDefender(b) ||
                                                              war.isDefender(a) && war.isAttacker(b)));
#endif

    // 宣战(开战定目标之后，见 WarGoalSystem.Ensure；没有目标的仗见 WarPatch.update)：只记一次，排在最前
    // (盟国、附庸开战当时就加入了，"加入攻方"会先记下)
#if EC_PENDING
    public static void Declared(War war)
    {
        if (war?.data == null) return;
        WarExtension.WarExtraData data = war.GetOrCreate();
        if (data.declared) return;
        data.declared = true;
        if (war.main_attacker?.data == null || war.main_defender?.data == null) return;
        // 没有开战目标的仗(叛乱、独立、争位……)：写类型
        string text;
        if (WarGoalSystem.Get(war) == EmpireCraft.Core.WarGoal.None)
            text = string.Format(LM.Get("war_log_declared_type"), war.main_attacker.GetKingdomName(),
                war.main_defender.GetKingdomName(), WarCatalog.Kind(war));
        else
        {
            string tags = string.Join(LM.Get("list_separator"), WarCatalog.Tags(war));
            text = string.Format(LM.Get(string.IsNullOrEmpty(tags) ? "war_log_declared" : "war_log_declared_tagged"),
                war.main_attacker.GetKingdomName(), war.main_defender.GetKingdomName(), WarCatalog.GoalText(war), tags);
        }
        List<WarChronicleEntry> list = data.chronicle ??= new List<WarChronicleEntry>();
        list.Insert(0, new WarChronicleEntry { time = war.data.created_time, kind = Declare, text = text });
    }

    public static void Joined(War war, Realm kingdom)
    {
        if (war?.data == null || kingdom?.Record == null || kingdom == war.main_attacker || kingdom == war.main_defender) return;
        Record(war, Diplomacy, string.Format(LM.Get(war.isAttacker(kingdom) ? "war_log_joined_attackers" : "war_log_joined_defenders"),
            kingdom.GetKingdomName()));
    }

    public static void Left(War war, Realm kingdom)
    {
        if (war?.data == null || war.hasEnded() || kingdom?.Record == null) return;
        Record(war, Diplomacy, string.Format(LM.Get("war_log_left"), kingdom.GetKingdomName()));
    }
#endif

    // 战败退出：灭了国的"亡于此战"，还在的(攻下都城认输、称臣)"认输"
#if EC_PENDING
    public static void Fallen(War war, Realm kingdom)
    {
        if (war?.data == null || kingdom?.Record == null) return;
        Record(war, Diplomacy, string.Format(LM.Get(kingdom.Alive && !kingdom.IsGone() ? "war_log_surrendered" : "war_log_fallen"),
            kingdom.GetKingdomName()));
    }
#endif

    // 攻下一座城：新主人与旧主人对立的仗都记
#if EC_PENDING
    public static void CityTaken(City city, Realm from, Realm to)
    {
        if (city?.Record == null || from?.Record == null || to?.Record == null) return;
        Record(to, from, City, string.Format(LM.Get("war_log_city_taken"), to.GetKingdomName(), city.GetCityName(),
            from.GetKingdomName()));
    }
#endif

    // 一国参与的进行中的仗都记(统帅阵亡、兵败如山倒)
#if EC_PENDING
    public static void RecordFor(Realm kingdom, int kind, string text)
    {
        if (kingdom?.Record == null) return;
        foreach (War war in (kingdom.getWars() ?? Enumerable.Empty<War>()).Where(war => war?.data != null && !war.hasEnded()).ToList())
            Record(war, kind, text);
    }

    public static void Ended(War war, WarWinner winner)
    {
        if (war?.data == null) return;
        string key = winner switch
        {
            WarWinner.Attackers => "war_log_end_attackers",
            WarWinner.Defenders => "war_log_end_defenders",
            _ => "war_log_end_peace"
        };
        Record(war, End, string.Format(LM.Get(key), war.main_attacker?.GetKingdomName() ?? "",
            war.main_defender?.GetKingdomName() ?? ""));
    }
#endif

    // 军团战损(CorpsSystem.Casualties)：记进这国参与的各场仗，按场平分
#if EC_PENDING
    public static void Casualties(long kingdomId, float dead)
    {
        if (dead <= 0f || kingdomId < 0 || EmpireWorld.Current?.LiveRealms == null) return;
        Realm kingdom = EmpireWorld.Current.RealmByKey(kingdomId);
        if (kingdom?.Record == null || kingdom.IsGone()) return;
        List<War> wars = (kingdom.getWars() ?? Enumerable.Empty<War>()).Where(war => war?.data != null && !war.hasEnded()).ToList();
        if (wars.Count == 0) return;
        float share = dead / wars.Count;
        foreach (War war in wars)
        {
            WarExtension.WarExtraData data = war.GetOrCreate();
            if (war.isAttacker(kingdom)) data.attacker_losses += share;
            else data.defender_losses += share;
        }
    }
#endif

    // 一方的总伤亡：原版记的实体阵亡 + 军团战损
#if EC_PENDING
    public static float Losses(War war, bool attackers)
    {
        if (war?.data == null) return 0f;
        WarExtension.WarExtraData data = war.GetOrCreate();
        return attackers ? war.getDeadAttackers() + data.attacker_losses : war.getDeadDefenders() + data.defender_losses;
    }
#endif
}
}
