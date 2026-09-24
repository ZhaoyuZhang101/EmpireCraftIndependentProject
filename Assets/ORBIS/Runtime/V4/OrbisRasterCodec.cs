using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;

namespace Orbis
{
    /// <summary>Bounded, CPU-only lossless raster codec. Rows are NORTH first. No sRGB conversion.
    /// PNG: non-interlaced grayscale/RGB/RGBA, gray+alpha, 8/16 bit; palette PNG at 8 bit.
    /// BMP: uncompressed 8-bit indexed, 24/32-bit. Deliberately rejects RLE/interlaced input.</summary>
    public static class OrbisRasterCodec
    {
        public const int MaxPixels = 2097152, MaxBytes = 32 * 1024 * 1024;
        public sealed class Raster
        {
            public int width, height;
            public ushort[] gray;
            public byte[] rgba;
        }
        static readonly byte[] Signature = {137,80,78,71,13,10,26,10};
        static uint BE(byte[] b,int p)=>((uint)b[p]<<24)|((uint)b[p+1]<<16)|((uint)b[p+2]<<8)|b[p+3];
        static void PutBE(Stream s,uint v){s.WriteByte((byte)(v>>24));s.WriteByte((byte)(v>>16));s.WriteByte((byte)(v>>8));s.WriteByte((byte)v);}
        static uint CRC(byte[] b,int start,int count){uint c=0xffffffff;for(int i=start;i<start+count;i++){c^=b[i];for(int k=0;k<8;k++)c=(c>>1)^((c&1)!=0?0xedb88320u:0);}return c^0xffffffff;}
        static uint Adler(byte[] bytes){uint a=1,b=0;foreach(byte v in bytes){a=(a+v)%65521;b=(b+a)%65521;}return(b<<16)|a;}
        static void Size(int w,int h){if(w<2||h<2||w>8192||h>4096||(long)w*h>MaxPixels)throw new InvalidDataException("地图尺寸无效：至少 2×2，最多 2,097,152 像素。");}
        public static Raster Read(byte[] bytes)
        {
            if(bytes==null||bytes.Length<32||bytes.Length>MaxBytes)throw new InvalidDataException("地图文件为空、损坏或超过 32 MiB。");
            if(bytes[0]=='B'&&bytes[1]=='M')return ReadBMP(bytes);
            for(int i=0;i<8;i++)if(bytes[i]!=Signature[i])throw new InvalidDataException("只支持 PNG 或未压缩 BMP；请勿使用 JPEG 高度图。");
            int w=0,h=0,depth=0,type=-1;byte[] palette=null,alpha=null;bool ended=false;
            using(var packed=new MemoryStream())
            {
                for(int p=8;p+12<=bytes.Length;)
                {
                    uint length=BE(bytes,p);if(length>MaxBytes||(long)p+12+length>bytes.Length)throw new InvalidDataException("PNG 数据块长度无效。");
                    int n=(int)length;string tag=System.Text.Encoding.ASCII.GetString(bytes,p+4,4);
                    if(CRC(bytes,p+4,n+4)!=BE(bytes,p+8+n))throw new InvalidDataException("PNG CRC 校验失败。");
                    if(tag=="IHDR")
                    {
                        if(n!=13||w!=0)throw new InvalidDataException("PNG 头无效。");w=(int)BE(bytes,p+8);h=(int)BE(bytes,p+12);Size(w,h);
                        depth=bytes[p+16];type=bytes[p+17];
                        if((depth!=8&&depth!=16)||!(type==0||type==2||type==3||type==4||type==6)||bytes[p+18]!=0||bytes[p+19]!=0||bytes[p+20]!=0||(type==3&&depth!=8))
                            throw new InvalidDataException("请导出非交错 8/16 位 PNG（索引色须 8 位）。");
                    }
                    else if(tag=="IDAT")packed.Write(bytes,p+8,n);
                    else if(tag=="PLTE"){palette=new byte[n];Buffer.BlockCopy(bytes,p+8,palette,0,n);}
                    else if(tag=="tRNS"){alpha=new byte[n];Buffer.BlockCopy(bytes,p+8,alpha,0,n);}
                    else if(tag=="IEND"){ended=true;break;}
                    p+=n+12;
                }
                if(!ended||w==0)throw new InvalidDataException("PNG 缺少必要数据块。");
                int channels=type==0||type==3?1:type==2?3:type==4?2:4,bpp=channels*(depth/8),stride=w*bpp;
                byte[] z=packed.ToArray();if(z.Length<6||(z[0]&15)!=8||((z[0]<<8)+z[1])%31!=0||(z[1]&32)!=0)throw new InvalidDataException("PNG zlib 头无效。");
                byte[] raw=new byte[(stride+1)*h];
                using(var compressed=new MemoryStream(z,2,z.Length-6))using(var deflate=new DeflateStream(compressed,CompressionMode.Decompress))
                {int at=0;while(at<raw.Length){int n=deflate.Read(raw,at,raw.Length-at);if(n==0)throw new InvalidDataException("PNG 数据截断。");at+=n;}if(deflate.ReadByte()!=-1)throw new InvalidDataException("PNG 解压长度超过尺寸。");}
                if(Adler(raw)!=BE(z,z.Length-4))throw new InvalidDataException("PNG Adler 校验失败。");
                byte[] pixels=new byte[stride*h];
                for(int y=0;y<h;y++)
                {
                    int filter=raw[y*(stride+1)];if(filter>4)throw new InvalidDataException("PNG 行过滤器无效。");
                    for(int x=0;x<stride;x++)
                    {int i=y*stride+x,a=x>=bpp?pixels[i-bpp]:0,b=y>0?pixels[i-stride]:0,c=y>0&&x>=bpp?pixels[i-stride-bpp]:0;
                        int v=filter==0?0:filter==1?a:filter==2?b:filter==3?(a+b)/2:Paeth(a,b,c);pixels[i]=unchecked((byte)(raw[y*(stride+1)+x+1]+v));}
                }
                var result=new Raster{width=w,height=h,gray=new ushort[w*h],rgba=new byte[w*h*4]};
                for(int i=0;i<w*h;i++)
                {
                    int p=i*bpp;ushort r,g,b,a=65535;
                    if(type==3)
                    {int index=pixels[p];if(palette==null||index*3+2>=palette.Length)throw new InvalidDataException("PNG 调色板无效。");r=(ushort)(palette[index*3]*257);g=(ushort)(palette[index*3+1]*257);b=(ushort)(palette[index*3+2]*257);if(alpha!=null&&index<alpha.Length)a=(ushort)(alpha[index]*257);}
                    else
                    {r=Channel(pixels,p,depth);g=b=r;int step=depth/8;if(type==2||type==6){g=Channel(pixels,p+step,depth);b=Channel(pixels,p+step*2,depth);}if(type==4)a=Channel(pixels,p+step,depth);if(type==6)a=Channel(pixels,p+step*3,depth);}
                    // Height must actually be grayscale, rather than silently interpreting color as elevation.
                    result.gray[i]=(ushort)((r+g+b)/3);result.rgba[i*4]=(byte)(r>>8);result.rgba[i*4+1]=(byte)(g>>8);result.rgba[i*4+2]=(byte)(b>>8);result.rgba[i*4+3]=(byte)(a>>8);
                }
                return result;
            }
        }
        static ushort Channel(byte[] p,int i,int depth)=>(ushort)(depth==16?(p[i]<<8)|p[i+1]:p[i]*257);
        static int Paeth(int a,int b,int c){int p=a+b-c,pa=Math.Abs(p-a),pb=Math.Abs(p-b),pc=Math.Abs(p-c);return pa<=pb&&pa<=pc?a:pb<=pc?b:c;}
        static Raster ReadBMP(byte[] bytes)
        {
            using(var reader=new BinaryReader(new MemoryStream(bytes)))
            {
                reader.BaseStream.Position=10;int offset=reader.ReadInt32(),header=reader.ReadInt32();if(header<40||header>124)throw new InvalidDataException("不支持此 BMP 头。");
                int w=reader.ReadInt32(),signedH=reader.ReadInt32();if(signedH==int.MinValue)throw new InvalidDataException("BMP 尺寸错误。");int h=Math.Abs(signedH);Size(w,h);
                int planes=reader.ReadUInt16(),bits=reader.ReadUInt16(),compression=reader.ReadInt32();
                if(planes!=1||compression!=0||!(bits==8||bits==24||bits==32))throw new InvalidDataException("BMP 须为未压缩 8 位索引色、24 或 32 位 RGB。");
                int stride=((w*bits+31)/32)*4;if(offset<14+header||(long)offset+stride*h>bytes.Length)throw new InvalidDataException("BMP 像素长度无效。");
                var r=new Raster{width=w,height=h,gray=new ushort[w*h],rgba=new byte[w*h*4]};
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)
                {int source=offset+(signedH>0?h-1-y:y)*stride+x*(bits/8),p=source,i=(y*w+x)*4;
                    if(bits==8){p=14+header+bytes[source]*4;if(p+3>=offset)throw new InvalidDataException("BMP 调色板缺少颜色。");}
                    r.rgba[i]=bytes[p+2];r.rgba[i+1]=bytes[p+1];r.rgba[i+2]=bytes[p];r.rgba[i+3]=255;r.gray[y*w+x]=(ushort)((bytes[p]+bytes[p+1]+bytes[p+2])*257/3);}
                return r;
            }
        }
        static void Chunk(Stream output,string name,byte[] payload)
        {byte[] tag=System.Text.Encoding.ASCII.GetBytes(name),crc=new byte[4+payload.Length];Buffer.BlockCopy(tag,0,crc,0,4);Buffer.BlockCopy(payload,0,crc,4,payload.Length);PutBE(output,(uint)payload.Length);output.Write(crc,0,crc.Length);PutBE(output,CRC(crc,0,crc.Length));}
        static byte[] PNG(int w,int h,int depth,int type,byte[] raw)
        {
            Size(w,h);using(var output=new MemoryStream())
            {output.Write(Signature,0,8);using(var head=new MemoryStream()){PutBE(head,(uint)w);PutBE(head,(uint)h);head.WriteByte((byte)depth);head.WriteByte((byte)type);head.Write(new byte[3],0,3);Chunk(output,"IHDR",head.ToArray());}
                using(var zipped=new MemoryStream())
                {zipped.WriteByte(0x78);zipped.WriteByte(0x9c);using(var d=new DeflateStream(zipped,CompressionLevel.Optimal,true))d.Write(raw,0,raw.Length);PutBE(zipped,Adler(raw));Chunk(output,"IDAT",zipped.ToArray());}
                Chunk(output,"IEND",new byte[0]);return output.ToArray();}
        }
        public static byte[] Gray16(int w,int h,ushort[] gray)
        {Size(w,h);if(gray==null||gray.Length!=w*h)throw new ArgumentException("Height array size mismatch.");int stride=w*2+1;byte[] raw=new byte[stride*h];for(int y=0;y<h;y++)for(int x=0;x<w;x++){ushort v=gray[y*w+x];raw[y*stride+x*2+1]=(byte)(v>>8);raw[y*stride+x*2+2]=(byte)v;}return PNG(w,h,16,0,raw);}
        public static byte[] RGBA8(int w,int h,byte[] rgba)
        {Size(w,h);if(rgba==null||rgba.Length!=w*h*4)throw new ArgumentException("River array size mismatch.");int stride=w*4+1;byte[] raw=new byte[stride*h];for(int y=0;y<h;y++)Buffer.BlockCopy(rgba,y*w*4,raw,y*stride+1,w*4);return PNG(w,h,8,6,raw);}
    }
}
