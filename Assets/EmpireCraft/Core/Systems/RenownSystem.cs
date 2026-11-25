// Ported from the EmpireCraft mod: Scripts/GeneralSystems/RenownSystem.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{

// 名望年结(用户 2026-11-10 "民望系统校准下，因为民望还是照着原版来的""希望能更贴合新的模组系统"，
// 问答选"身份年入，原版压低""每年衰减一成")。名望在本模组里既是威望(推举次序、难动与否、量刑天平的影响力)，
// 又是影响力货币(推诉求、同化、请封、计谋要花)：
//   · 每年过生日先去一成(人走茶凉，至少 1)，再按身份记年入，名望稳在"年入 × 10"上下，反映眼下的分量；
//   · 年入：君主按本国城数(皇帝另按帝国城数与天命)；皇室(储君、近支、宗室)；官员按官品(一品最多，往下递减)；
//     爵位(公以上、侯伯与封号)；名将名臣；世家子弟按门第；
//   · 军团打赢一仗：统领与战争统帅按战功记名望(见 FamousTalentSystem.OnBattle)；
//   · 原版杀人、打仗胜负、城主逐年所得只留 VanillaShare(见 RenownPatch)。
// 花名望的开销(推诉求每年 50、请封 300……)不动
public static class RenownSystem
{
    public const float Decay = 0.1f;
    public const float VanillaShare = 0.2f;

    // 君主：基数 + 每城；皇帝另加帝国每城两点与天命的一半(按两份存档估，皇帝稳态中位八九百，明显高过一品大员的五百上下)
    public const int RulerBase = 20, RulerPerCity = 2, RulerMaxCities = 40;
    public const int EmperorPerCity = 2, EmperorMaxCities = 100, EmperorMandateDivisor = 2;
    // 皇室
    public const int RoyalHeir = 40, RoyalClose = 20, RoyalKin = 8;
    // 爵位：公以上；侯伯与封号
    public const int PeerHigh = 15, PeerLow = 6;
    // 名将名臣
    public const int Famous = 20;

    // 官品年入：tier 0(一品)~8(九品)，6 + 0.75 × (8 - tier)²：一品 54、三品 33、五品 18、七品 9、九品 6
    public static int OfficeIncome(int tier)
    {
        int steps = 8 - Math.Max(0, Math.Min(8, tier));
        return 6 + (int)(0.75f * steps * steps);
    }

    // 世家门第(一等最高)：一等 5 … 四等 2
    public static int GentryIncome(int grade) => Math.Max(0, 6 - Math.Max(1, grade));

    // 这人一年的名望年入；parts 给界面列出各项(本地化键、数目)
#if EC_PENDING
    public static int Income(Person actor, List<(string key, int value)> parts = null)
    {
        if (actor?.Record == null || actor.IsGone() || !actor.Alive || !actor.IsAdult() || actor.IsWarMachine()) return 0;
        int total = 0;
        void Add(string key, int value)
        {
            if (value <= 0) return;
            total += value;
            parts?.Add((key, value));
        }

        Realm kingdom = actor.Realm;
        bool hasKingdom = kingdom?.Record != null && !kingdom.IsGone();
        if (hasKingdom && kingdom.Ruler == actor)
        {
            Add("renown_income_ruler", RulerBase + RulerPerCity * Math.Min(RulerMaxCities, kingdom.Cities?.Count ?? 0));
            Empire empire = kingdom.IsEmpire() ? kingdom.GetEmpire() : null;
            if (empire?.data != null && !empire.IsGone() && empire.Emperor == actor)
                Add("renown_income_emperor", EmperorPerCity * Math.Min(EmperorMaxCities, empire.countCities()) +
                                             empire.data.Mandate / EmperorMandateDivisor);
        }
        if (hasKingdom)
        {
            switch (EmpireLawSystem.RoyalStanding(actor, kingdom))
            {
                case 3: Add("renown_income_heir", RoyalHeir); break;
                case 2: Add("renown_income_royal_close", RoyalClose); break;
                case 1: Add("renown_income_royal_kin", RoyalKin); break;
            }
            OfficialRank rank = OfficialRankSystem.Of(actor);
            if (rank.Shown) Add("renown_income_office", OfficeIncome(rank.Tier));
        }
        int peerage = (int)actor.GetPeeragesLevel();
        Add("renown_income_peerage", peerage >= (int)PeeragesLevel.peerages_3 && peerage < (int)PeeragesLevel.peerages_0 ? PeerHigh
            : peerage > (int)PeeragesLevel.peerages_6 && peerage < (int)PeeragesLevel.peerages_0 || actor.HasTitle() ? PeerLow : 0);
        if (FamousTalentSystem.OfActorId(actor.Key) != null) Add("renown_income_famous", Famous);
        SpecificClan clan = actor.GetSpecificClan();
        if (GentrySystem.IsGentry(clan)) Add("renown_income_gentry", GentryIncome(GentrySystem.Grade(GentrySystem.Pedigree(clan))));
        return total;
    }
#endif

    // 过生日(ActorPatch.UpdateAge)：先去一成，再记年入
#if EC_PENDING
    public static void Yearly(Person actor)
    {
        if (actor?.Record == null || actor.IsGone() || !actor.Alive || actor.IsWarMachine()) return;
        int renown = Math.Max(0, actor.Record.renown);
        int decay = renown > 0 ? Math.Max(1, (int)Math.Ceiling(renown * Decay)) : 0;
        actor.Record.renown = renown - decay + Income(actor);
    }
#endif

    // 打赢一仗：按战功记名望(统领满额、战争统帅一半，见 FamousTalentSystem.OnBattle)
#if EC_PENDING
    public static void OnBattle(Person actor, float fame)
    {
        if (actor?.Record == null || actor.IsGone() || !actor.Alive || fame <= 0f) return;
        actor.Record.renown += Mathf.RoundToInt(fame);
    }
#endif

    // 原版加的名望只留 VanillaShare(余数按机会进位，平均不差)；减名望的不管(见 RenownPatch)
#if EC_PENDING
    public static void ScaleVanilla(Person actor, int before)
    {
        if (actor?.Record == null) return;
        int gained = actor.Record.renown - before;
        if (gained <= 0) return;
        float kept = gained * VanillaShare;
        int whole = (int)kept;
        if (EcRandom.value < kept - whole) whole++;
        actor.Record.renown = before + whole;
    }
#endif
}
}
