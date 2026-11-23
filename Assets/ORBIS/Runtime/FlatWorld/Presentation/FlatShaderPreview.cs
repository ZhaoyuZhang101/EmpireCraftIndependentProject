#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Orbis.Flat
{
    // Unity's loading shader cannot project the terrain lattice or cut out an animal silhouette.
    // Keep those two operations on screen while the editor compiles a detailed material variant.
    internal sealed class FlatShaderPreview : System.IDisposable
    {
        Material preview;

        internal Material ForDraw(Material detailed, int pass)
        {
            if (!EditorSettings.asyncShaderCompilation || ShaderUtil.IsPassCompiled(detailed, pass)) return detailed;
            // Simulation Update can run outside the editor's asynchronous Game-view scope.
            bool previous = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = true;
                ShaderUtil.CompilePass(detailed, pass, false);
            }
            finally { ShaderUtil.allowAsyncCompilation = previous; }
            if (ShaderUtil.IsPassCompiled(detailed, pass)) return detailed;

            if (!preview) preview = new Material(detailed.shader) { hideFlags = HideFlags.HideAndDontSave };
            if (preview.shader != detailed.shader) preview.shader = detailed.shader;
            preview.CopyPropertiesFromMaterial(detailed);
            preview.DisableKeyword("ORBIS_RIVER_RASTER");
            preview.DisableKeyword("ORBIS_RIVER_VECTORS");
            if(preview.IsKeywordEnabled(FlatRiverAtlas.DiagnosticKeyword))preview.DisableKeyword(FlatRiverAtlas.DiagnosticKeyword);
            preview.EnableKeyword("ORBIS_PREVIEW");
            if (!ShaderUtil.IsPassCompiled(preview, pass)) ShaderUtil.CompilePass(preview, pass, true);
            return preview;
        }

        public void Dispose()
        {
            if (preview) Object.Destroy(preview);
        }
    }
}
#endif
