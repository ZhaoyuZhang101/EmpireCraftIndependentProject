using UnityEngine;

namespace Orbis
{
    public static class OrbisSystemFont
    {
        public static Font Create(int size = 16)
        {
            string[] candidates;

#if UNITY_IOS || UNITY_TVOS || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            candidates = new[] { "PingFang SC", "PingFang TC", "Helvetica Neue", "Arial Unicode MS", "Arial" };
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            candidates = new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "Arial" };
#else
            candidates = new[] { "PingFang SC", "Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans CJK SC", "Noto Sans SC", "Arial" };
#endif

            try
            {
                Font font = Font.CreateDynamicFontFromOSFont(candidates, size);
                if (font != null)
                    return font;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("ORBIS system font fallback: " + e.Message);
            }

            return Object.Instantiate(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
        }
    }
}
