using UnityEngine;
namespace Orbis.Planet
{
    [CreateAssetMenu(menuName="ORBIS/Planet physical profile")]
    public sealed class PlanetProfile : ScriptableObject
    {
        [Tooltip("Physical planet radius in metres; configurable fictional planet, not an inferred Earth radius.")]
        [Min(10000)] public double radiusMeters=1000000;
        [Min(1)] public float metersPerRenderUnit=1000;
        [Range(16,128)] public int patchSegments=64;
        [Min(100)] public float patchHalfExtentMeters=32000;
    }
}
