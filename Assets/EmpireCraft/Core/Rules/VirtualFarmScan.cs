// Generated from the EmpireCraft mod: Scripts/GeneralSystems/VirtualFarmScan.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System.Collections.Generic;

namespace EmpireCraft.Core
{

// 保留原版候选集合：同城、风车半径 9、风车所属 region 及邻接 region 的 chunk。
// 从这些 chunk 的全部地块扫描改为直接读取圆内的 253 格。
public static class VirtualFarmScan
{
    public static long Scans { get; private set; }
    public static long TileChecks { get; private set; }
    public static void Reset() { Scans = TileChecks = 0; }

#if EC_PENDING
    public static void Refresh(City city)
    {
        using var timing = FrameProfiler.Measure("虚拟人口·风车农田扫描");
        city.calculated_place_for_farms.Clear();
        city.calculated_grown_wheat.Clear();
        city.calculated_farm_fields.Clear();
        city.calculated_crops.Clear();
        Cell mill = city.GetBuildingOfType("type_windmill")?.Cell;
        if (mill?.Region != null)
        {
            var chunks = new HashSet<MapChunk> { mill.Region.chunk };
            foreach (MapRegion neighbour in mill.Region.neighbours) chunks.Add(neighbour.chunk);
            for (int dx = -9; dx <= 9; dx++)
                for (int dy = -9; dy <= 9; dy++)
                {
                    if (dx * dx + dy * dy > 81) continue;
                    TileChecks++;
                    Cell tile = EmpireWorld.Current.CellAt(mill.X + dx, mill.Y + dy);
                    if (tile?.Zone?.City != city || !chunks.Contains(tile.chunk)) continue;
                    if (tile.Type.CanBeFarm) city.calculated_place_for_farms.Add(tile);
                    if (!tile.Type.FarmField) continue;
                    city.calculated_farm_fields.Add(tile);
                    Building crop = tile.Building;
                    if (crop?.Asset?.Wheat != true) continue;
                    city.calculated_crops.Add(tile);
                    if (crop.component_wheat.isMaxLevel()) city.calculated_grown_wheat.Add(tile);
                }
        }
        city.calculated_place_for_farms.checkAddRemove();
        city.calculated_farm_fields.checkAddRemove();
        city.calculated_crops.checkAddRemove();
        city.calculated_grown_wheat.checkAddRemove();
        Scans++;
    }
#endif
}
}
