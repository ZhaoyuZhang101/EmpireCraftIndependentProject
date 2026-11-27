namespace EmpireCraft.Core
{
    // Hand-written half of WildernessDevelopmentSystem: Clear. As rainforest and great marsh are developed the mod turned
    // their tiles into grass (felling the trees on them). Here a cell's ground is the world map's biome: a developed cell
    // becomes grassland.
    public static partial class WildernessDevelopmentSystem
    {
        private static bool Clear(Cell tile)
        {
            var world = tile?.World;
            if (world == null || tile.Index < 0 || tile.Index >= world.Map.Biomes.Length) return false;
            world.Map.Biomes[tile.Index] = Orbis.Flat.Biome.Grassland;
            return true;
        }
    }
}
