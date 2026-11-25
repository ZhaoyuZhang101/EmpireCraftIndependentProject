// Generated from the EmpireCraft mod: Scripts/Layer/IdeologyMapColor.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;

namespace EmpireCraft.Core
{

// Transient color source for the ideology map layer (independent of vanilla religion).
// These objects never enter the world's religion collection or the save file.
public sealed class IdeologyMapColorData : RecordData { }

public sealed class IdeologyMapColor : RecordObject<IdeologyMapColorData>
{
    public override EntityKind KindOfEntity => EmpireCraft.Core.MetaTypeExtension.Ideology;

#if EC_PENDING
    public override ColorLibrary getColorLibrary() => AssetManager.culture_colors_library;

    public static IdeologyMapColor Create(PartyIdeology ideology)
    {
        int count = AssetManager.culture_colors_library.list.Count;
        var color = new IdeologyMapColor();
        var data = new IdeologyMapColorData { name = ideology.ToString() };
        // 近代十三种照旧均分色板；古代理念插在它们之间(避免同色)
        int modern = (int)PartyIdeology.Confucianism, ancient = Enum.GetValues(typeof(PartyIdeology)).Length - modern;
        int index = (int)ideology < modern ? (int)ideology * count / modern
            : ((int)ideology - modern) * count / Math.Max(1, ancient) + count / (2 * modern);
        data.setColorID(count == 0 ? 0 : index % count);
        color.LoadData(data);
        return color;
    }
#endif
}
}
