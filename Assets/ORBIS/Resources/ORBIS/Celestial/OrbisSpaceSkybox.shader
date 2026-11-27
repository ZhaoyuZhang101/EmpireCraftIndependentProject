Shader "ORBIS/Celestial/OrbisSpaceSkybox"
{
    Properties
    {
        _Tint ("Tint Color", Color) = (.5, .5, .5, .5)
        [Gamma] _Exposure ("Exposure", Range(0, 8)) = 1.0
        _Rotation ("Rotation", Range(0, 360)) = 0
        [NoScaleOffset] _FrontTex ("Front [+Z]", 2D) = "grey" {}
        [NoScaleOffset] _BackTex ("Back [-Z]", 2D) = "grey" {}
        [NoScaleOffset] _LeftTex ("Left [+X]", 2D) = "grey" {}
        [NoScaleOffset] _RightTex ("Right [-X]", 2D) = "grey" {}
        [NoScaleOffset] _UpTex ("Up [+Y]", 2D) = "grey" {}
        [NoScaleOffset] _DownTex ("Down [-Y]", 2D) = "grey" {}
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _FrontTex, _BackTex, _LeftTex, _RightTex, _UpTex, _DownTex;
            half4 _Tint;
            half _Exposure;
            float _Rotation;

            struct appdata_t
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 screenPos : TEXCOORD0;
            };

            float3 RotateAroundYInDegrees(float3 vertex, float degrees)
            {
                float alpha = degrees * UNITY_PI / 180.0;
                float sina, cosa;
                sincos(alpha, sina, cosa);
                float2x2 m = float2x2(cosa, -sina, sina, cosa);
                return float3(mul(m, vertex.xz), vertex.y).xzy;
            }

            v2f vert(appdata_t v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.screenPos = ComputeScreenPos(o.pos);
                return o;
            }

            half4 SampleSixSided(float3 d)
            {
                float3 ad = abs(d);
                float2 uv;
                if (ad.z >= ad.x && ad.z >= ad.y)
                {
                    if (d.z >= 0.0)
                    {
                        uv = saturate(0.5 + 0.5 * float2(d.x, d.y) / max(1e-6, d.z));
                        return tex2Dlod(_FrontTex, float4(uv, 0, 0));
                    }
                    uv = saturate(0.5 + 0.5 * float2(-d.x, d.y) / max(1e-6, -d.z));
                    return tex2Dlod(_BackTex, float4(uv, 0, 0));
                }
                if (ad.x >= ad.y)
                {
                    if (d.x >= 0.0)
                    {
                        uv = saturate(0.5 + 0.5 * float2(-d.z, d.y) / max(1e-6, d.x));
                        return tex2Dlod(_LeftTex, float4(uv, 0, 0));
                    }
                    uv = saturate(0.5 + 0.5 * float2(d.z, d.y) / max(1e-6, -d.x));
                    return tex2Dlod(_RightTex, float4(uv, 0, 0));
                }
                if (d.y >= 0.0)
                {
                    uv = saturate(0.5 + 0.5 * float2(d.x, -d.z) / max(1e-6, d.y));
                    return tex2Dlod(_UpTex, float4(uv, 0, 0));
                }
                uv = saturate(0.5 + 0.5 * float2(d.x, d.z) / max(1e-6, -d.y));
                return tex2Dlod(_DownTex, float4(uv, 0, 0));
            }

            half4 frag(v2f i) : SV_Target
            {
                float2 screenUV = i.screenPos.xy / max(1e-6, i.screenPos.w);
                float2 ndc = screenUV * 2.0 - 1.0;
                float aspect = _ScreenParams.x / max(1.0, _ScreenParams.y);
                // Keep deep-space starfield at crisp 42-deg reference angular density even when
                // the telephoto globe camera uses 18-deg external-viewpoint FOV.
                const float tanRefHalfFov = 0.38386404;
                float3 camRight = UNITY_MATRIX_V[0].xyz;
                float3 camUp    = UNITY_MATRIX_V[1].xyz;
                float3 camFwd   = -UNITY_MATRIX_V[2].xyz;
                float3 worldDir = normalize(camFwd + camRight * (ndc.x * aspect * tanRefHalfFov) + camUp * (ndc.y * tanRefHalfFov));
                worldDir = RotateAroundYInDegrees(worldDir, _Rotation);
                half3 c = SampleSixSided(worldDir).rgb * _Tint.rgb * unity_ColorSpaceDouble.rgb * _Exposure;
                return half4(c, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
