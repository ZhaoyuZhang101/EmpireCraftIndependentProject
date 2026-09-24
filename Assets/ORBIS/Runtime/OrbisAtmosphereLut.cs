using System;
using System.IO;
using UnityEngine;
namespace Orbis
{
    /// <summary>Sun-column density LUT; no sunlight/weather dependence, no per-frame baking.
    /// The shipped default bake uses the identical 2x64 midpoint quadrature below.</summary>
    public static class OrbisAtmosphereLut
    {
        public const int Width=192,Height=96;
        static Texture2D texture;
        static float lastRay,lastMie;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            if(texture)UnityEngine.Object.Destroy(texture);
            texture=null;lastRay=lastMie=0;
        }
        public static Texture2D Get(float rayHeight,float mieHeight)
        {
            if(texture&&Mathf.Abs(lastRay-rayHeight)<1e-7f&&Mathf.Abs(lastMie-mieHeight)<1e-7f)return texture;
            var pixels=new Color[Width*Height];bool loaded=false;
            if(Mathf.Abs(rayHeight-.022f)<1e-7f&&Mathf.Abs(mieHeight-.0064f)<1e-7f)
            {
                TextAsset blob=Resources.Load<TextAsset>("ORBIS/AtmosphereOpticalDepthV42");
                if(blob)
                {
                    try
                    {
                        using(var reader=new BinaryReader(new MemoryStream(blob.bytes)))
                        {
                            if(reader.ReadInt32()!=0x3234444f||reader.ReadUInt16()!=Width||reader.ReadUInt16()!=Height)
                                throw new InvalidDataException("Invalid V4-2 optical LUT header.");
                            float hr=reader.ReadSingle(),hm=reader.ReadSingle();
                            if(Math.Abs(hr-rayHeight)>1e-7||Math.Abs(hm-mieHeight)>1e-7)throw new InvalidDataException("Scale height mismatch.");
                            for(int i=0;i<pixels.Length;i++)pixels[i]=new Color(reader.ReadSingle(),reader.ReadSingle(),0,1);
                            loaded=true;
                        }
                    }
                    catch(Exception e){Debug.LogWarning("ORBIS optical LUT rebake: "+e.Message);}
                }
            }
            if(!loaded)
                for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)
                {
                    double v=y/(double)(Height-1),u=x/(double)(Width-1),r=1+.2*v*v;
                    double horizon=-Math.Sqrt(Math.Max(0,1-1/(r*r))),mu=horizon+(1-horizon)*u*u;
                    pixels[y*Width+x]=Column(r,mu,rayHeight,mieHeight);
                }
            if(texture)UnityEngine.Object.Destroy(texture);
            var format=SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf)?TextureFormat.RGBAHalf:TextureFormat.RGBAFloat;
            texture=new Texture2D(Width,Height,format,false,true)
                {name="V4-2 GLtracy optical columns",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear,hideFlags=HideFlags.HideAndDontSave};
            texture.SetPixels(pixels);texture.Apply(false,true);lastRay=rayHeight;lastMie=mieHeight;return texture;
        }
        public static Color Column(double r,double mu,double hr,double hm)
        {
            double b=r*mu,end=Math.Max(0,-b+Math.Sqrt(Math.Max(0,b*b-r*r+1.44)));
            double closest=Math.Max(0,Math.Min(end,-b)),ray=0,mie=0;
            for(int side=0;side<2;side++)
            {
                double span=side==0?closest:end-closest;
                for(int j=0;j<64;j++)
                {
                    double a=span*j*j/4096.0,z=span*(j+1)*(j+1)/4096.0;
                    double t=closest+(side==0?-1:1)*(a+z)*.5;
                    double h=Math.Max(0,Math.Sqrt(Math.Max(0,r*r+2*b*t+t*t))-1);
                    ray+=Math.Exp(-h/hr)*(z-a);mie+=Math.Exp(-h/hm)*(z-a);
                }
            }
            return new Color((float)ray,(float)mie,0,1);
        }
    }
}
