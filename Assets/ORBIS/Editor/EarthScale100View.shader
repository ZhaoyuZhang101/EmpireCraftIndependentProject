Shader "Hidden/ORBIS/EarthScale100View"
{
    Properties { _Height ("Signed metre raster, biased by 32768", 2D) = "black" {} }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _Height;
            float4 _Height_TexelSize;
            float _Local;
            float3 _Lamp, _Site;

            struct Inputs { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct Interpolants { float4 pos : SV_POSITION; float3 normal : TEXCOORD0; float2 uv : TEXCOORD1; };
            Interpolants vert(Inputs v)
            {
                Interpolants o;
                o.pos=UnityObjectToClipPos(v.vertex);
                o.normal=UnityObjectToWorldNormal(v.normal);
                o.uv=v.uv;
                return o;
            }
            float HeightAt(float2 uv) { return tex2D(_Height,uv).r*65535-32768; }
            float3 TerrainTint(float h)
            {
                if(h<0)
                {
                    float3 shelf=lerp(float3(.24,.64,.67),float3(.12,.42,.53),saturate(-h/220));
                    float3 basin=lerp(float3(.105,.31,.45),float3(.060,.16,.29),saturate((-h-1200)/4200));
                    return lerp(shelf,basin,saturate((-h-220)/980));
                }
                float3 low=lerp(float3(.42,.63,.37),float3(.54,.62,.38),saturate(h/450));
                float3 upland=lerp(float3(.57,.52,.37),float3(.67,.61,.53),saturate((h-1100)/1900));
                float3 rock=lerp(low,upland,saturate((h-450)/850));
                return lerp(rock,float3(.94,.94,.89),smoothstep(2800,5000,h));
            }
            float4 frag(Interpolants i) : SV_Target
            {
                float h=i.uv.x;
                float3 n=normalize(i.normal);
                float light, marker=0, outline=0;
                if(_Local>.5)
                {
                    light=.52+.60*saturate(dot(n,_Lamp));
                }
                else
                {
                    float px=max(1e-6,max(length(ddx(n)),length(ddy(n))));
                    float markerRadius=length(n-_Site)/px;
                    marker=1-smoothstep(.6,1.4,abs(markerRadius-4));
                    outline=1-smoothstep(1.3,2.2,abs(markerRadius-4));
                    h=HeightAt(i.uv);
                    float dLon=_Height_TexelSize.x, dLat=_Height_TexelSize.y;
                    float ew=(HeightAt(i.uv+float2(dLon,0))-HeightAt(i.uv-float2(dLon,0))) /
                             (2*40030229*dLon*max(.015,abs(sin(UNITY_PI*i.uv.y))));
                    float ns=(HeightAt(i.uv-float2(0,dLat))-HeightAt(i.uv+float2(0,dLat))) /
                             (2*20015114*dLat);
                    float theta=(i.uv.x-.5)*2*UNITY_PI;
                    float3 east=float3(cos(theta),0,sin(theta));
                    float3 north=cross(east,n);
                    n=normalize(n-ew*east-ns*north);
                    light=.47+.65*saturate(dot(n,_Lamp));
                }
                float3 displayRGB=saturate(TerrainTint(h)*light);
                displayRGB=lerp(lerp(displayRGB,float3(.06,.10,.12),outline),float3(1,.94,.74),marker);
                #ifndef UNITY_COLORSPACE_GAMMA
                displayRGB=GammaToLinearSpace(displayRGB);
                #endif
                return float4(displayRGB,1);
            }
            ENDCG
        }
    }
}
