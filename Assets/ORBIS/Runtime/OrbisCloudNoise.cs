using UnityEngine;

namespace Orbis
{
    /// <summary>Small periodic volume: low-frequency value/Worley blend + erosion channel.
    /// The volume is immutable; flow changes sampling coordinates, never texels.</summary>
    public static class OrbisCloudNoise
    {
        static float Hash(int x,int y,int z,int period,uint seed)
        {
            unchecked
            {
                x=(x%period+period)%period;y=(y%period+period)%period;z=(z%period+period)%period;
                uint h=(uint)x*374761393u+(uint)y*668265263u+(uint)z*2147483647u+seed;
                h=(h^(h>>13))*1274126177u;h^=h>>16;return (h&0xffffffu)/16777215f;
            }
        }
        static float Value(Vector3 p,int period,uint seed)
        {
            int x=Mathf.FloorToInt(p.x),y=Mathf.FloorToInt(p.y),z=Mathf.FloorToInt(p.z);
            Vector3 t=p-new Vector3(x,y,z);t=new Vector3(t.x*t.x*(3-2*t.x),t.y*t.y*(3-2*t.y),t.z*t.z*(3-2*t.z));float v=0;
            for(int k=0;k<2;k++)for(int j=0;j<2;j++)for(int i=0;i<2;i++)v+=Hash(x+i,y+j,z+k,period,seed)*(i==0?1-t.x:t.x)*(j==0?1-t.y:t.y)*(k==0?1-t.z:t.z);
            return v;
        }
        static float Worley(Vector3 p,int period,uint seed)
        {
            int x=Mathf.FloorToInt(p.x),y=Mathf.FloorToInt(p.y),z=Mathf.FloorToInt(p.z);float best=3;
            for(int k=-1;k<=1;k++)for(int j=-1;j<=1;j++)for(int i=-1;i<=1;i++)
            {
                int a=x+i,b=y+j,c=z+k;
                Vector3 feature=new Vector3(a+Hash(a,b,c,period,seed),b+Hash(a,b,c,period,seed+101),c+Hash(a,b,c,period,seed+307));
                best=Mathf.Min(best,(feature-p).sqrMagnitude);
            }
            return 1-Mathf.Clamp01(Mathf.Sqrt(best));
        }
        // Offline bake: 128^3 shape + independent 64^3 detail, as in the
        // supplied planetary demo. Runtime uploads bytes instead of blocking on noise.
        public static byte[] BakeShapeBytes()
        {
            const int size=128;var bytes=new byte[size*size*size*4];
            System.Threading.Tasks.Parallel.For(0,size,Flat.FlatWorkerPolicy.Options(default),z=>
            {
                for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                {
                    var p=(new Vector3(x,y,z)+Vector3.one*.5f)/size;
                    float perlin=Value(p*4,4,19871)*.57f+Value(p*8,8,19872)*.29f+Value(p*16,16,19873)*.14f;
                    float worley=Worley(p*4,4,37841),fine=Worley(p*8,8,2781);
                    Color32 c=new Color(Mathf.Clamp01(perlin*.72f+worley*.28f),worley,fine,Value(p*16,16,9971));
                    int i=((z*size+y)*size+x)*4;bytes[i]=c.r;bytes[i+1]=c.g;bytes[i+2]=c.b;bytes[i+3]=c.a;
                }
            });return bytes;
        }
        public static Texture3D Create()
        {
            TextAsset baked=Resources.Load<TextAsset>("ORBIS/PlanetShape128");
            int bakedSize=128;
            if(!baked){baked=Resources.Load<TextAsset>("ORBIS/CloudNoiseV42");bakedSize=64;}
            if(baked)
            {
                byte[] bytes=baked.bytes;
                if(bytes.Length==bakedSize*bakedSize*bakedSize*4)
                {
                    var voxels=new Color32[bytes.Length/4];
                    for(int i=0;i<voxels.Length;i++){int j=i*4;voxels[i]=new Color32(bytes[j],bytes[j+1],bytes[j+2],bytes[j+3]);}
                    var result=new Texture3D(bakedSize,bakedSize,bakedSize,TextureFormat.RGBA32,true)
                        {name="ORBIS planetary shape "+bakedSize+" cubed",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear};
                    result.SetPixels32(voxels);result.Apply(true,true);return result;
                }
                Debug.LogWarning("ORBIS V4-2 cloud noise size invalid; falling back to procedural noise.");
            }
            const int size=32;var data=new Color32[size*size*size];int n=0;
            for(int z=0;z<size;z++)for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                Vector3 uv=(new Vector3(x,y,z)+Vector3.one*.5f)/size;
                float body=Value(uv*4,4,19871)*.72f+Worley(uv*4,4,37841)*.28f;
                float detail=Worley(uv*8,8,2781)*.65f+Value(uv*12,12,8217)*.35f;
                data[n++]=new Color(body,detail,Value(uv*16,16,9971),1);
            }
            var texture=new Texture3D(size,size,size,TextureFormat.RGBA32,false){name="ORBIS periodic cloud volume",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Bilinear};
            texture.SetPixels32(data);texture.Apply(false,true);return texture;
        }
        // The reference demo bakes independent 3D shape and detail volumes.
        // Keep ORBIS's HP erosion separate from the 64^3 shape cache as well:
        // repeating the same shape at a higher frequency caused large flat slabs.
        static readonly System.Lazy<Color32[]> DetailData=new System.Lazy<Color32[]>(BakeDetail);
        static Color32[] BakeDetail()
        {
            const int size=64;var data=new Color32[size*size*size];
            System.Threading.Tasks.Parallel.For(0,size,Flat.FlatWorkerPolicy.Options(default),z=>
            {
                for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                {
                    Vector3 uv=(new Vector3(x,y,z)+Vector3.one*.5f)/size;
                    float a=Worley(uv*4,4,5261),b=Worley(uv*8,8,7219);
                    float c=Value(uv*16,16,11939),d=Value(uv*32,32,18313);
                    data[(z*size+y)*size+x]=new Color(a,b,c,d);
                }
            });
            return data;
        }
        public static Texture3D CreateDetail()
        {
            const int size=64;var data=DetailData.Value;
            var texture=new Texture3D(size,size,size,TextureFormat.RGBA32,true)
            {name="ORBIS HP independent detail volume",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear};
            texture.SetPixels32(data);texture.Apply(true,true);return texture;
        }
        // HP's reading package omits its high-cloud cell/warp/wisp assets. Generate
        // a spherical 2D substitute with no longitude seam or UniStorm dependency.
        public static Texture2D CreateHighDetail()
        {
            const int width=256,height=128;var data=new Color32[width*height];
            System.Threading.Tasks.Parallel.For(0,height,Flat.FlatWorkerPolicy.Options(default),y=>
            {
                float lat=((y+.5f)/height-.5f)*Mathf.PI;
                for(int x=0;x<width;x++)
                {
                    float lon=((x+.5f)/width-.5f)*Mathf.PI*2;
                    var p=new Vector3(Mathf.Sin(lon)*Mathf.Cos(lat),Mathf.Sin(lat),Mathf.Cos(lon)*Mathf.Cos(lat));
                    data[y*width+x]=new Color(Mathf.Clamp01(Worley(p*14,64,7411)*1.7f),Value(p*40,128,2917),Value(p*7,32,9751),Value(p*7,32,3187));
                }
            });
            var result=new Texture2D(width,height,TextureFormat.RGBA32,true,true){name="HP spherical cells / wisps / warp",wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear};
            result.SetPixels32(data);result.Apply(true,true);return result;
        }
    }
}
