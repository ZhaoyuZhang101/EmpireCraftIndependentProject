// Ported from the EmpireCraft mod: Scripts/GeneralSystems/FeudalPosthumousSystem.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{

// 封国国君的谥号(仿春秋诸侯：国名 + 谥 + 爵，如湘文侯、湘桓公、梁孝王)：
//   帝国内世袭封国(周制、律令制等华夏政体)的国君去世时，按其一生定一个单字谥，
//   用字与帝王谥法同一套(rule_shihao_*)，同一封国内尽量不重复；实在重了加“后”(湘后文侯，仿卫后庄公)；
//   记入人物档案、个人史与事件日志。
//   谥号不补“国”字(魏文侯、齐桓公)；共和与现代政体不授谥。
public static class FeudalPosthumousSystem
{
#if EC_PENDING
    public static void OnRulerDied(Person ruler, Realm realm, AttackType cause)
    {
        if (ruler?.Record == null || realm?.Record == null || realm.IsGone() || realm.Ruler != ruler) return;
        try
        {
            Empire empire = realm.GetEmpire();
            if (empire == null || empire.IsGone() || realm.IsEmpire() || !empire.AllowsPosthumousNames()) return;
            if (string.IsNullOrEmpty(ActorExtension.HereditaryRealmRank(ruler))) return;
            OfficeObject office = realm.GetOffice();
            if (office == null || office.regimeType != RegimeType.ZhouFeudalism && office.regimeType != RegimeType.LvLing) return;
            string rank = LM.Get(DynastyNaming.RankKey(office.regimeType, office.officeType, realm));
            string realmName = realm.GetKingdomName();
            if (string.IsNullOrWhiteSpace(rank) || string.IsNullOrWhiteSpace(realmName)) return;

            var realmData = realm.GetOrCreate();
            var used = new HashSet<string>(realmData.posthumous_used ??= new List<string>());
            int years = realm.Record.timestamp_king_rule >= 0d ? GameClock.YearsSince(realm.Record.timestamp_king_rule) : -1;
            bool killed = cause is AttackType.Weapon or AttackType.Explosion or AttackType.Poison;
            int cities = realm.Cities?.Count ?? 0;
            var decision = PosthumousNameGenerator.DecideVassal(years, ruler.Age(), killed, cities, used);
            string shi = LM.Get(decision.shi);
            if (string.IsNullOrWhiteSpace(shi)) return;
            // 本国先君已用过这个谥：加“后”以示区别(卫后庄公、秦后昭公)
            bool repeated = realmData.posthumous_used.Contains(shi);
            realmData.posthumous_used.Add(shi);
            string title = realmName + (repeated ? LM.Get("posthumous_later_prefix") : "") + shi + rank;

            EmpireCraft.Core.PersonalClanIdentity identity = ruler.GetPersonalIdentity();
            if (identity != null)
            {
                identity.posthumous_name = title;
                identity.RecordPersonalHistory(string.Format(LM.Get("feudal_posthumous_history"), title, LM.Get(decision.reason)));
            }
            TranslateHelper.LogEventMessage(string.Format(LM.Get("feudal_posthumous_log"),
                ruler.FullName(), title, LM.Get(decision.reason)), realm);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][谥号] 封国国君定谥失败: {exception.Message}");
        }
    }
#endif
}
}
