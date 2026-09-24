using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Orbis.Editor
{
    /// <summary>Independent PNG-slice data bridge, not the upstream HDRP generator.
    /// R: cloud body, G: erosion; preserve raw linear 8-bit channel values.</summary>
    public static class OrbisCloudNoiseImport
    {
        const string AssetPath="Assets/ORBIS/Resources/ORBIS/CloudNoiseExternal.asset";
        static int Index(string path)
        {
            Match match=Regex.Match(Path.GetFileNameWithoutExtension(path),@"(\d+)(?!.*\d)");
            if(!match.Success||!int.TryParse(match.Value,out int n))throw new InvalidDataException("切片檔名需以編號排序："+path);
            return n;
        }
        [MenuItem("Tools/ORBIS/V4.2/匯入雲噪聲 PNG 切片")]
        public static void Import()
        {
            if(EditorApplication.isPlaying){EditorUtility.DisplayDialog("ORBIS","請先停止 Play，再匯入噪聲。","確定");return;}
            string folder=EditorUtility.OpenFolderPanel("選擇僅含連續 PNG 切片的資料夾","","");if(string.IsNullOrEmpty(folder))return;
            Texture3D volume=null;Texture2D slice=null;
            try
            {
                string[] files=Directory.GetFiles(folder).Where(f=>string.Equals(Path.GetExtension(f),".png",StringComparison.OrdinalIgnoreCase)).OrderBy(Index).ToArray();
                if(files.Length<2||files.Length>256)throw new InvalidDataException("需要 2–256 張連續的 RGB/RGBA、8 位 PNG 切片。");
                for(int i=1;i<files.Length;i++)if(Index(files[i])!=Index(files[i-1])+1)throw new InvalidDataException("切片編號重複或缺號。");
                var existing=AssetDatabase.LoadAssetAtPath<Texture3D>(AssetPath);
                if(existing&&!EditorUtility.DisplayDialog("覆蓋雲噪聲？",AssetPath+" 將替換；先備份已有素材。","替換","取消"))return;
                int width=0,height=0;Color32[] voxels=null;
                for(int z=0;z<files.Length;z++)
                {
                    if(EditorUtility.DisplayCancelableProgressBar("ORBIS 雲噪聲","讀入 "+Path.GetFileName(files[z]),(float)z/files.Length))return;
                    byte[] data=File.ReadAllBytes(files[z]);
                    if(data.Length<33||data[0]!=137||data[1]!=80||data[2]!=78||data[3]!=71||data[24]!=8||(data[25]!=2&&data[25]!=6))
                        throw new InvalidDataException("僅接受 RGB/RGBA 8 位 PNG（不含索引色、16 位、EXR 或 DDS）："+files[z]);
                    int headerW=(data[16]<<24)|(data[17]<<16)|(data[18]<<8)|data[19];
                    int headerH=(data[20]<<24)|(data[21]<<16)|(data[22]<<8)|data[23];
                    if(headerW<2||headerH<2||headerW>256||headerH>256)throw new InvalidDataException("每張切片尺寸須為 2–256 像素。");
                    slice=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
                    if(!ImageConversion.LoadImage(slice,data,false))throw new InvalidDataException("PNG 解碼失敗："+files[z]);
                    if(z==0){width=slice.width;height=slice.height;voxels=new Color32[checked(width*height*files.Length)];}
                    if(slice.width!=width||slice.height!=height)throw new InvalidDataException("切片尺寸必須一致。");
                    Array.Copy(slice.GetPixels32(),0,voxels,z*width*height,width*height);
                    UnityEngine.Object.DestroyImmediate(slice);slice=null;
                }
                volume=new Texture3D(width,height,files.Length,TextureFormat.RGBA32,true)
                    {name="CloudNoiseExternal",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear};
                volume.SetPixels32(voxels);volume.Apply(true,false);
                if(existing){EditorUtility.CopySerialized(volume,existing);EditorUtility.SetDirty(existing);UnityEngine.Object.DestroyImmediate(volume);volume=null;}
                else{AssetDatabase.CreateAsset(volume,AssetPath);volume=null;}
                AssetDatabase.SaveAssets();Selection.activeObject=AssetDatabase.LoadAssetAtPath<Texture3D>(AssetPath);
                Debug.Log("ORBIS：已匯入 "+width+"×"+height+"×"+files.Length+" 的線性 R/G 噪聲。下次 Play 自動使用；移除此 asset 可恢復程序噪聲。請檢查切片方向與週期接縫。");
            }
            catch(Exception error){Debug.LogError("ORBIS 噪聲匯入失敗："+error.Message);}
            finally{EditorUtility.ClearProgressBar();if(slice)UnityEngine.Object.DestroyImmediate(slice);if(volume)UnityEngine.Object.DestroyImmediate(volume);}
        }
    }
}
