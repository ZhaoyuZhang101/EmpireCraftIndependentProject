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
        internal bool IsActive { get; private set; }
        internal Material ActivePreview => IsActive ? preview : null;

        internal Material ForDraw(Material detailed, int pass) => ForDraw(detailed, pass, -1);

        // Non-blocking initial warmup used behind the loading cover before presenting a new world.
        internal static bool WarmPassAsync(Material detailed, int pass, int depthPass)
        {
            if (!detailed || pass < 0) return true;
            if (depthPass >= 0 && depthPass != pass && !ShaderUtil.IsPassCompiled(detailed, depthPass))
                ShaderUtil.CompilePass(detailed, depthPass, true);
            if (!EditorSettings.asyncShaderCompilation || ShaderUtil.IsPassCompiled(detailed, pass))
                return true;
            bool previous = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = true;
                ShaderUtil.CompilePass(detailed, pass, false);
            }
            finally { ShaderUtil.allowAsyncCompilation = previous; }
            return ShaderUtil.IsPassCompiled(detailed, pass);
        }

        internal Material ForDraw(Material detailed, int pass, int depthPass)
        {
            if (depthPass >= 0 && depthPass != pass && !ShaderUtil.IsPassCompiled(detailed, depthPass))
            {
                // Keep the lightweight depth pass synchronous so _CameraDepthTexture never
                // receives the unprojected flat unit-circle lattice during async compilation.
                ShaderUtil.CompilePass(detailed, depthPass, true);
            }
            if (!EditorSettings.asyncShaderCompilation || ShaderUtil.IsPassCompiled(detailed, pass))
            {
                IsActive = false;
                return detailed;
            }
            // Simulation Update can run outside the editor's asynchronous Game-view scope.
            bool previous = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = true;
                ShaderUtil.CompilePass(detailed, pass, false);
            }
            finally { ShaderUtil.allowAsyncCompilation = previous; }
            if (ShaderUtil.IsPassCompiled(detailed, pass))
            {
                IsActive = false;
                return detailed;
            }

            if (!preview) preview = new Material(detailed.shader) { hideFlags = HideFlags.HideAndDontSave };
            if (preview.shader != detailed.shader) preview.shader = detailed.shader;
            preview.CopyPropertiesFromMaterial(detailed);
            preview.DisableKeyword("ORBIS_RIVER_RASTER");
            preview.DisableKeyword("ORBIS_RIVER_VECTORS");
            if (preview.IsKeywordEnabled(FlatRiverAtlas.DiagnosticKeyword)) preview.DisableKeyword(FlatRiverAtlas.DiagnosticKeyword);
            preview.EnableKeyword("ORBIS_PREVIEW");
            if (depthPass >= 0 && depthPass != pass && !ShaderUtil.IsPassCompiled(preview, depthPass))
                ShaderUtil.CompilePass(preview, depthPass, true);
            if (!ShaderUtil.IsPassCompiled(preview, pass)) ShaderUtil.CompilePass(preview, pass, true);
            IsActive = true;
            return preview;
        }

        public void Dispose()
        {
            IsActive = false;
            if (preview) Object.Destroy(preview);
        }
    }
}
#endif
