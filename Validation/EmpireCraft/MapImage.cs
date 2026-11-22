using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using EmpireCraft.Core;
using Orbis.Flat;

// Offline territory picture: relief-shaded ground, realm colours, borders, city seats.
public static class MapImage
{
    public static void Write(EmpireWorld w,string path)
    {
        var m=w.Map;int width=m.Width,height=m.Height;var rgb=new byte[width*height*3];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            int i=y*width+x;float r,g,b;
            if(FlatTerrainAnalysis.AreaWater(m.Water[i])&&m.Water[i]!=WaterKind.River){float d=Math.Min(1,-m.Elevation[i]/4000f);r=.10f;g=.24f-.1f*d;b=.42f-.12f*d;if(m.Water[i]==WaterKind.Lake){r=.22f;g=.42f;b=.62f;}}
            else
            {
                float e=m.Elevation[i]/3500f;r=.45f+.3f*e;g=.52f+.18f*e;b=.36f+.35f*e;
                int east=m.Index(x+1,y),north=m.Index(x,Math.Min(height-1,y+1));
                float shade=1-(m.Elevation[east]-m.Elevation[i]-(m.Elevation[north]-m.Elevation[i]))/900f;shade=Math.Clamp(shade,.6f,1.3f);r*=shade;g*=shade;b*=shade;
                int realm=w.RealmOfTile(i);
                if(realm>=0)
                {
                    uint c=w.Realms[realm].Color;float cr=(c>>16&255)/255f,cg=(c>>8&255)/255f,cb=(c&255)/255f;
                    bool border=false;for(int d=0;d<4;d++){int n=m.Neighbor(i,d);if(n>=0&&w.RealmOfTile(n)!=realm)border=true;}
                    float k=border?.85f:.5f;r=r*(1-k)+cr*k;g=g*(1-k)+cg*k;b=b*(1-k)+cb*k;if(border){r*=.6f;g*=.6f;b*=.6f;}
                }
                if(m.Water[i]==WaterKind.River){r=r*.5f+.1f;g=g*.5f+.2f;b=b*.5f+.4f;}
            }
            int o=((height-1-y)*width+x)*3;rgb[o]=(byte)(Math.Clamp(r,0,1)*255);rgb[o+1]=(byte)(Math.Clamp(g,0,1)*255);rgb[o+2]=(byte)(Math.Clamp(b,0,1)*255);
        }
        foreach(var c in w.Cities.Where(c=>c.Alive))
        {
            bool capital=w.Realms[c.Realm].Capital==c.Id;int x=c.Tile%width,y=c.Tile/width,s=capital?1:0;
            for(int dy=-s;dy<=s;dy++)for(int dx=-s;dx<=s;dx++)
            {
                int px=((x+dx)%width+width)%width,py=height-1-Math.Clamp(y+dy,0,height-1);int o=(py*width+px)*3;
                rgb[o]=(byte)(capital?255:250);rgb[o+1]=(byte)(capital?230:250);rgb[o+2]=(byte)(capital?60:250);
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        File.WriteAllBytes(path,Png(width,height,rgb));
    }

    static byte[] Png(int width,int height,byte[] rgb)
    {
        using var ms=new MemoryStream();
        void Chunk(string type,byte[] data)
        {
            var t=System.Text.Encoding.ASCII.GetBytes(type);var len=BitConverter.GetBytes(data.Length);Array.Reverse(len);ms.Write(len);
            var body=t.Concat(data).ToArray();ms.Write(body);var crc=BitConverter.GetBytes(Crc(body));Array.Reverse(crc);ms.Write(crc);
        }
        ms.Write(new byte[]{137,80,78,71,13,10,26,10});
        var header=new byte[13];void Be(int at,int v){header[at]=(byte)(v>>24);header[at+1]=(byte)(v>>16);header[at+2]=(byte)(v>>8);header[at+3]=(byte)v;}
        Be(0,width);Be(4,height);header[8]=8;header[9]=2;Chunk("IHDR",header);
        using var raw=new MemoryStream();
        using(var z=new ZLibStream(raw,CompressionLevel.Fastest,true))for(int y=0;y<height;y++){z.WriteByte(0);z.Write(rgb,y*width*3,width*3);}
        Chunk("IDAT",raw.ToArray());Chunk("IEND",Array.Empty<byte>());return ms.ToArray();
    }
    static uint[] table;
    static uint Crc(byte[] data)
    {
        if(table==null){table=new uint[256];for(uint n=0;n<256;n++){uint c=n;for(int k=0;k<8;k++)c=(c&1)!=0?0xedb88320u^(c>>1):c>>1;table[n]=c;}}
        uint r=0xffffffffu;foreach(byte b in data)r=table[(r^b)&255]^(r>>8);return r^0xffffffffu;
    }
}
