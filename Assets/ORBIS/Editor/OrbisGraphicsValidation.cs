using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Orbis.Editor
{
    public static class OrbisGraphicsValidation
    {
        [MenuItem("Tools/ORBIS/驗證圖形與中文字型")]
        public static void Validate()
        {
            foreach(string name in new[]{"Surface","Clouds","Overlay","Transport","AtmosphereComposite"})
            {
                Shader shader=Resources.Load<Shader>("ORBIS/"+name);
                Require(shader!=null,"找不到 Shader："+name);
                Require(!ShaderUtil.ShaderHasError(shader),"Shader 編譯錯誤："+name+"，請查看 Console。");
                if(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null)
                    Require(shader.isSupported,"目前圖形裝置不支援："+name);
            }
            var font=OrbisSystemFont.Create();
            Require(font!=null,"作業系統未提供可用字型。");
            foreach(char c in "晨昏線體積雲大氣散射溫度濕度氣壓洋流軍團遠征繁體中文")
                if(!font.HasCharacter(c))Debug.LogWarning("本機字型可能缺字，請在 Play Mode 驗收或使用本機授權 CJK 字型："+c);
            UnityEngine.Object.DestroyImmediate(font);
            var noise=Resources.Load<TextAsset>("ORBIS/CloudNoise");
            Require(noise!=null&&noise.bytes.Length==64*64*64*4,"雲噪聲體積資料長度錯誤。");
            var atlas=Resources.Load<Texture2D>("ORBIS/TerrainAtlas");
            Require(atlas!=null&&atlas.width==1254&&atlas.height==1254,"地形材質尺寸或匯入設定錯誤。");
            Debug.Log("ORBIS 圖形資源檢查完成。請進入 Play 模式檢查晨昏線、雲影、中文字與實際幀率。");
        }
        static void Require(bool value,string message){if(!value)throw new InvalidDataException(message);}
    }
}
