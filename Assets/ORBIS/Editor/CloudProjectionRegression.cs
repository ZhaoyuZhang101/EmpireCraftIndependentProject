using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.Editor
{
    internal static class CloudProjectionRegression
    {
        // Exercise the production ray binding on the camera used for the wheel replay.
        // Rows represent near geometry, terrain and the depth buffer's cleared sky.
        internal static string Check(Camera camera, OrbisAtmosphereRenderer atmosphere)
        {
            var projection = camera.projectionMatrix;
            if (!atmosphere.scroll || !atmosphere.scroll.IsTransitioning || projection.m33 <= 0)
                throw new ArgumentException("Expected the wheel camera's mixed projection.");

            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/CloudPassageProbe.shader");
            var probe = new Material(shader);
            const int width = 7, height = 3;
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var read = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                ShaderUtil.CompilePass(probe, 0, true);
                probe.SetFloat("_ProbeProjection", 1);
                typeof(OrbisAtmosphereRenderer).GetMethod("SetRays", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(atmosphere, new object[] { probe });
                var eyeDepths = new[] { camera.nearClipPlane * 2, .32f, camera.farClipPlane };
                var gpu = GL.GetGPUProjectionMatrix(projection, true);
                var api = SystemInfo.graphicsDeviceType;
                bool gl = api == GraphicsDeviceType.OpenGLCore || api == GraphicsDeviceType.OpenGLES3;
                float RawDepth(float eye)
                {
                    var clip = gpu * new Vector4(0, 0, -eye, 1);
                    float ndc = clip.z / clip.w;
                    return gl ? ndc * .5f + .5f : ndc;
                }
                probe.SetVector("_ProbeRawDepths", new Vector4(RawDepth(eyeDepths[0]), RawDepth(eyeDepths[1]),
                    SystemInfo.usesReversedZBuffer ? 0 : 1, 0));
                GL.sRGBWrite = false;
                Graphics.Blit(Texture2D.blackTexture, target, probe, 0);
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                read.Apply();
                float maxDepthError = 0, maxViewportError = 0;
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    var sample = read.GetPixel(x, y);
                    var world = atmosphere.view.transform.TransformPoint(new Vector3(sample.r, sample.g, sample.b));
                    var eye = camera.worldToCameraMatrix.MultiplyPoint(world);
                    var clip = projection * new Vector4(eye.x, eye.y, eye.z, 1);
                    Vector2 uv = new Vector2(clip.x, clip.y) * (.5f / clip.w) + Vector2.one * .5f;
                    Vector2 expected = new Vector2((x + .5f) / width, (y + .5f) / height);
                    float relativeDepthError = Mathf.Abs(sample.a / eyeDepths[y] - 1);
                    float viewportError = Vector2.Distance(uv, expected);
                    if (!(relativeDepthError < .001f && viewportError < .001f))
                        throw new InvalidOperationException($"Cloud ray at {expected}, morph {atmosphere.scroll.Morph:F4}: " +
                            $"expected depth {eyeDepths[y]}, reconstructed {sample.a}, projected to {uv}.");
                    maxDepthError = Mathf.Max(maxDepthError, relativeDepthError);
                    maxViewportError = Mathf.Max(maxViewportError, viewportError);
                }
                return $"PASS mixed projection at {atmosphere.scroll.Morph:F4}: 21 GPU rays, " +
                    $"max relative depth error {maxDepthError:G3}, max viewport error {maxViewportError:G3}";
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = srgb;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(read);
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }
    }
}
