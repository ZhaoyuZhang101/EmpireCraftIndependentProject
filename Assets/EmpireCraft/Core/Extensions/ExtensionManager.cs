// Generated from the EmpireCraft mod: Scripts/GameClassExtensions/ExtensionManager.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
namespace EmpireCraft.Core
{

public static class ExtensionManager<TKey, TData>
    where TKey : Entity
    where TData : class, new()
{
    public static ConditionalWeakTable<TKey, TData> _table = new();

    public static TData GetOrCreate(TKey key, bool isSave=false)
    {
        // 深递归探针(见 StackGuard)：模组逻辑几乎每层都经过这里
        StackGuard.Probe();
        if (isSave)
        {
            if (key == null) return null;
            if (_table.TryGetValue(key, out TData data))
            {
                return data;
            } else
            {
                return null;
            }
        }
        if (key == null) return default;
        TData d = _table.GetOrCreateValue(key);
        return d;

    }
    public static void Update(TKey key, TData data)
    {
        _table.Remove(key);
        _table.Add(key, data);
    }

    public static bool Remove(TKey key)
    {
        return _table.Remove(key);
    }

    public static void Clear()
    {
        _table = new();
    }
}
}
