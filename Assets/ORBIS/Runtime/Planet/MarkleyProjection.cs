using System;
using Complex=System.Numerics.Complex;
using UnityEngine;
namespace Orbis.Planet
{
    // Markley rectangular arrangement of Lee's conformal tetrahedral projection.
    // Modified Mapshaper source and license: Tools/Markley/source.
    public static class MarkleyProjection
    {
        public const int TableWidth=1025,TableHeight=445;
        public const double Aspect=2.309401076758503;
        [Serializable] sealed class Face {public double[] matrix,affine;}
        [Serializable] sealed class FaceSet {public Face[] faces;}
        static Face[] faces;static float[] samples;
        public static Texture2D Directions {get;private set;}
        static readonly double[] G={1.15470053837925,.192450089729875,.0481125224324687,.010309826235529,3.34114739114366e-4,-1.50351632601465e-3,-1.2304417796231e-3,-6.75190201960282e-4,-2.84084537293856e-4,-8.21205120500051e-5,-1.59257630018706e-6,1.91691805888369e-5,1.73095888028726e-5,1.03865580818367e-5,4.70614523937179e-6,1.4413500104181e-6,1.92757960170179e-8,-3.82869799649063e-7,-3.57526015225576e-7,-2.2175964844211e-7};
        static readonly double[] H={1,.125,3.0/56,1.0/32,35.0/1664,63.0/4096,231.0/19456};
        public static void Load()
        {
            if(faces!=null&&samples!=null&&Directions)return;
            var f=Resources.Load<TextAsset>("ORBIS/Markley/faces");var data=Resources.Load<TextAsset>("ORBIS/Markley/inverse");
            if(!f||!data)throw new InvalidOperationException("Missing Markley projection tables.");
            faces=JsonUtility.FromJson<FaceSet>(f.text).faces;byte[] bytes=data.bytes;
            if(faces==null||faces.Length!=4||bytes.Length!=TableWidth*TableHeight*16)throw new InvalidOperationException("Invalid Markley projection tables.");
            if(!BitConverter.IsLittleEndian)throw new PlatformNotSupportedException("Little-endian floats required.");
            samples=new float[bytes.Length/4];Buffer.BlockCopy(bytes,0,samples,0,bytes.Length);
            Directions=new Texture2D(TableWidth,TableHeight,TextureFormat.RGBAFloat,false,true){name="Markley direction table",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            Directions.LoadRawTextureData(bytes);Directions.Apply(false,true);Resources.UnloadAsset(f);Resources.UnloadAsset(data);
        }
        public static void Bind(Material m){Load();m.SetTexture("_MarkleyDirections",Directions);m.SetVector("_MarkleyTableSize",new Vector4(TableWidth,TableHeight,1f/TableWidth,1f/TableHeight));}
        static Complex Lee(Complex z)
        {
            double n=z.Magnitude;Complex h=Complex.Zero,k=Complex.Zero,rot=Complex.One;
            var w=new Complex(-.5,Math.Sqrt(3)/2);double best=z.Real;
            for(int i=1;i<3;i++){var r=i==1?w:w*w;double real=(r*z).Real;if(real>best){best=real;rot=r;}}
            if(n>.3){Complex y=Complex.One-rot*z,g=Complex.Zero;for(int i=G.Length-1;i>=0;i--)g=G[i]+g*y;k=(1.4021821053254548-Complex.Sqrt(y)*g)*rot*rot;}
            if(n<.5){Complex z3=z*z*z;for(int i=H.Length-1;i>=0;i--)h=H[i]+h*z3;h*=z;}
            if(n<.3)return h;if(n>.5)return k;double t=(n-.3)/.2;return k*t+h*(1-t);
        }
        public static Vector2 Project(PlanetLocation location)
        {
            Load();var n=PlanetCoordinateSystem.SurfaceNormal(location);Face face=faces[0];double best=double.NegativeInfinity;
            foreach(var f in faces){var m=f.matrix;double dot=m[0]*n.X+m[1]*n.Y+m[2]*n.Z;if(dot>best){best=dot;face=f;}}
            var a=face.matrix;double localY=a[3]*n.X+a[4]*n.Y+a[5]*n.Z,localZ=a[6]*n.X+a[7]*n.Y+a[8]*n.Z;
            var q=Lee(new Complex(localY,localZ)*(Math.Sqrt(2)/(1+best)));a=face.affine;
            double x=a[0]*q.Real+a[1]*q.Imaginary+a[2],y=a[3]*q.Real+a[4]*q.Imaginary+a[5];
            if(y>=0){if(x>3)x-=8;y=-y;}else x=x>1?4-x:-4-x;
            double u=PlanetCoordinateSystem.Wrap(x+5,8)/8,v=(y+2*Math.Sqrt(3))/(2*Math.Sqrt(3));
            return new Vector2((float)u,(float)Math.Max(0,Math.Min(1,v)));
        }
        public static Vector2 Fold(double u,double v)
        {
            double row=Math.Floor(v),k=Math.Floor(row/2),local=v-row;bool odd=row-2*k>=1;
            return new Vector2((float)PlanetCoordinateSystem.Wrap(odd?.75-u-k*.5:u+k*.5,1),(float)(odd?1-local:local));
        }
        public static PlanetVector Direction(double u,double v)
        {
            Load();var p=Fold(u,v);double x=p.x*(TableWidth-1),y=p.y*(TableHeight-1);
            int ix=Math.Min(TableWidth-2,(int)x),iy=Math.Min(TableHeight-2,(int)y);double tx=x-ix,ty=y-iy;
            int a=(iy*TableWidth+ix)*4,b=a+TableWidth*4;
            var n=new PlanetVector(Mix(a,b,0,tx,ty),Mix(a,b,1,tx,ty),Mix(a,b,2,tx,ty));return n*(1/Math.Sqrt(PlanetVector.Dot(n,n)));
        }
        static double Mix(int a,int b,int c,double x,double y){return (samples[a+c]*(1-x)+samples[a+4+c]*x)*(1-y)+(samples[b+c]*(1-x)+samples[b+4+c]*x)*y;}
        public static PlanetLocation Unproject(double u,double v){var n=Direction(u,v);return new PlanetLocation(Math.Atan2(n.X,n.Z),Math.Asin(Math.Max(-1,Math.Min(1,n.Y))));}
        public static Vector2 GeoUV(double u,double v){var p=Unproject(u,v);return new Vector2((float)PlanetCoordinateSystem.MapU(p),(float)PlanetCoordinateSystem.MapV(p));}
        public static Vector2 Nearest(Vector2 point,Vector2 anchor)
        {
            Vector2 result=point;float best=float.PositiveInfinity;
            for(int row=-3;row<=3;row++){
                int k=(int)Math.Floor(row/2.0);bool odd=row-2*k==1;
                double x=odd?.75-point.x-k*.5:point.x-k*.5,y=odd?row+1-point.y:row+point.y;
                x=anchor.x+PlanetCoordinateSystem.NearestOffset(x-anchor.x,1);
                float distance=(float)((x-anchor.x)*(x-anchor.x)+(y-anchor.y)*(y-anchor.y)/(Aspect*Aspect));
                if(distance<best){best=distance;result=new Vector2((float)x,(float)y);}
            }return result;
        }
    }
    public struct MarkleyMapPosition
    {
        public double X,Y;
        public void Set(double x,double y,int width,int height){X=x;Y=y;Normalize(width,height);}
        public void Move(double x,double y,int width,int height){X+=x;Y+=y;Normalize(width,height);}
        public void Normalize(int width,int height){double h=width/MarkleyProjection.Aspect,cycles=Math.Floor(Y/(2*h));Y-=cycles*2*h;X=PlanetCoordinateSystem.Wrap(X+cycles*.5*width,width);}
    }
}
