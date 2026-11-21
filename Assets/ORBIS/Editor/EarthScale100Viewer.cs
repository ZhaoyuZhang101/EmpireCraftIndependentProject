using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.Editor
{
    /// <summary>Read-only metric DEM preview. All mesh and camera coordinates are game metres.</summary>
    public sealed class EarthScale100Viewer : EditorWindow
    {
        const double RealMetresPerGameMetre = 100.0;
        const double GlobeRadius = 6371008.8 / RealMetresPerGameMetre;
        const string ShaderPath = "Assets/ORBIS/Editor/EarthScale100View.shader";
        const string DefaultFolder = "Examples/EarthScale100";

        [Serializable]
        sealed class EarthManifest
        {
            public int globalWidth, globalHeight, localWidth, localHeight;
            public double localCenterLatitude, localCenterLongitude, localSpacingMeters;
            public double realMetresPerGameMetre, earthMeanRadiusMetres;
            public string localName, localFile, globalFile, heightStorage;
        }

        [SerializeField] string folder;
        [SerializeField] bool local;
        [SerializeField] int step = 1;
        EarthManifest info;
        short[] globalBed, localBed;
        Texture2D globalHeight;
        PreviewRenderUtility preview;
        Material material;
        readonly List<Mesh> meshes = new List<Mesh>();
        double longitude, latitude;
        float orbitZoom, localZoom, bearing, tilt;
        Vector2 focus;
        int peak;
        float patchRelief;
        string error;
        bool triedDefault;

        [MenuItem("Tools/ORBIS/1:100 Earth Terrain")]
        public static void Open()
        {
            var window = GetWindow<EarthScale100Viewer>("1:100 地球");
            window.minSize = new Vector2(440, 310);
            window.Show();
        }

        static short[] ReadBed(string root, string relative, int width, int height, int maxSamples)
        {
            long count = (long)width * height;
            if (width < 2 || height < 2 || count > maxSamples || string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
                throw new InvalidDataException("高程图尺寸或路径无效。");
            string path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("高程图路径越出数据目录。");
            if (new FileInfo(path).Length != count * 2)
                throw new InvalidDataException(relative + " 字节数与地图尺寸不符。");
            var bytes = File.ReadAllBytes(path);
            var bed = new short[(int)count];
            if (BitConverter.IsLittleEndian) Buffer.BlockCopy(bytes, 0, bed, 0, bytes.Length);
            else for (int i = 0; i < bed.Length; i++) bed[i] = (short)(bytes[2*i] | bytes[2*i+1] << 8);
            return bed;
        }

        void LoadFolder(string path)
        {
            path = Path.GetFullPath(path);
            var data = JsonUtility.FromJson<EarthManifest>(File.ReadAllText(Path.Combine(path, "manifest.json")));
            if (data == null) throw new InvalidDataException("manifest.json 缺少地球测试数据说明。");
            if (data.realMetresPerGameMetre != RealMetresPerGameMetre)
                throw new InvalidDataException("1:100 地球需要 realMetresPerGameMetre 为 100。");
            if (!(Math.Abs(data.earthMeanRadiusMetres - GlobeRadius*RealMetresPerGameMetre) <= .01))
                throw new InvalidDataException("earthMeanRadiusMetres 需要使用地球平均半径 6371008.8 米。");
            if (data.heightStorage != "int16-le-metres")
                throw new InvalidDataException("heightStorage 需要为 int16-le-metres（小端、有符号、单位现实米）。");
            if (data.globalWidth != 2L * data.globalHeight || data.localSpacingMeters != 100 ||
                !(Math.Abs(data.localCenterLatitude) <= 90 && Math.Abs(data.localCenterLongitude) <= 180))
                throw new InvalidDataException("需要全球 2:1 经纬网格与 100 米局部网格，包含有效的局部中心经纬度。");
            var globe = ReadBed(path, data.globalFile ?? "global/earth-global.i16", data.globalWidth, data.globalHeight, 48000000);
            var patch = ReadBed(path, data.localFile ?? "local/taiwan-100m.i16", data.localWidth, data.localHeight, 4194304);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (!shader || !shader.isSupported) throw new InvalidOperationException("高程预览着色器未能加载。");
            Release();
            folder = path;
            info = data;
            globalBed = globe;
            localBed = patch;
            peak = 0;
            short lowest = patch[0];
            for (int i = 1; i < patch.Length; i++)
            {
                if (patch[i]>patch[peak]) peak=i;
                if (patch[i]<lowest) lowest=patch[i];
            }
            patchRelief=(float)((patch[peak]-lowest)/RealMetresPerGameMetre);
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetVector("_Site",Direction(data.localCenterLongitude,data.localCenterLatitude));
            preview = new PreviewRenderUtility();
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(.066f, .088f, .113f);
            preview.camera.allowHDR = false;
            preview.camera.allowMSAA = false;
            ResetPose();
            try { RebuildMeshes(); }
            catch { Release(); throw; }
            error = null;
        }

        void TryLoad(string path)
        {
            try { LoadFolder(path); }
            catch (Exception e) { error = e.Message; }
            Repaint();
        }

        void ResetPose()
        {
            longitude = info.localCenterLongitude;
            latitude = info.localCenterLatitude;
            orbitZoom = 1;
            localZoom = 1;
            focus = Vector2.zero;
            bearing = 0;
            tilt = 60;
        }

        void SetMode(bool showLocal, int stride)
        {
            if (local == showLocal && step == stride) return;
            local = showLocal;
            step = stride;
            RebuildMeshes();
        }

        void Release()
        {
            ClearMeshes();
            if (material) DestroyImmediate(material);
            if (globalHeight) DestroyImmediate(globalHeight);
            preview?.Cleanup();
            preview = null;
            globalBed = localBed = null;
            info = null;
        }
        void OnDisable() { Release(); triedDefault = false; }
        void ClearMeshes()
        {
            foreach (var mesh in meshes) if (mesh) DestroyImmediate(mesh);
            meshes.Clear();
        }

        static Vector3 Direction(double lon, double lat)
        {
            double phi = lat * Math.PI / 180, theta = lon * Math.PI / 180;
            return new Vector3((float)(Math.Sin(theta)*Math.Cos(phi)), (float)Math.Sin(phi),
                               (float)(-Math.Cos(theta)*Math.Cos(phi)));
        }

        float GlobalElevation(double u, double v)
        {
            // Raster values refer to cell centres; geometry wraps at the date line.
            int width = info.globalWidth, height = info.globalHeight;
            double x = u * width - .5, y = Math.Max(0,Math.Min(height-1,v*height-.5));
            int xi = (int)Math.Floor(x), a = (xi + width) % width, b = (a+1) % width;
            int row = (int)y, next = Math.Min(row+1,height-1);
            float dx = (float)(x - xi);
            return Mathf.Lerp(Mathf.Lerp(globalBed[row*width+a],globalBed[row*width+b],dx),
                              Mathf.Lerp(globalBed[next*width+a],globalBed[next*width+b],dx), (float)(y-row));
        }

        static int[] Triangles(int w, int h)
        {
            var indices = new int[(w-1)*(h-1)*6];
            int n = 0;
            for (int y = 0; y < h-1; y++)
            for (int x = 0; x < w-1; x++)
            {
                int i = y*w+x;
                indices[n++] = i; indices[n++] = i+1; indices[n++] = i+w;
                indices[n++] = i+1; indices[n++] = i+w+1; indices[n++] = i+w;
            }
            return indices;
        }

        static Mesh MakeMesh(Vector3[] vertices, Vector3[] normals, Vector2[] uv, int w, int h)
        {
            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave,
                indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv;
            mesh.triangles = Triangles(w,h);
            mesh.UploadMeshData(true);
            return mesh;
        }

        void BuildGlobe()
        {
            if (!globalHeight)
            {
                if (!SystemInfo.SupportsTextureFormat(TextureFormat.R16) || info.globalWidth > SystemInfo.maxTextureSize)
                    throw new NotSupportedException("当前显卡无法显示此尺寸的 16 位高程纹理。");
                var packed = new ushort[globalBed.Length];
                for (int i = 0; i < packed.Length; i++) packed[i] = (ushort)(globalBed[i]+32768);
                globalHeight = new Texture2D(info.globalWidth,info.globalHeight,TextureFormat.R16,false,true)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear,
                  wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp };
                globalHeight.SetPixelData(packed,0);
                globalHeight.Apply(false,true);
            }
            const int w = 385, h = 193;
            var vertices = new Vector3[w*h]; var normals = new Vector3[w*h]; var uv = new Vector2[w*h];
            double north = 0, south = 0;
            for (int x = 0; x < info.globalWidth; x++)
            {
                north += globalBed[x]; south += globalBed[globalBed.Length-info.globalWidth+x];
            }
            north /= info.globalWidth; south /= info.globalWidth;
            for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                double u = (double)i/(w-1), v = (double)j/(h-1);
                int n = j*w+i;
                Vector3 dir = j==0 ? Vector3.up : j==h-1 ? Vector3.down : Direction((u-.5)*360,90-v*180);
                double metres = j==0 ? north : j==h-1 ? south : GlobalElevation(u,v);
                vertices[n] = dir*(float)(GlobeRadius+metres/RealMetresPerGameMetre);
                normals[n] = dir; uv[n] = new Vector2((float)u,(float)v);
            }
            meshes.Add(MakeMesh(vertices,normals,uv,w,h));
            material.SetTexture("_Height",globalHeight);
        }

        void BuildPatch(int left, int top, int lastX, int lastY)
        {
            int w = (lastX-left+step-1)/step+1, h = (lastY-top+step-1)/step+1;
            var vertices = new Vector3[w*h]; var normals = new Vector3[w*h]; var uv = new Vector2[w*h];
            int width = info.localWidth, height = info.localHeight;
            float spacing = (float)(info.localSpacingMeters / RealMetresPerGameMetre);
            for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                int x = Math.Min(lastX,left+i*step), y = Math.Min(lastY,top+j*step), n = j*w+i;
                int x0 = Math.Max(0,x-step), x1 = Math.Min(width-1,x+step);
                int y0 = Math.Max(0,y-step), y1 = Math.Min(height-1,y+step);
                short z = localBed[y*width+x];
                vertices[n] = new Vector3((x-(width-1)*.5f)*spacing,(float)(z/RealMetresPerGameMetre),
                                          ((height-1)*.5f-y)*spacing);
                // Neighbours refer to the complete DEM, including across chunk edges.
                float dx = (localBed[y*width+x1]-localBed[y*width+x0])/(float)((x1-x0)*info.localSpacingMeters);
                float dy = (localBed[y1*width+x]-localBed[y0*width+x])/(float)((y1-y0)*info.localSpacingMeters);
                normals[n] = new Vector3(-dx,1,dy).normalized;
                uv[n] = new Vector2(z,0);
            }
            meshes.Add(MakeMesh(vertices,normals,uv,w,h));
        }

        void RebuildMeshes()
        {
            ClearMeshes();
            material.SetFloat("_Local",local?1:0);
            if (!local) { BuildGlobe(); return; }
            int cellsPerChunk = 128*step;
            for (int y = 0; y < info.localHeight-1; y += cellsPerChunk)
            for (int x = 0; x < info.localWidth-1; x += cellsPerChunk)
                BuildPatch(x,y,Math.Min(x+cellsPerChunk,info.localWidth-1),Math.Min(y+cellsPerChunk,info.localHeight-1));
        }

        float PatchExtent => (float)(info.localSpacingMeters/RealMetresPerGameMetre)*Math.Max(info.localWidth,info.localHeight);

        void FocusPeak()
        {
            SetMode(true,step);
            focus=new Vector2(peak%info.localWidth-(info.localWidth-1)*.5f,
                               (info.localHeight-1)*.5f-peak/info.localWidth);
            localZoom=Mathf.Clamp(130/PatchExtent,.025f,1);
            Repaint();
        }

        float FocusHeight(float width, float depth)
        {
            float x=Mathf.Clamp(focus.x+width*.5f,0,width), y=Mathf.Clamp(depth*.5f-focus.y,0,depth);
            int x0=(int)x, y0=(int)y, x1=Math.Min(info.localWidth-1,x0+1), y1=Math.Min(info.localHeight-1,y0+1);
            float north=Mathf.Lerp(localBed[y0*info.localWidth+x0],localBed[y0*info.localWidth+x1],x-x0);
            float south=Mathf.Lerp(localBed[y1*info.localWidth+x0],localBed[y1*info.localWidth+x1],x-x0);
            return (float)(Mathf.Lerp(north,south,y-y0)/RealMetresPerGameMetre);
        }

        void ConfigureCamera(float aspect)
        {
            var cam = preview.camera;
            cam.aspect = aspect;
            cam.orthographic = true;
            Vector3 target, direction, up;
            float distance;
            if (local)
            {
                // Fit the default oblique view of the rectangular patch; changing the
                // sampling stride or turning the camera must not change magnification.
                float width=info.localWidth-1, depth=info.localHeight-1;
                float vertical=Mathf.Sin(60*Mathf.Deg2Rad)*depth+patchRelief*Mathf.Cos(60*Mathf.Deg2Rad);
                cam.orthographicSize=.55f*Mathf.Max(width/aspect,vertical)*localZoom;
                target = new Vector3(focus.x,FocusHeight(width,depth),focus.y);
                direction = Quaternion.Euler(tilt,bearing,0)*Vector3.back;
                up = Vector3.up;
                distance = Mathf.Max(PatchExtent*2,500);
            }
            else
            {
                cam.orthographicSize=(float)GlobeRadius*1.07f*orbitZoom/Mathf.Min(1,aspect);
                target = Vector3.zero; direction = Direction(longitude,latitude);
                // The tangent on this meridian also supplies a north-up orientation at a pole.
                up = Direction(longitude,latitude+90); distance = (float)GlobeRadius*2.5f;
            }
            cam.transform.SetPositionAndRotation(target+direction*distance,Quaternion.LookRotation(-direction,up));
            cam.nearClipPlane = Mathf.Max(.1f,distance*.015f);
            cam.farClipPlane = distance*2;
            material.SetVector("_Lamp",local ? new Vector3(-.5f,.66f,.55f).normalized :
                (direction*.82f+cam.transform.up*.48f-cam.transform.right*.35f).normalized);
        }

        Texture Render(Rect rect)
        {
            preview.BeginPreview(rect, GUIStyle.none);
            try
            {
                ConfigureCamera(rect.width/rect.height);
                foreach (var mesh in meshes) preview.DrawMesh(mesh,Matrix4x4.identity,material,0);
                preview.Render();
            }
            catch { preview.EndPreview(); throw; }
            return preview.EndPreview();
        }

        void Input(Rect rect)
        {
            var evt = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            switch (evt.GetTypeForControl(id))
            {
                case EventType.MouseDown when rect.Contains(evt.mousePosition) && (evt.button==0 || evt.button==1):
                    GUIUtility.hotControl = id; evt.Use(); break;
                case EventType.MouseUp when GUIUtility.hotControl==id:
                    GUIUtility.hotControl=0; evt.Use(); break;
                case EventType.MouseDrag when GUIUtility.hotControl==id:
                    if (!local)
                    {
                        longitude -= evt.delta.x*.36*orbitZoom;
                        latitude = Math.Max(-89.9,Math.Min(89.9,latitude+evt.delta.y*.36*orbitZoom));
                    }
                    else if (evt.button==1)
                    {
                        bearing += evt.delta.x*.4f; tilt = Mathf.Clamp(tilt-evt.delta.y*.4f,10,89.9f);
                    }
                    else
                    {
                        float perPixel = 2*preview.camera.orthographicSize/Mathf.Max(1,rect.height);
                        var turn = Quaternion.Euler(0,bearing,0);
                        var move = turn*new Vector3(-evt.delta.x,0,evt.delta.y/Mathf.Sin(tilt*Mathf.Deg2Rad))*perPixel;
                        focus.x = Mathf.Clamp(focus.x+move.x,-info.localWidth*.5f,info.localWidth*.5f);
                        focus.y = Mathf.Clamp(focus.y+move.z,-info.localHeight*.5f,info.localHeight*.5f);
                    }
                    evt.Use(); Repaint(); break;
                case EventType.ScrollWheel when rect.Contains(evt.mousePosition):
                    float scale = Mathf.Exp(evt.delta.y*.09f);
                    if (local) localZoom=Mathf.Clamp(localZoom*scale,2/PatchExtent,2);
                    else orbitZoom=Mathf.Clamp(orbitZoom*scale,.025f,1.6f);
                    evt.Use(); Repaint(); break;
            }
        }

        void OnGUI()
        {
            if (!triedDefault)
            {
                triedDefault=true;
                string start=string.IsNullOrEmpty(folder)?Path.GetFullPath(Path.Combine(Application.dataPath,"..",DefaultFolder)):folder;
                if (File.Exists(Path.Combine(start,"manifest.json"))) TryLoad(start);
            }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("选择数据目录",EditorStyles.toolbarButton))
                {
                    string picked=EditorUtility.OpenFolderPanel("选择含 manifest.json 的 1:100 地球数据目录",folder??"", "");
                    if (picked.Length>0) TryLoad(picked);
                }
                using (new EditorGUI.DisabledScope(info==null))
                {
                    if (GUILayout.Toggle(!local,"地球",EditorStyles.toolbarButton)) SetMode(false,step);
                    if (GUILayout.Toggle(local&&step==1,"局部 100 米",EditorStyles.toolbarButton)) SetMode(true,1);
                    if (GUILayout.Toggle(local&&step==4,"局部 400 米",EditorStyles.toolbarButton)) SetMode(true,4);
                    if (GUILayout.Button("定位最高点",EditorStyles.toolbarButton)) FocusPeak();
                    if (GUILayout.Button("复位",EditorStyles.toolbarButton)) { ResetPose(); Repaint(); }
                }
            }
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error,MessageType.Error);
            if (info==null) { EditorGUILayout.HelpBox("选择含全球高程和局部 100 米网格的数据目录。",MessageType.Info); return; }
            string site=string.IsNullOrEmpty(info.localName)?"局部地形":info.localName;
            EditorGUILayout.LabelField(local ? $"{site}   ·   显示间距 {100*step} 现实米   ·   横纵比例 1:100"
                : $"地球半径 {GlobeRadius:N1} 游戏米   ·   全球底图 {info.globalWidth} × {info.globalHeight}",EditorStyles.boldLabel);
            var rect=GUILayoutUtility.GetRect(10,10,GUILayout.ExpandWidth(true),GUILayout.ExpandHeight(true));
            if (rect.width>=2 && rect.height>=2)
            {
                Input(rect);
                if (Event.current.type==EventType.Repaint) GUI.DrawTexture(rect,Render(rect),ScaleMode.StretchToFill,false);
            }
            EditorGUILayout.LabelField(local ? $"视野宽 {preview.camera.orthographicSize*preview.camera.aspect*.2f:F1} 现实公里    滚轮缩放    左键平移    右键转动"
                : "拖动旋转    滚轮缩放    圆点为局部中心    选择「局部 100 米」查看地形",EditorStyles.miniLabel);
        }
    }
}
