using UnityEngine;
namespace Orbis
{
    // Visible on each chunk GameObject during Play Mode; independent renderer and bounds.
    public sealed class OrbisChunkView : MonoBehaviour
    {
        public int chunkId;
        public int tileCount;
        public int zoneCount;
    }
}
