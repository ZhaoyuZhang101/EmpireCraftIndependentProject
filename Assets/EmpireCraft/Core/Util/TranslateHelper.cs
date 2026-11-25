// Generated from the EmpireCraft mod: Scripts/HelperFunc/TranslateHelper.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Color = EmpireCraft.Core.EcColor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Core
{
    public static class TranslateHelper
    {
        public static string GetPeerageTranslate(PeeragesLevel pl)
        {
            return LM.Get("default_" + pl.ToString());
        }
        public static void LogNewEmpire(Empire empire)
        {
            new News(NewsKinds.become_new_empire_log, empire.CoreKingdom.Ruler.Name, empire.CoreKingdom.Name)
            {
                Color1 = empire.CoreKingdom.GetColor().GetColorText(),
                Color2 = empire.CoreKingdom.GetColor().GetColorText(),
            }.Add();
        }
        /// <summary>
        /// 检索所有输入字段的翻译并且拼接在一起
        /// </summary>
        /// <param name="args"></param>
        /// <returns>翻译完成并拼接的字段</returns>
        public static string TryTranslate(params string[] args)
        {
            string result = "";
            foreach (var text in args)
            {
                if (text == " " || text == ModClass.NARROW_SPACE)
                {
                    result += text;
                }
                else
                {
                    result += LM.Get(text);
                }
            }
            return result;
        }
        public static void LogministerTryAqcuireEmpire(Person minister, Empire empire)
        {
            new News(NewsKinds.minister_try_aqcuire_empire_log, minister.GetTitle(), minister.Name, empire.Name)
            {
                Color1 = minister.GetColor().ColorText,
                Color2 = minister.GetColor().ColorText,
                Color3 = empire.GetColor().ColorText,
            }.Add();
        }

        /// <summary>
        /// 记录“大臣获封称号”
        ///     $empire$ → 帝国名
        ///     $minister$ → 大臣名
        ///     $title$ → 新称号
        /// </summary>
        public static void LogPowerfulMinisterAcquireTitle(Person minister, Empire empire, string title)
        {
            new News(NewsKinds.empire_powerful_minister_aquire_title,
                minister.Name,
                empire.GetEmpireName(),
                title)
            {
                Color1 = minister.Realm.GetColor().ColorText,
                Color2 = empire.CoreKingdom.GetColor().ColorText
            }.Add();
        }

        public static void LogPowerfulMinisterControlsCourt(Person minister, Empire empire)
        {
            new News(NewsKinds.powerful_minister_controls_court_log,
                minister.FullName(), empire.Emperor?.FullName() ?? "", empire.GetEmpireName())
            {
                Color1 = minister.GetColor().ColorText,
                Color2 = empire.GetColor().ColorText,
                Color3 = empire.GetColor().ColorText
            }.Add();
        }

        public static void LogPowerfulMinisterNineBestowments(Person minister, Empire empire, string title)
        {
            new News(NewsKinds.powerful_minister_nine_bestowments_log,
                minister.FullName(), title, empire.GetEmpireName())
            {
                Color1 = minister.GetColor().ColorText,
                Color2 = minister.GetColor().ColorText,
                Color3 = empire.GetColor().ColorText
            }.Add();
        }

        public static void LogPowerfulMinisterUsurpation(Person minister, Empire empire, string newEmpireName)
        {
            new News(NewsKinds.powerful_minister_usurpation_log,
                minister.FullName(), empire.GetEmpireName(), newEmpireName)
            {
                Color1 = minister.GetColor().ColorText,
                Color2 = empire.GetColor().ColorText,
                Color3 = minister.GetColor().ColorText
            }.Add();
        }

        public static void LogEmpressDowagerInstallsSon(Person dowager, Person deposed, Person son)
        {
            new News(NewsKinds.empress_dowager_installs_son_log,
                dowager.FullName(), deposed.FullName(), son.FullName())
            {
                Color1 = dowager.GetColor().ColorText,
                Color2 = deposed.GetColor().ColorText,
                Color3 = son.GetColor().ColorText
            }.Add();
        }
        public static void LogCreateTitle(Realm kingdom, KingdomTitle title)
        {
            new News(NewsKinds.king_create_title_log,
                kingdom.Name,
                kingdom.Ruler.Name,
                title.data.name)
            {
                Color1 = kingdom.GetColor().ColorText,
                Color2 = kingdom.GetColor().ColorText,
                Color3 = title.GetColor().ColorText
            }.Add();
        }
        public static void LogEmpireCoreAbsorbTitle(Empire empire, KingdomTitle title, EmpireCore core)
        {
            if (empire == null || title == null || core == null) return;
            new News(
                NewsKinds.empire_core_absorb_title_log,
                empire.GetEmpireName(),
                title.data?.name ?? "",
                EmpireCoreManager.GetDisplayName(core))
            {
                Color1 = empire.GetColor().ColorText,
                Color2 = title.GetColor().ColorText,
                Color3 = empire.GetColor().ColorText
            }.RecordIntoEmpire(empire);
        }
        public static void LogEmpirePressureSurrender(Empire attacker, Empire defender, string targetName, bool cityOnly = false)
        {
            if (attacker == null || defender == null || string.IsNullOrWhiteSpace(targetName)) return;
            new News(
                cityOnly ? NewsKinds.empire_pressure_surrender_city_log : NewsKinds.empire_pressure_surrender_log,
                attacker.GetEmpireName(),
                defender.GetEmpireName(),
                targetName)
            {
                Color1 = attacker.GetColor().ColorText,
                Color2 = defender.GetColor().ColorText,
                Color3 = defender.GetColor().ColorText
            }.RecordIntoEmpire(attacker);
        }
        
        // 选定接班人：只报帝国与五城以上的大国(小国换来换去刷屏)
        public static void LogKingChooseHeir(Realm kingdom,string relation, Person pActor)
        {
            if (kingdom != null && pActor != null && (kingdom.IsEmpire() || kingdom.CountCities() >= 5))
                new News(NewsKinds.king_choose_heir_log,
                    (kingdom.GetKingdomName() ?? "") + (kingdom.GetOffice()?.GetName() ?? ""),
                    relation,
                    pActor.Name)
                {
                    Color1 = kingdom.GetColor().ColorText,
                    Color3 = pActor.GetColor().ColorText
                }.RecordIntoEmpire(pActor.GetEmpire());
        }
        
        public static void LogProvinceChangeToKingdom(Realm province, string name)
        {
            new News(NewsKinds.province_change_to_kingdom_log,
                province.Name,
                name
                )
            {
                Color1 = province.GetColor().ColorText,
                Color2 = province.GetColor().ColorText

            }.RecordIntoEmpire();
        }
        public static void LogMinisterSelectEmpire(Empire empire, OfficeObject office, Realm kingdom, Person actor)
        {
            new News(NewsKinds.minister_select_emperor_log,
                empire.data.name,
                (office?.GetOfficeName(kingdom)??"")+" "+actor.Name
                )
            {
                Color1 = empire.GetColor().ColorText,
                Color2 = actor.GetColor().ColorText

            }.RecordIntoEmpire(empire);
        }
        public static string GetEmpireHistoryFormatedText(this News pMessage)
        {
            NewsKind asset = pMessage.GetKind();
            string localeId;
            if (asset.Variants > 0)
            {
                int pIndex = pMessage.Stamp % asset.Variants + 1;
                localeId = asset.LocaleId(pIndex);
            }
            else
                localeId = asset.LocaleId();
            string text = LM.Get(localeId);
            if (asset.TextReplacer != null)
                asset.TextReplacer(pMessage, ref text);
            return text.ColorString(pColor:asset.Color);
        }
        // 系统事件：发一条世界消息（不再弹屏幕中央提示）。记入史书由调用方负责——大部分调用点
        // 在此之前已经 RecordHistory 过了，这里不重复记录。
        // 世界日志里的事件也写进这国所属帝国的史书(已经写过同一句的不重复写；独立王国没有史书)
        public static void LogEventMessage(string content, Realm kingdom = null)
        {
            // 进史书的：屏幕提示只上帝国、大国的(见 ScreenFeedPatch)
            Log(content, kingdom, NewsLevel.History);
            if (string.IsNullOrWhiteSpace(content) || kingdom?.Record == null || kingdom.IsGone()) return;
            EmpireCraft.Core.Empire empire = kingdom.GetEmpire();
            if (empire?.data != null && !empire.IsGone()) empire.RecordHistory(directContent: content);
        }

        // 只进世界日志、不进史书：太频繁的事(市场缺货、发现矿藏、任命统帅、理念传播之类)。
        // 同一句话三年内只报一次(世界提示刷屏的多是同一件事反复报)
        private const float RepeatYears = 3f;
        private static readonly Dictionary<string, double> LastLogged = new();

        public static void LogEventOnly(string content, Realm kingdom = null) =>
            Log(content, kingdom, NewsLevel.Minor);

        // 不论哪国都上屏的大事
        public static void LogMajor(string content, Realm kingdom = null) =>
            Log(content, kingdom, NewsLevel.Major);

        private static void Log(string content, Realm kingdom, NewsLevel level)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            double now = GameClock.Now;
            if (LastLogged.TryGetValue(content, out double last) && now >= last && GameClock.YearsSince(last) < RepeatYears) return;
            if (LastLogged.Count > 2000) LastLogged.Clear();
            LastLogged[content] = now;
            WorldNews.Pending = level;
            WorldNews.PendingRealm = kingdom;
            try
            {
                new News(NewsKinds.empirecraft_event_log, content)
                {
                    Color1 = kingdom != null && !kingdom.IsGone()
                        ? kingdom.GetColor().ColorText
                        : Tint.Good
                }.Add();
            }
            finally
            {
                WorldNews.Pending = NewsLevel.None;
                WorldNews.PendingRealm = null;
            }
        }

        public static void RecordIntoEmpire(this News worldLog, Empire pEmpire = null)
        {
            worldLog.Add();
        }
        public static void RecordNationalHistoryIntoEmpire(this News worldLog, Empire empire,
            Person actor = null, Realm kingdom = null)
        {
            worldLog.Add();
            empire?.RecordHistory(directContent: worldLog.GetEmpireHistoryFormatedText(),
                actorId: actor?.Key ?? -1L, kingdomId: kingdom?.Key ?? -1L);
        }
        // 爵位名：人物身上记的是"文化_peerages_N"，没有这个文化专门叫法的退回默认叫法(default_peerages_N)；
        // 以前直接取，没有的就在日志里报缺字(Huaxia_peerages_6)
        public static string PeerageName(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return "";
            if (LM.Has(key)) return LM.Get(key);
            int at = key.LastIndexOf("peerages_", StringComparison.Ordinal);
            string fallback = at >= 0 ? "default_" + key.Substring(at) : null;
            return fallback != null && LM.Has(fallback) ? LM.Get(fallback) : "";
        }
        public static void LogHonoraryPeerageGranted(Person actor, Empire empire, string peerageKey)
        {
            LogPeerageGranted(actor, empire, LM.Get(peerageKey));
        }
        public static void LogPeerageGranted(Person actor, Empire empire, string titleName)
        {
            if (actor == null || empire == null) return;
            new News(NewsKinds.honorary_peerage_granted_log,
                empire.GetEmpireFullName(), GetActorFullLogName(actor), titleName)
            {
                Color1 = empire.CoreKingdom.GetColor().ColorText,
                Color2 = actor.GetColor().ColorText,
                Color3 = empire.CoreKingdom.GetColor().ColorText
            }.RecordNationalHistoryIntoEmpire(empire, actor);
            actor.RecordPersonalHistory(string.Format(LM.Get("personal_history_peerage_granted"),
                empire.GetEmpireFullName(), titleName), relatedActorId: empire.Emperor?.Key ?? -1L);
            EmpireCraft.Core.PrinceLineage.NoteGrant(actor, titleName);
        }
        public static void LogHonoraryPeerageInherited(Person heir, Person predecessor, Empire empire, string peerageKey)
        {
            LogPeerageInherited(heir, empire, LM.Get(peerageKey), predecessor?.GetPersonalIdentity(),
                predecessor?.Key ?? -1L, predecessor?.FullName());
        }
        public static void LogPeerageInherited(Person heir, Empire empire, string titleName,
            PersonalClanIdentity predecessor, long predecessorId, string predecessorName)
        {
            if (heir == null || empire == null) return;
            predecessorName = predecessor?.name ?? predecessorName ?? "";
            new News(NewsKinds.honorary_peerage_inherited_log,
                GetActorFullLogName(heir), predecessorName,
                $"{titleName} ({empire.GetEmpireFullName()})")
            {
                Color1 = heir.GetColor().ColorText,
                Color2 = empire.CoreKingdom.GetColor().ColorText,
                Color3 = empire.CoreKingdom.GetColor().ColorText
            }.RecordNationalHistoryIntoEmpire(empire, heir);
            heir.RecordPersonalHistory(string.Format(LM.Get("personal_history_peerage_inherited"),
                titleName, predecessorName), relatedActorId: predecessorId,
                relatedPersonalIdentityId: predecessor?.id ?? -1L);
        }
        // 判决上公屏(独立王国也报，2026-11 起)。犯人的名号、颜色、上不上公屏在立案时记下(施刑后罢了官、伏了诛也照原样写)；
        // 卷宗、前科在这里补记(揭发罪行的计谋自己施刑，见 EmpireLawSystem.FinishCase)
#if EC_PENDING
        public static void LogLawEnforcement(LawEnforcementContext context)
        {
            if (context == null) return;
            EmpireLawSystem.FinishCase(context);

            Person actor = context.Person;
            Realm kingdom = context.Realm;
            if (actor == null || kingdom == null || kingdom.IsGone()) return;
            Empire empire = kingdom.GetEmpire();
            // 立案时没记(旧的调用方自己拼的案情)就按现在的身份判断
            bool broadcast = string.IsNullOrEmpty(context.ActorLogName) ? ShouldBroadcastLawActor(actor) : context.Broadcast;
            if (!broadcast) return;

            string actorName = !string.IsNullOrEmpty(context.ActorLogName) ? context.ActorLogName : GetActorFullLogName(actor);
            string crimeDetailText = GetLawCrimeDetailText(context.LawName, context.CrimeDate);
            string reason = EmpireLawSystem.GetReasonText(context.ReasonKey);
            if (!string.IsNullOrEmpty(reason)) crimeDetailText = string.Format(LM.Get("empire_law_crime_with_reason"), crimeDetailText, reason);
            string punishmentText = GetPunishmentText(context);
            UnityEngine.Color kingdomColor = kingdom.GetColor().ColorText;
            News worldLog = new News(
                NewsKinds.empire_law_enforced_log,
                actorName,
                crimeDetailText,
                punishmentText)
            {
                Color1 = context.ActorColorText ?? kingdomColor,
                Color2 = kingdomColor,
                Color3 = kingdomColor
            };

            if (empire != null && ShouldRecordLawEnforcementHistory(actor, empire))
            {
                worldLog.RecordIntoEmpire(empire);
                return;
            }

            worldLog.Add();
        }
#endif

        public static void LogLawArrest(Person actor, Realm kingdom, string crimeName, string crimeDate)
        {
            if (actor == null || kingdom == null) return;

            Empire empire = kingdom.GetEmpire();
            if (!ShouldBroadcastLawActor(actor)) return;

            string actorName = GetActorFullLogName(actor);

            News worldLog = new News(
                NewsKinds.empire_law_arrest_log,
                actorName,
                string.IsNullOrEmpty(crimeDate) ? GameClock.DateText(GameClock.Now) : crimeDate,
                EmpireLawSystem.LocalizeCrimeName(crimeName))
            {
                Color1 = actor.GetColor().ColorText,
                Color2 = kingdom.GetColor().ColorText,
                Color3 = kingdom.GetColor().ColorText
            };

            if (empire != null && ShouldRecordLawEnforcementHistory(actor, empire))
            {
                worldLog.RecordIntoEmpire(empire);
                return;
            }

            worldLog.Add();
        }

        // 所施刑罚；减等、赎罪、开释、从重另注结果与缘由(八议、赎金、时代废刑……)
        private static string GetPunishmentText(LawEnforcementContext context)
        {
            if (context == null) return LM.Get("empire_law_default_punishment");
            List<PunishmentLevel> applied = (context.AppliedPunishments ?? new List<PunishmentLevel>())
                .Where(p => p != PunishmentLevel.无罪).ToList();
            string text = applied.Count > 0
                ? EmpireLawSystem.GetPunishmentsText(applied)
                : context.CaseRecorded ? EmpireLawSystem.GetOutcomeName(context.Outcome) : LM.Get("empire_law_default_punishment");
            if (!context.CaseRecorded || context.Outcome == LawCaseOutcome.定罪) return text;
            var notes = (context.Notes ?? new List<string>()).Select(EmpireLawSystem.FormatNote)
                .Where(note => !string.IsNullOrEmpty(note)).ToList();
            string outcome = EmpireLawSystem.GetOutcomeName(context.Outcome);
            if (notes.Count == 0) return applied.Count == 0 ? outcome : string.Format(LM.Get("empire_law_punishment_with_outcome"), text, outcome);
            string detail = string.Format(LM.Get("empire_law_outcome_detail"), outcome, string.Join(LM.Get("law_list_separator"), notes));
            return applied.Count == 0 ? detail : string.Format(LM.Get("empire_law_punishment_with_outcome"), text, detail);
        }

        private static string GetLawCrimeDetailText(string crimeName, string crimeDate)
        {
            string localizedCrimeName = GetTemporaryFactionCrimeText(crimeName);
            string templateKey = string.IsNullOrWhiteSpace(crimeDate)
                ? "empire_law_crime_detail_without_date"
                : "empire_law_crime_detail_with_date";
            string template = LM.Get(templateKey);
            if (string.IsNullOrWhiteSpace(template) || template == templateKey)
            {
                return string.IsNullOrWhiteSpace(crimeDate)
                    ? localizedCrimeName
                    : $"{crimeDate} {localizedCrimeName}";
            }

            return template
                .Replace("$date$", crimeDate ?? "")
                .Replace("$crime$", localizedCrimeName ?? "");
        }

        public static bool ShouldBroadcastLawActor(Person actor)
        {
            if (actor == null || actor.IsGone())
            {
                return false;
            }

            if (actor.IsEmperor())
            {
                return false;
            }

            return actor.isOfficer() || actor.IsOnOffice() || actor.GetOffice() != null || actor.IsRuler() || actor.HasTitle() || actor.IsCityLeader();
        }

        public static string GetPunishmentName(PunishmentLevel punishment) => EmpireLawSystem.GetPunishmentName(punishment);

        private static bool ShouldRecordLawEnforcementHistory(Person actor, Empire empire)
        {
            if (actor == null || empire == null) return false;

            if (actor.isOfficer() || actor.IsOnOffice() || actor.GetOffice() != null)
            {
                return true;
            }

            if (actor.IsEmperor() || actor.IsRuler() || actor.HasTitle())
            {
                return true;
            }

            if (actor.HasSpecificClan() && empire.Emperor != null && empire.Emperor.HasSpecificClan())
            {
                return actor.GetSpecificClan() == empire.Emperor.GetSpecificClan();
            }

            return false;
        }
        public static void LogOfficerJoinFaction(OfficeObject office, Person pActor, FixedFaction faction)
        {
            new News(NewsKinds.officer_join_faction,
                office?.GetOfficeName() ?? "",
                pActor.Name,
                faction.Name
            )
            {
                Color1 = pActor.GetColor().ColorText,
                Color2 = pActor.GetColor().ColorText,
                Color3 = pActor.GetColor().ColorText

            }.RecordIntoEmpire(faction.Empire);
        }

        private static News CreateTemporaryFactionMessage(NewsKind asset, Empire empire, string pSpecial1, string pSpecial2 = "", string pSpecial3 = "")
        {
            return new News(asset, pSpecial1, pSpecial2, pSpecial3)
            {
                Color1 = empire?.CoreKingdom?.GetColor()?.ColorText ?? Tint.Good,
                Color2 = empire?.CoreKingdom?.GetColor()?.ColorText ?? Tint.Good,
                Color3 = empire?.CoreKingdom?.GetColor()?.ColorText ?? Tint.Good
            };
        }

        // 按帝国换称呼的诉求名(华夏原始政体：索取皇位 → 争为共主，见 DynastyNaming.Term)
#if EC_PENDING
        public static string GetTemporaryFactionClaimText(TemporaryFactionType claimType, Empire empire)
        {
            string baseKey = $"temporary_faction_claim_{(int)claimType}";
            string key = EmpireCraft.Core.DynastyNaming.Term(empire, baseKey);
            if (key.EndsWith("_huaxia_origin", StringComparison.Ordinal)) return LM.Get(key);
            // 诉求按帝国的文化线换说法(西方不叫推行三省六部、屯田实边……见 CultureDoctrines)，机制不变
            string cultureKey = CultureDoctrines.Key(baseKey, CultureDoctrines.CultureOf(empire));
            return cultureKey != baseKey ? LM.Get(cultureKey) : GetTemporaryFactionClaimText(claimType);
        }
#endif

        // 史书、朝局纪事里的诉求名：按帝国的文化线(诉求名记的是枚举名)
#if EC_PENDING
        public static string GetTemporaryFactionClaimText(string claimName, Empire empire) =>
            empire != null && Enum.TryParse(claimName, out TemporaryFactionType claimType)
                ? GetTemporaryFactionClaimText(claimType, empire)
                : GetTemporaryFactionClaimText(claimName);
#endif

        public static string GetTemporaryFactionClaimText(TemporaryFactionType claimType)
        {
            string fallback = claimType.ToString();
            string key = $"temporary_faction_claim_{(int)claimType}";
            string localized = LM.Get(key);
            if (!string.IsNullOrWhiteSpace(localized) && !string.Equals(localized, key, StringComparison.Ordinal))
            {
                return localized;
            }

            return GetTemporaryFactionClaimText(fallback);
        }

        public static string GetTemporaryFactionClaimText(string claimName)
        {
            if (string.IsNullOrWhiteSpace(claimName))
            {
                return "";
            }

            if (Enum.TryParse(claimName, out TemporaryFactionType claimType))
            {
                string key = $"temporary_faction_claim_{(int)claimType}";
                string localizedByStableKey = LM.Get(key);
                if (!string.IsNullOrWhiteSpace(localizedByStableKey) &&
                    !string.Equals(localizedByStableKey, key, StringComparison.Ordinal))
                {
                    return localizedByStableKey;
                }
            }

            string localized = LM.Get(claimName);
            return !string.IsNullOrEmpty(localized) && localized != claimName ? localized : claimName;
        }

        private static string GetTemporaryFactionCrimeText(string crimeName)
        {
            if (string.IsNullOrWhiteSpace(crimeName))
            {
                return "";
            }

            // 罪名(枚举名或律法表的中文名)换成本地化的罪名；其余照旧按本地化键查
            string lawName = EmpireLawSystem.LocalizeCrimeName(crimeName);
            if (lawName != crimeName) return lawName;
            if (!LM.Has(crimeName)) return crimeName;
            string localized = LM.Get(crimeName);
            return !string.IsNullOrEmpty(localized) && localized != crimeName ? localized : crimeName;
        }

        private static string GetBaseActorLogName(Person actor)
        {
            if (actor == null)
            {
                return "";
            }

            string actorName = actor.FullName();
            if (string.IsNullOrWhiteSpace(actorName))
            {
                actorName = actor.Name;
            }

            return actorName ?? "";
        }

        private static string GetActorLogOfficeName(Person actor)
        {
            if (actor == null)
            {
                return "";
            }

            OfficeObject office = actor.GetOffice();
            if (office != null)
            {
                string officeName = office.GetOfficeName(office.meta_object);
                if (!string.IsNullOrWhiteSpace(officeName))
                {
                    return officeName;
                }
            }

            string title = actor.GetTitle();
            return string.IsNullOrWhiteSpace(title) ? "" : title;
        }

        private static string FormatActorFullLogName(string officeName, string actorName)
        {
            if (string.IsNullOrWhiteSpace(actorName))
            {
                return officeName ?? "";
            }

            if (string.IsNullOrWhiteSpace(officeName))
            {
                return actorName;
            }

            string template = LM.Get("log_actor_full_name_format");
            if (string.IsNullOrWhiteSpace(template) || template == "log_actor_full_name_format")
            {
                return officeName + " " + actorName;
            }

            return template
                .Replace("$office$", officeName)
                .Replace("$actor$", actorName);
        }

        public static string GetActorFullLogName(Person actor)
        {
            if (actor == null)
            {
                return "";
            }

            return FormatActorFullLogName(GetActorLogOfficeName(actor), GetBaseActorLogName(actor));
        }

        public static string GetTemporaryFactionTargetText(EntityKind targetType, long targetId)
        {
            if (targetId < 0)
            {
                return null;
            }

            switch (targetType)
            {
                case EntityKind.Realm:
                    return EmpireWorld.Current?.RealmByKey(targetId)?.GetKingdomName();
                case EntityKind.City:
                    return EmpireWorld.Current?.CityByKey(targetId)?.GetCityName();
                case EntityKind.Religion:
                    return EmpireWorld.Current?.Religions?.Get(targetId)?.data?.name;
                case EntityKind.Person:
                    return GetActorFullLogName(EmpireWorld.Current?.PersonByKey(targetId));
                case EntityKind.None:
                    return null;
                default:
                    if (targetType == MetaTypeExtension.KingdomTitle)
                    {
                        return ModClass.KINGDOM_TITLE_MANAGER.Get(targetId)?.data?.name;
                    }

                    return null;
            }
        }

#if EC_PENDING
        public static void LogTemporaryFactionPreparing(Empire empire, string claimName, string targetName = null, string crimeName = null)
        {
            if (empire == null || string.IsNullOrWhiteSpace(claimName))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(crimeName) && !string.IsNullOrWhiteSpace(targetName))
            {
                CreateTemporaryFactionMessage(NewsKinds.temporary_faction_prepare_crime_log, empire,
                    targetName,
                    GetTemporaryFactionCrimeText(crimeName),
                    GetTemporaryFactionClaimText(claimName, empire)).RecordIntoEmpire(empire);
                return;
            }

            var asset = string.IsNullOrWhiteSpace(targetName)
                ? NewsKinds.temporary_faction_prepare_no_target_log
                : NewsKinds.temporary_faction_prepare_log;

            CreateTemporaryFactionMessage(asset, empire,
                empire.GetEmpireName() ?? empire.data?.name ?? "",
                GetTemporaryFactionClaimText(claimName, empire),
                targetName ?? "").RecordIntoEmpire(empire);
        }

        public static void LogTemporaryFactionSucceeded(Empire empire, string claimName, string targetName = null,
            string crimeName = null, string outcome = null)
        {
            if (empire == null || string.IsNullOrWhiteSpace(claimName))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(outcome))
            {
                CreateTemporaryFactionMessage(NewsKinds.temporary_faction_success_outcome_log,
                    empire, empire.GetEmpireName() ?? empire.data?.name ?? "",
                    GetTemporaryFactionClaimText(claimName, empire), outcome)
                    .RecordNationalHistoryIntoEmpire(empire, empire.Emperor);
                return;
            }

            if (!string.IsNullOrWhiteSpace(crimeName) && !string.IsNullOrWhiteSpace(targetName))
            {
                CreateTemporaryFactionMessage(NewsKinds.temporary_faction_success_crime_log, empire,
                    targetName,
                    GetTemporaryFactionCrimeText(crimeName),
                    GetTemporaryFactionClaimText(claimName, empire)).RecordIntoEmpire(empire);
                return;
            }

            var asset = string.IsNullOrWhiteSpace(targetName)
                ? NewsKinds.temporary_faction_success_no_target_log
                : NewsKinds.temporary_faction_success_log;

            CreateTemporaryFactionMessage(asset, empire,
                empire.GetEmpireName() ?? empire.data?.name ?? "",
                GetTemporaryFactionClaimText(claimName, empire),
                targetName ?? "").RecordIntoEmpire(empire);
        }

        public static void LogTemporaryFactionFailed(Empire empire, string claimName, string targetName = null, string crimeName = null)
        {
            if (empire == null || string.IsNullOrWhiteSpace(claimName))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(crimeName) && !string.IsNullOrWhiteSpace(targetName))
            {
                CreateTemporaryFactionMessage(NewsKinds.temporary_faction_failed_crime_log, empire,
                    targetName,
                    GetTemporaryFactionCrimeText(crimeName),
                    GetTemporaryFactionClaimText(claimName, empire)).RecordIntoEmpire(empire);
                return;
            }

            var asset = string.IsNullOrWhiteSpace(targetName)
                ? NewsKinds.temporary_faction_failed_no_target_log
                : NewsKinds.temporary_faction_failed_log;

            CreateTemporaryFactionMessage(asset, empire,
                empire.GetEmpireName() ?? empire.data?.name ?? "",
                GetTemporaryFactionClaimText(claimName, empire),
                targetName ?? "").RecordIntoEmpire(empire);
        }
#endif

        public static void LogTemporaryFactionOfficialFall(Empire empire, Person actor, string crimeName)
        {
            if (empire == null || actor == null || string.IsNullOrWhiteSpace(crimeName))
            {
                return;
            }

            new News(NewsKinds.temporary_faction_official_fall_log,
                actor.FullName(),
                EmpireLawSystem.LocalizeCrimeName(crimeName))
            {
                Color1 = actor.GetColor().ColorText,
                Color2 = empire.CoreKingdom?.GetColor()?.ColorText ?? Tint.Warning,
            }.RecordIntoEmpire(empire);
        }

        public static void LogTemporaryFactionReduceFeudatory(Empire empire, Realm kingdom)
        {
            if (empire == null || kingdom == null) return;
            new News(NewsKinds.temporary_faction_reduce_feudatory_log,
                kingdom.GetKingdomName())
            {
                Color1 = kingdom.GetColor().ColorText
            }.RecordIntoEmpire(empire);
        }

        public static void LogTemporaryFactionRevokeWarRight(Empire empire, Realm kingdom)
        {
            if (empire == null || kingdom == null) return;
            new News(NewsKinds.temporary_faction_revoke_war_right_log,
                kingdom.GetKingdomName())
            {
                Color1 = kingdom.GetColor().ColorText
            }.RecordIntoEmpire(empire);
        }

        public static void LogTemporaryFactionRevokeMilitaryRegion(Empire empire, Realm kingdom)
        {
            if (empire == null || kingdom == null) return;
            new News(NewsKinds.temporary_faction_revoke_military_region_log,
                kingdom.GetKingdomName())
            {
                Color1 = kingdom.GetColor().ColorText
            }.RecordIntoEmpire(empire);
        }

        public static void LogTemporaryFactionRaiseTax(Empire empire)
        {
            if (empire == null) return;
            new News(NewsKinds.temporary_faction_raise_tax_log,
                empire.GetEmpireName() ?? empire.data?.name ?? "")
            {
                Color1 = empire.CoreKingdom?.GetColor()?.ColorText ?? Tint.Warning,
            }.RecordIntoEmpire(empire);
        }

        public static void LogOccupationCaptureEvent(Person capturer, Person victim, string resultLocaleKey, Empire empire = null)
        {
            if (victim == null) return;
            string capturerName = GetActorFullLogName(capturer);
            if (string.IsNullOrWhiteSpace(capturerName))
            {
                capturerName = capturer?.FullName() ?? "";
            }

            string victimName = GetActorFullLogName(victim);
            string resultText = LM.Get(resultLocaleKey);
            if (string.IsNullOrWhiteSpace(resultText) || resultText == resultLocaleKey)
            {
                resultText = resultLocaleKey;
            }

            new News(NewsKinds.occupation_capture_event_log,
                capturerName,
                victimName,
                resultText)
            {
                Color1 = capturer?.GetColor().ColorText ?? Tint.Good,
                Color2 = victim.GetColor().ColorText,
                Color3 = victim.Realm?.GetColor().ColorText ?? Tint.Warning
            }.RecordIntoEmpire(empire);
        }
        
        public static void LogOfficerBecomeFactionLeader(Person pActor, FixedFaction faction)
        {
            new News(NewsKinds.officer_become_faction_leader,
                pActor.FullName(),
                faction.Name
            )
            {
                Color1 = new Color(1f, 0, 0.5f),
                Color2 = new Color(0.7f, 0.0f, 0.9f),

            }.RecordIntoEmpire(faction.Empire);
        }
        public static void LogNewJingShi(Empire empire, Person pActor)
        {
            // 现代国家的科举就是常规的公务员考试：照常选拔官员，但不再发"殿试已毕、授进士第"的世界提示
            if (empire?.CoreKingdom == null || pActor == null ||
                empire.CoreKingdom.GetRegime()?.type == EmpireCraft.Core.RegimeType.Modern) return;

            new News(NewsKinds.new_jingshi_log,
                empire.data.name,
                pActor.Name
                )
            {
                Color1 = empire.CoreKingdom.GetColor().ColorText,
                Color2 = empire.CoreKingdom.GetColor().ColorText

                }.RecordNationalHistoryIntoEmpire(pActor.GetEmpire(), pActor);
        }
        public static void LogDestroyTitle(Realm kingdom, KingdomTitle title)
        {
            new News(NewsKinds.destroy_title_log,
                kingdom.Ruler.Name,
                title.data.name)
            {
                Color1 = kingdom.GetColor().ColorText,
                Color2 = title.GetColor().ColorText

            }.Add();
        }
        public static void LogKingdomAcquireTitle(Realm attacker, Realm defender, KingdomTitle title)
        {
            new News(NewsKinds.history_kingdom_attack_for_title,
                attacker.Name,
                defender.Name,
                title.data.name)
            {
                Color1 = attacker.GetColor().ColorText,
                Color2 = defender.GetColor().ColorText,
                Color3 = title.GetColor().ColorText

            }.RecordIntoEmpire(attacker.GetEmpire());
        }
        public static void LogReligionWarTransfer(City city, Religion religion)
        {
            new News(NewsKinds.religion_war_transfer_log,
                city.Name,religion.data.name
                )
            {
                Color1 = city.GetColor().ColorText,
                Color2 = religion.GetColor().ColorText

            }.Add();
        }
        public static void LogOfficeMove(Person actor, PeerageType type, int officeLevel)
        {
            new News(NewsKinds.官员品级调动,
                actor.Name, LM.Get($"Huaxia_honoraryofficial_{type.ToString()}_{officeLevel}"), ""+(officeLevel+1)
                )
            {
                Color1 = actor.GetColor().ColorText,
                Color2 = actor.GetColor().ColorText,
                Color3 = actor.GetColor().ColorText

            }.RecordIntoEmpire();
        }
        public static void LogControlledEmpire(Person actor, Empire empire)
        {
            new News(NewsKinds.cotrolled_country_log,
                actor.Name,
                empire.data.name
                )
            {
                Color1 = empire.GetColor().ColorText,
                Color2 = empire.GetColor().ColorText

            }.Add();
        }
        public static void LogJoinEmpireWar(Realm kingdom, Empire empire)
        {
            new News(NewsKinds.join_empire_war_log,
                kingdom.Name,
                empire.data.name
                )
            {
                Color1 = kingdom.GetColor().ColorText,
                Color2 = empire.GetColor().ColorText

            }.RecordNationalHistoryIntoEmpire(empire);
        }
        public static void LogEmpireJoinWar(Empire empire, Realm kingdom)
        {
            new News(NewsKinds.join_empire_war_log,
                empire.data.name,
                kingdom.Name
                )
            {
                Color1 = empire.GetColor().ColorText,
                Color2 = kingdom.GetColor().ColorText

            }.RecordNationalHistoryIntoEmpire(empire);
        }
        public static void LogJoinReligionWar(Realm kingdom, Religion religion)
        {
            new News(NewsKinds.join_religion_war_log,
                kingdom.Name,
                religion.data.name
                )
            {
                Color1 = kingdom.GetColor().ColorText,
                Color2 = religion.GetColor().ColorText

            }.RecordIntoEmpire(kingdom.GetEmpire());
        }
        public static void LogJoinRebellionWar(Realm joiner, Realm beginner, Empire empire)
        {
            if (joiner?.Record == null || beginner?.Record == null || empire == null ||
                NewsKinds.join_rebellion_war_log == null) return;
            new News(NewsKinds.join_rebellion_war_log,
                joiner.Name,
                beginner.Name
                )
            {
                Color1 = joiner.GetColor().ColorText,
                Color2 = beginner.GetColor().ColorText

            }.RecordNationalHistoryIntoEmpire(empire);
        }
        public static void LogOfficerBuildSpecificClan(Person actor, SpecificClan sc)
        {
            new News(NewsKinds.officer_build_specific_clan,
                actor.Name,
                sc.name
                )
            {
                Location = actor.Position,
                Color1 = actor.Realm.GetColor().ColorText,
                Color2 = actor.Clan.GetColor().ColorText

            }.Add();
        }
        public static void LogKingdomChangeCapitalToTitle(Realm kingdom,KingdomTitle title)
        {
            new News(NewsKinds.history_kingdom_change_capital_to_title,
                kingdom.Name,
                title.data.name,
                kingdom.Capital.Name)
            {
                Color1 = kingdom.GetColor().ColorText,
                Color2 = title.GetColor().ColorText,
                Color3 = kingdom.GetColor().ColorText

            }.RecordIntoEmpire(kingdom.GetEmpire());
        }
        public static void LogKingdomJoinEmpire(Realm kingdom,Empire empire)
        {
            new News(NewsKinds.history_kingdom_join_empire,
                kingdom.Name,
                empire.data.name)
            {
                Color1 = kingdom.GetColor().ColorText,
                Color2 = empire.GetColor().ColorText

            }.RecordNationalHistoryIntoEmpire(empire);
        }
        public static void LogNewEmperor(Person emperor, City city, string year_name, bool isNew = false)
        {
            if (emperor == null || emperor.Record == null || emperor.Realm == null || city == null) return;
            var empire = emperor.GetEmpire();
            if (empire?.data == null) return;
            new News(NewsKinds.history_new_emperor,
                emperor.Name,
                city.GetCityName(),
                year_name)
            {
                Color1 = emperor.Realm.GetColor().ColorText,
                Color2 = emperor.Realm.GetColor().ColorText,
                Color3 = emperor.Realm.GetColor().ColorText

            }.RecordIntoEmpire(empire);
            if (empire.data.currentHistory != null)
            {
                empire.data.currentHistory.is_first = isNew;
            }
            if (!string.IsNullOrWhiteSpace(year_name))
            {
                emperor.RecordPersonalHistory(string.Format(LM.Get("personal_history_new_year_name"), year_name),
                    "personal_history_new_year_name");
            }
        }
        public static void LogEmperorNewYearName(Person emperor, Empire empire)
        {
            if (emperor == null || empire == null || !empire.HasYearName()) return;

            new News(NewsKinds.history_emperor_new_year_name,
                empire.GetEmpireName(),
                empire.data.year_name,
                emperor.FullName())
            {
                Location = empire.CoreKingdom != null ? empire.CoreKingdom.Location : emperor.Position,
                Color1 = empire.GetColor().ColorText,
                Color2 = empire.GetColor().ColorText,
                Color3 = emperor.GetColor().ColorText
            }.RecordIntoEmpire(empire);

            emperor.RecordPersonalHistory(string.Format(LM.Get("personal_history_new_year_name"), empire.data.year_name),
                "personal_history_new_year_name");
        }
        public static void LogNewEmperorWest(Person emperor, City city, bool isNew = false)
        {
            if (emperor?.Record == null || emperor.IsGone() || emperor.Realm?.Record == null) return;
            var empire = emperor.GetEmpire();
            if (empire == null || empire.data == null || empire.IsArchived()) return;
            string cityName = city?.Record != null && !city.IsGone()
                ? city.GetCityName()
                : emperor.Realm.Capital?.GetCityName() ?? "";
            new News(NewsKinds.history_new_emperor_west,
                emperor.Name,
                cityName)
            {
                Color1 = emperor.Realm.GetColor().ColorText,
                Color2 = emperor.Realm.GetColor().ColorText

            }.RecordIntoEmpire(empire);
            if (empire.data.currentHistory != null)
            {
                empire.data.currentHistory.is_first = isNew;
            }
        }

        /// <summary>
        /// 加入朝贡国的提示
        /// </summary>
        /// <param name="kingdom">朝贡国</param>
        /// <param name="empire">宗主国</param>
        public static void LogJoinTakenAlliance(Realm kingdom, Empire empire)
        {
            new News(NewsKinds.成为朝贡国,
                kingdom.Name,
                empire.Name)
            {
                Color1 = kingdom.GetColor().ColorText,
                Color2 = empire.CoreKingdom.GetColor().ColorText

            }.Add();
        }

        /// <summary>
        /// 增加派系影响力
        /// </summary>
        /// <param name="invitor">邀请者</param>
        /// <param name="target">目标</param>
        /// <param name="faction">派系</param>
        public static void LogInviteIntoFaction(Realm invitor, Realm target, FixedFaction faction)
        {
            new News(NewsKinds.邀请入派系,
                invitor.Name,
                target.Name,
                faction.Name)
            {
                Color1 = invitor.GetColor().ColorText,
                Color2 = target.GetColor().ColorText,
                Color3 = invitor.GetColor().ColorText,

            }.Add();
        }

        public static void LogFactionLeaderInfluenceLocalKingdom(Person actor, Realm target, FixedFaction faction)
        {
            new News(NewsKinds.faction_leader_influence_local_kingdom_log,
                actor.FullName(),
                target.Name,
                faction.Name)
            {
                Color1 = actor.Realm?.GetColor().ColorText ?? Tint.Good,
                Color2 = target.GetColor().ColorText,
                Color3 = faction.Empire?.CoreKingdom?.GetColor().ColorText ?? Tint.Good
            }.RecordIntoEmpire(faction.Empire);
        }

        /// <summary>
        /// 罪加罪行
        /// </summary>
        /// <param name="actor">执行者</param>
        /// <param name="victim">受害者</param>
        /// <param name="crime">罪行</param>
        public static void LogExposeCrime(Realm actor, Realm victim, string crime)
        {
            new News(NewsKinds.追加罪行,
                actor.Name,
                victim.Name,
                EmpireLawSystem.LocalizeCrimeName(crime))
            {
                Color1 = actor.GetColor().ColorText,
                Color2 = victim.GetColor().ColorText,
                Color3 = victim.GetColor().ColorText,

            }.RecordIntoEmpire();
        }
        public static void LogCityAddToTitle(City city, KingdomTitle title)
        {
            new News(NewsKinds.city_add_to_title_log,
                city.Name,
                title.data.name)
            {
                Color1 = city.GetColor().ColorText,
                Color2 = title.GetColor().ColorText

            }.Add();
        }

        public static void LogKingTakeTitle(Realm kingdom,KingdomTitle title)
        {
            new News(NewsKinds.king_take_title_log,
                kingdom.Name,
                kingdom.Ruler.FullName(),
                title.data.name)
                {
                    Color1 = kingdom.GetColor().ColorText,
                    Color2 = kingdom.GetColor().ColorText,
                    Color3 = title.GetColor().ColorText

                }.Add();
        }

        public static void LogTributaryTitleGranted(Realm kingdom, Empire empire, KingdomTitle title)
        {
            if (kingdom == null || empire == null || title == null) return;
            new News(NewsKinds.tributary_title_granted_log,
                empire.GetEmpireFullName(), kingdom.GetKingdomFullName(), title.data.name)
            {
                Color1 = empire.CoreKingdom.GetColor().ColorText,
                Color2 = kingdom.GetColor().ColorText,
                Color3 = title.GetColor().ColorText
            }.RecordNationalHistoryIntoEmpire(empire, kingdom.Ruler, kingdom);
            kingdom.Ruler?.RecordPersonalHistory(string.Format(LM.Get("personal_history_tributary_title_granted"),
                empire.GetEmpireFullName(), title.data.name), relatedActorId: empire.Emperor?.Key ?? -1L);
        }

        public static void LogKingdomChangeMainTitle(Realm kingdom, KingdomTitle newTitle)
        {
            if (kingdom == null || newTitle == null) return;
            new News(NewsKinds.kingdom_change_main_title_log,
                kingdom.Name,
                newTitle.data.name)
            {
                Color1 = kingdom.GetColor().ColorText,
                Color2 = kingdom.GetColor().ColorText,

            }.RecordIntoEmpire(kingdom.GetEmpire());
        }

        public static void LogBecomeKingdom(Realm kingdom,string title)
        {
            if (kingdom != null&&title!=null) 
            {
                new News(NewsKinds.become_kingdom_log,
                kingdom.Ruler.Name,
                title,
                kingdom.Name)
                {
                    Color1 = kingdom.GetColor().ColorText,
                    Color2 = kingdom.GetColor().ColorText,
                    Color3 = kingdom.GetColor().ColorText

                }.RecordIntoEmpire(kingdom.GetEmpire());
            }
        }

        public static void LogRoyalKingBecomeEmperor(Empire empire,KingdomTitle title, Person actor)
        {
            var language = LM.Language;
            var text = "";
            if (language == "en")
            {
                text = $"{LM.Get("default_" + actor.GetPeeragesLevel())} of {title?.Name??actor.Realm.Capital.GetCityName()}" + " " + actor.Name;
            }
            else
            {
                text = (title?.Name??actor.Realm.Capital.GetCityName())+""+LM.Get("King") + " " + actor.Name;
            }
            new News(NewsKinds.royal_king_become_emperor_log,
                text,
                empire.data.name)
            {
                Color1 = actor.GetColor().ColorText,
                Color2 = empire.GetColor().ColorText

            }.RecordIntoEmpire(empire);
        }

        public static void LogEmpireTakeBackTitle(Person a, string titles, string crime)
        {
            new News(NewsKinds.empire_take_back_title_log,
                a.Name,
                EmpireLawSystem.LocalizeCrimeName(crime),
                titles)
            {
                Color1 = a.GetColor().ColorText,
                Color2 = a.GetColor().ColorText,
                Color3 = a.GetColor().ColorText

            }.RecordIntoEmpire(a.GetEmpire());
        }
        public static void LogChangeCityName(Person pActor, City pCity, string beforeName, string afterName)
        {
            new News(NewsKinds.change_city_name_log,
                pActor.Name,
                beforeName,
                afterName)
            {
                Color1 = pActor.GetColor().ColorText,
                Color2 = pCity.GetColor().ColorText,
                Color3 = pCity.GetColor().ColorText

            }.RecordIntoEmpire();
        }
        public static void LogChangeKingdomName(Person pActor, Realm pKingdom, string beforeName, string afterName)
        {
            new News(NewsKinds.change_kingdom_name_log,
                pActor.Name,
                beforeName,
                afterName)
            {
                Color1 = pActor.GetColor().ColorText,
                Color2 = pKingdom.GetColor().ColorText,
                Color3 = pKingdom.GetColor().ColorText

            }.RecordIntoEmpire();
        }

        public static void LogCombineKingdom(Person pActor)
        {
            new News(NewsKinds.combine_kingdom_log,
                pActor.Name)
            {
                Color1 = pActor.GetColor().ColorText

            }.RecordIntoEmpire(pActor.GetEmpire());
        }
        /// <summary>
        /// 新年号
        ///     $empire$ → 帝国名
        ///     $emperor$ → 皇帝名
        ///     $year_name$ → 年号
        /// </summary>
        public static void LogEmperorCreateNewYearName(Person minister, Empire empire, string title)
        {
            new News(NewsKinds.empire_powerful_minister_aquire_title,
                minister.Name,
                empire.GetEmpireName(),
                title)
            {
                Color1 = minister.Realm.GetColor().ColorText,
                Color2 = empire.CoreKingdom.GetColor().ColorText

            }.RecordIntoEmpire(empire);
        }

        /// <summary>
        /// 记录“大臣获取了天命”
        ///     $title$ → 称号
        ///     $minister$ → 大臣名
        ///     $empire$ → 新帝国名
        /// </summary>
        public static void LogministerAqcuireEmpire(Person minister, Empire new_empire)
        {
            new News(NewsKinds.minister_aqcuire_empire_log,
                minister.GetTitle(),
                minister.Name,
                new_empire.GetEmpireName())
            {
                Color1 = minister.Realm.GetColor().ColorText,
                Color2 = new_empire.CoreKingdom.GetColor().ColorText,
            }.RecordIntoEmpire(new_empire);
        }
        /// <summary>
        /// 记录“恢复历史帝国”
        ///     $clan$ -> 家族
        ///     $empire$ → 帝国名
        /// </summary>
        public static void LogRestoreHistoricalEmpire(Clan clan, Empire empire)
        {
            new News(NewsKinds.restore_historcial_empire,
                empire.Name)
            {
                Color1 = clan.GetColor().ColorText,
                Color2 = empire.CoreKingdom.GetColor().ColorText
            }.RecordIntoEmpire(empire);
        }
        /// <summary>
        /// 记录“追封先帝”
        ///     $actor$ -> 在任皇帝
        ///     $name$ → 被追封人姓名
        /// </summary>
        public static void LogEmpeorNamingPreviousEmperor(Person actor, string name)
        {
            new News(NewsKinds.emperor_posthumous_name,
                actor.Name, name)
            {
                Color1 = actor.Realm.GetColor().ColorText,
                Color2 = actor.Realm.GetColor().ColorText
            }.RecordIntoEmpire(actor.GetEmpire());
        }

        public static void LogDeJureCultureChanged(KingdomTitle title, string oldCulture, string newCulture)
        {
            if (title?.data == null) return;
            string oldName = CultureService.IsValidCulture(oldCulture) ? oldCulture.GetCultureTranslate() : LM.Get("label_none");
            string newName = CultureService.IsValidCulture(newCulture) ? newCulture.GetCultureTranslate() : newCulture;
            EmpireCore core = CultureService.GetInheritedEmpireCore(title);
            Empire empire = EmpireCoreManager.GetEmpires(core).FirstOrDefault();
            News message = new News(NewsKinds.de_jure_culture_changed_log,
                title.data.name, oldName, newName)
            {
                Color1 = title.GetColor().ColorText,
                Color2 = title.GetColor().ColorText,
                Color3 = title.GetColor().ColorText
            };
            if (empire != null)
                message.RecordNationalHistoryIntoEmpire(empire, title.owner, title.main_kingdom);
            else
                message.Add();
        }

        public static void LogCityCultureShift(City city, string oldCulture, string newCulture)
        {
            if (city?.Record == null) return;
            string oldName = CultureService.IsValidCulture(oldCulture) ? oldCulture.GetCultureTranslate() : LM.Get("label_none");
            string newName = CultureService.IsValidCulture(newCulture) ? newCulture.GetCultureTranslate() : newCulture;
            new News(NewsKinds.city_culture_shift_log,
                city.Name, oldName, newName)
            {
                Color1 = city.GetColor().ColorText,
                Color2 = city.GetColor().ColorText,
                Color3 = city.GetColor().ColorText
            }.RecordNationalHistoryIntoEmpire(city.Realm?.GetEmpire(), kingdom: city.Realm);
        }

        public static void LogForeignCultureOccupation(City city, string oldCulture, string newCulture)
        {
            if (city?.Record == null) return;
            string oldName = CultureService.IsValidCulture(oldCulture) ? oldCulture.GetCultureTranslate() : LM.Get("label_none");
            string newName = CultureService.IsValidCulture(newCulture) ? newCulture.GetCultureTranslate() : newCulture;
            new News(NewsKinds.foreign_culture_occupation_log,
                city.Name, oldName, newName)
            {
                Color1 = city.GetColor().ColorText,
                Color2 = city.GetColor().ColorText,
                Color3 = city.GetColor().ColorText
            }.RecordNationalHistoryIntoEmpire(city.Realm?.GetEmpire(), kingdom: city.Realm);
        }

        public static void LogCultureRestorationAvailable(City city, string culture)
        {
            if (city?.Record == null) return;
            string cultureName = CultureService.IsValidCulture(culture) ? culture.GetCultureTranslate() : culture;
            new News(NewsKinds.culture_restoration_available_log,
                city.Name, cultureName)
            {
                Color1 = city.GetColor().ColorText,
                Color2 = city.GetColor().ColorText
            }.RecordNationalHistoryIntoEmpire(city.Realm?.GetEmpire(), kingdom: city.Realm);
        }

        public static void LogCityCultureRestored(City city, string oldCulture, string newCulture)
        {
            if (city?.Record == null) return;
            string oldName = CultureService.IsValidCulture(oldCulture) ? oldCulture.GetCultureTranslate() : LM.Get("label_none");
            string newName = CultureService.IsValidCulture(newCulture) ? newCulture.GetCultureTranslate() : newCulture;
            new News(NewsKinds.city_culture_restored_log,
                city.Name, oldName, newName)
            {
                Color1 = city.GetColor().ColorText,
                Color2 = city.GetColor().ColorText,
                Color3 = city.GetColor().ColorText
            }.RecordNationalHistoryIntoEmpire(city.Realm?.GetEmpire(), kingdom: city.Realm);
        }

        /// <summary>
        /// 城市因为文化转变而改名（这个文化以前在这座城市上没有出现过，现生成了一个新名字）。
        /// </summary>
        public static void LogCityNameChanged(City city, string oldName, string newName, string culture)
        {
            if (city?.Record == null) return;
            string cultureName = CultureService.IsValidCulture(culture) ? culture.GetCultureTranslate() : culture;
            new News(NewsKinds.cultural_name_changed_log,
                oldName, newName, cultureName)
            {
                Color1 = city.GetColor().ColorText,
                Color2 = city.GetColor().ColorText,
                Color3 = city.GetColor().ColorText
            }.RecordNationalHistoryIntoEmpire(city.Realm?.GetEmpire(), kingdom: city.Realm);
        }

        /// <summary>
        /// 城市因为文化转变，恢复了这个文化以前在这座城市上用过、被永久记录下来的历史名字。
        /// </summary>
        public static void LogCityNameRestored(City city, string oldName, string newName, string culture)
        {
            if (city?.Record == null) return;
            string cultureName = CultureService.IsValidCulture(culture) ? culture.GetCultureTranslate() : culture;
            new News(NewsKinds.cultural_name_restored_log,
                oldName, newName, cultureName)
            {
                Color1 = city.GetColor().ColorText,
                Color2 = city.GetColor().ColorText,
                Color3 = city.GetColor().ColorText
            }.RecordNationalHistoryIntoEmpire(city.Realm?.GetEmpire(), kingdom: city.Realm);
        }

        /// <summary>
        /// 法理头衔名 / 省份名因为文化转变而改名（isProvince 区分是头衔名还是省份名）。
        /// </summary>
        public static void LogTitleNameChanged(KingdomTitle title, string oldName, string newName, string culture,
            bool isProvince)
        {
            if (title?.data == null) return;
            string cultureName = CultureService.IsValidCulture(culture) ? culture.GetCultureTranslate() : culture;
            new News(NewsKinds.cultural_name_changed_log,
                oldName, newName, cultureName)
            {
                Color1 = title.GetColor().ColorText,
                Color2 = title.GetColor().ColorText,
                Color3 = title.GetColor().ColorText
            }.RecordNationalHistoryIntoEmpire(title.main_kingdom?.GetEmpire(), title.owner, title.main_kingdom);
        }

        /// <summary>
        /// 法理头衔名 / 省份名因为文化转变，恢复了这个文化以前用过、被永久记录下来的历史名字。
        /// </summary>
        public static void LogTitleNameRestored(KingdomTitle title, string oldName, string newName, string culture,
            bool isProvince)
        {
            if (title?.data == null) return;
            string cultureName = CultureService.IsValidCulture(culture) ? culture.GetCultureTranslate() : culture;
            new News(NewsKinds.cultural_name_restored_log,
                oldName, newName, cultureName)
            {
                Color1 = title.GetColor().ColorText,
                Color2 = title.GetColor().ColorText,
                Color3 = title.GetColor().ColorText
            }.RecordNationalHistoryIntoEmpire(title.main_kingdom?.GetEmpire(), title.owner, title.main_kingdom);
        }

        public static void LogCulturalAssimilationDuty(City city, string targetCulture, int influenceCost)
        {
            if (city?.Record == null) return;
            string cultureName = CultureService.IsValidCulture(targetCulture) ? targetCulture.GetCultureTranslate() : targetCulture;
            new News(NewsKinds.cultural_assimilation_duty_log,
                city.Name, cultureName, "" + influenceCost)
            {
                Color1 = city.GetColor().ColorText,
                Color2 = city.GetColor().ColorText,
                Color3 = city.GetColor().ColorText
            }.Add();
        }

        public static void LogCulturalAssimilationDutyAssigned(Realm kingdom, string targetCulture)
        {
            if (kingdom?.Record == null) return;
            string cultureName = CultureService.IsValidCulture(targetCulture) ? targetCulture.GetCultureTranslate() : targetCulture;
            new News(NewsKinds.cultural_assimilation_duty_assigned_log,
                kingdom.GetKingdomName(), cultureName)
            {
                Color1 = kingdom.GetColor().ColorText,
                Color2 = kingdom.GetColor().ColorText
            }.Add();
        }

        // "文化同化"决议指定帝国直辖城市（而非整个行政区）时记录，与上面按
        // 行政区指定的版本是同一事件的两种触发路径，只是主体从王国换成了城市。
        public static void LogCulturalAssimilationDutyAssignedCity(City city, string targetCulture)
        {
            if (city?.Record == null) return;
            string cultureName = CultureService.IsValidCulture(targetCulture) ? targetCulture.GetCultureTranslate() : targetCulture;
            new News(NewsKinds.cultural_assimilation_duty_assigned_city_log,
                city.Name, cultureName)
            {
                Color1 = city.GetColor().ColorText,
                Color2 = city.GetColor().ColorText
            }.Add();
        }

        public static void LogCulturalAssimilationDutyCompleted(Realm kingdom, string targetCulture)
        {
            if (kingdom?.Record == null) return;
            LogCulturalAssimilationDutyCompleted(kingdom.GetKingdomName(),
                kingdom.GetColor().ColorText, targetCulture);
        }

        public static void LogCulturalAssimilationDutyCompleted(City city, string targetCulture)
        {
            if (city?.Record == null) return;
            LogCulturalAssimilationDutyCompleted(city.Name, city.GetColor().ColorText, targetCulture);
        }

        private static void LogCulturalAssimilationDutyCompleted(string targetName, Color color,
            string targetCulture)
        {
            string cultureName = CultureService.IsValidCulture(targetCulture) ? targetCulture.GetCultureTranslate() : targetCulture;
            new News(NewsKinds.cultural_assimilation_duty_completed_log,
                targetName, cultureName)
            {
                Color1 = color,
                Color2 = color
            }.Add();
        }

        public static void LogCultureReform(Realm kingdom, Empire empire, Person king, string culture, string surname)
        {
            if (kingdom?.Record == null) return;
            string cultureName = CultureService.IsValidCulture(culture) ? culture.GetCultureTranslate() : culture;
            bool renamed = !string.IsNullOrWhiteSpace(surname);
            var message = renamed
                ? new News(NewsKinds.culture_reform_surname_log, kingdom.GetKingdomName(), cultureName, surname)
                : new News(NewsKinds.culture_reform_log, kingdom.GetKingdomName(), cultureName);
            message.Color1 = kingdom.GetColor().ColorText;
            message.Color2 = kingdom.GetColor().ColorText;
            message.Color3 = kingdom.GetColor().ColorText;
            if (empire != null) message.RecordNationalHistoryIntoEmpire(empire, king, kingdom);
            else message.Add();
            king?.RecordPersonalHistory(renamed
                ? string.Format(LM.Get("personal_history_culture_reform_surname"), cultureName, surname)
                : string.Format(LM.Get("personal_history_culture_reform"), cultureName));
        }

        public static void LogKingdomRegimeConversion(Realm kingdom, RegimeType? oldRegime, RegimeType newRegime)
        {
            if (kingdom?.Record == null) return;
            string oldName = GetRegimeDisplayName(oldRegime);
            string newName = GetRegimeDisplayName(newRegime);
            new News(NewsKinds.kingdom_regime_conversion_log,
                kingdom.GetKingdomName(), oldName, newName)
            {
                Color1 = kingdom.GetColor().ColorText,
                Color2 = kingdom.GetColor().ColorText,
                Color3 = kingdom.GetColor().ColorText
            }.Add();
        }

        public static void LogCompositeEmpireAdoptionStarted(Empire empire,
            CompositeEmpireService.AdoptionStatus status)
        {
            if (empire?.CoreKingdom == null || status == null) return;
            string rulingName = CultureService.IsValidCulture(status.RulingCulture)
                ? status.RulingCulture.GetCultureTranslate()
                : status.RulingCulture;
            string institutionalName = CultureService.IsValidCulture(status.InstitutionalCulture)
                ? status.InstitutionalCulture.GetCultureTranslate()
                : status.InstitutionalCulture;
            new News(NewsKinds.composite_empire_adoption_started_log,
                empire.GetEmpireFullName(), rulingName, institutionalName)
            {
                Color1 = empire.GetColor().ColorText,
                Color2 = empire.GetColor().ColorText,
                Color3 = empire.GetColor().ColorText
            }.RecordNationalHistoryIntoEmpire(empire, empire.Emperor, empire.CoreKingdom);
        }

        public static void LogCompositeEmpireAdopted(Empire empire, string rulingCulture,
            string institutionalCulture)
        {
            if (empire?.CoreKingdom == null) return;
            string rulingName = CultureService.IsValidCulture(rulingCulture)
                ? rulingCulture.GetCultureTranslate()
                : rulingCulture;
            string institutionalName = CultureService.IsValidCulture(institutionalCulture)
                ? institutionalCulture.GetCultureTranslate()
                : institutionalCulture;
            new News(NewsKinds.composite_empire_adopted_log,
                empire.GetEmpireFullName(), rulingName, institutionalName)
            {
                Color1 = empire.GetColor().ColorText,
                Color2 = empire.GetColor().ColorText,
                Color3 = empire.GetColor().ColorText
            }.RecordNationalHistoryIntoEmpire(empire, empire.Emperor, empire.CoreKingdom);
        }

        public static void LogCompositeEmpireStageChanged(Empire empire,
            CompositeEmpireIntegrationStage oldStage, CompositeEmpireIntegrationStage newStage)
        {
            if (empire?.CoreKingdom == null) return;
            string oldName = LM.Get($"composite_empire_stage_{oldStage}");
            string newName = LM.Get($"composite_empire_stage_{newStage}");
            new News(NewsKinds.composite_empire_stage_changed_log,
                empire.GetEmpireFullName(), oldName, newName)
            {
                Color1 = empire.GetColor().ColorText,
                Color2 = empire.GetColor().ColorText,
                Color3 = empire.GetColor().ColorText
            }.RecordNationalHistoryIntoEmpire(empire, empire.Emperor, empire.CoreKingdom);
        }

        public static void LogCompositeEmpireCulturalNameAdopted(Empire empire, string newName, string culture)
        {
            if (empire?.CoreKingdom == null || string.IsNullOrWhiteSpace(newName)) return;
            string cultureName = CultureService.IsValidCulture(culture)
                ? culture.GetCultureTranslate()
                : culture;
            new News(NewsKinds.composite_empire_cultural_name_adopted_log,
                empire.GetEmpireFullName(), newName, cultureName)
            {
                Color1 = empire.GetColor().ColorText,
                Color2 = empire.GetColor().ColorText,
                Color3 = empire.GetColor().ColorText
            }.RecordNationalHistoryIntoEmpire(empire, empire.Emperor, empire.CoreKingdom);
        }

        private static string GetRegimeDisplayName(RegimeType? regimeType)
        {
            if (!regimeType.HasValue) return LM.Get("label_none");
            string key = $"regime_type_{regimeType.Value}";
            string localized = LM.Get(key);
            return string.IsNullOrWhiteSpace(localized) || string.Equals(localized, key, StringComparison.Ordinal)
                ? regimeType.Value.ToString()
                : localized;
        }
    }
}
