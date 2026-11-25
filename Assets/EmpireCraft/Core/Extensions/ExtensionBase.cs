// Ported from the EmpireCraft mod: Scripts/GameClassExtensions/ExtensionBase.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
namespace EmpireCraft.Core
{

public static class ExtensionBase
{
    public static void Clear<TKey, TData>()
        where TKey : Entity
        where TData : ExtraDataBase, new()
    {
        ExtensionManager<TKey, TData>.Clear();
    }

    public static TData GetOrCreate<TKey, TData>( this TKey entity, bool isSave = false)
        where TKey : Entity
        where TData : ExtraDataBase, new()
    {
        var ed = ExtensionManager<TKey, TData>.GetOrCreate(entity, isSave);
        return ed;
    }

    public static bool SyncData<TKey, TData>( this TKey entity, TData extraData)
        where TKey : Entity
        where TData : ExtraDataBase, new()
    {    
        // 添加新值
        ExtensionManager<TKey, TData>.Update(entity, extraData);
        return true;
    }

    public static void RemoveExtraData<TKey, TData>(this TKey entity)
        where TKey : Entity
        where TData : ExtraDataBase, new()
    {
        if (entity == null) return;
        ExtensionManager<TKey, TData>.Remove(entity);
    }

    public static TData GetExtraData<TKey, TData>(this TKey entity, bool isSave = false)
        where TKey : Entity
        where TData : ExtraDataBase, new()
    {
        if (GetOrCreate<TKey, TData>(entity, isSave) == null) return null;
        TData data = GetOrCreate<TKey, TData>(entity);
        data.id = entity.Key;
        return data;
    }
}
}
