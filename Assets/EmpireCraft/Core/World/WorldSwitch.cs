using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace EmpireCraft.Core
{
    // The ported systems keep much of their state in static fields, as the mod did. The mod cleared
    // it when the player loaded another world, through a long hand-kept list of ResetWorldState
    // calls. Here that happens when a different world becomes current (EmpireWorld.Activate):
    // every static, parameterless ResetWorldState the core has is called, in a fixed order, plus
    // the few tables the mod cleared by assignment. Found by name, so a system that comes alive
    // later is covered without editing this file.
    //
    // One world at a time is the model: going back to a world after another was current starts
    // its ported systems' runtime state afresh (what is saved with the world is not touched).
    public static class WorldSwitch
    {
        static List<MethodInfo> _resets;
        public static int Resets => (_resets ?? Find()).Count;
        public static int Switches { get; private set; }

        static List<MethodInfo> Find()
        {
            var found = new List<MethodInfo>();
            foreach (var type in typeof(WorldSwitch).Assembly.GetTypes())
            {
                if (type.Namespace != "EmpireCraft.Core" || type.ContainsGenericParameters) continue;
                var method = type.GetMethod("ResetWorldState", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (method != null && method.ReturnType == typeof(void)) found.Add(method);
            }
            found.Sort((a, b) => string.CompareOrdinal(a.DeclaringType.FullName, b.DeclaringType.FullName));
            return _resets = found;
        }

        // Static fields of the core that hold something now: collections with items, numbers that are not zero,
        // references that are set. For finding state that outlives a world. Read-only fields are left out unless
        // they are collections (a readonly table can still fill up).
        public static List<string> Census()
        {
            var found = new List<string>();
            foreach (var type in typeof(WorldSwitch).Assembly.GetTypes())
            {
                if (type.Namespace != "EmpireCraft.Core" || type.ContainsGenericParameters) continue;
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (field.IsLiteral || field.FieldType.ContainsGenericParameters) continue;
                    object value;
                    try { value = field.GetValue(null); } catch (Exception) { continue; }
                    string what = null;
                    if (value is System.Collections.ICollection items) { if (items.Count > 0) what = items.Count + " items"; }
                    else if (field.IsInitOnly) continue;
                    else if (value is int i) { if (i != 0) what = i.ToString(); }
                    else if (value is long l) { if (l != 0) what = l.ToString(); }
                    else if (value is double d) { if (d != 0) what = d.ToString("0.##"); }
                    else if (value is float f) { if (f != 0) what = f.ToString("0.##"); }
                    else if (value is bool b) { if (b) what = "true"; }
                    else if (value != null && !(value is string) && !field.FieldType.IsValueType && !(value is Delegate)) what = "set";
                    if (what != null) found.Add(type.Name + "." + field.Name + " = " + what);
                }
            }
            found.Sort(StringComparer.Ordinal);
            return found;
        }

        // Called when `next` is about to become the current world and was not.
        internal static void Leaving(EmpireWorld next)
        {
            Switches++;
            foreach (var reset in _resets ?? Find())
            {
                try { reset.Invoke(null, null); }
                catch (TargetInvocationException fault)
                {
                    LogService.LogWarning("[EmpireCraft] " + reset.DeclaringType.Name + ".ResetWorldState failed: " + (fault.InnerException ?? fault).Message);
                }
            }
            // Tables the mod reset by assigning anew rather than through a ResetWorldState.
            OfficeManager.Offices.Clear();
            EmpireCoreManager.EmpireCores = new Dictionary<long, EmpireCore>();
            ModClass.ALL_HISTORY_DATA = new Dictionary<long, List<EmpireCraftHistory>>();
            ModClass.CULTURE_NO_EMPIRE_SINCE = new Dictionary<string, double>();
            ModClass.FEUDAL_EMPIRE_LINEAGE = new Dictionary<string, List<long>>();
            ModClass.MOD_ALLIANCE_IDS = new HashSet<long>();
            SpecificClanManager._specificClans = new List<SpecificClan>();
            // The lookups of persons by key go with it: keys start again from 0 in each world, so a lookup left over
            // would pair a person of this world with someone of another (and give him that one's birthday and age).
            SpecificClanManager.RebuildCache();
        }
    }
}
