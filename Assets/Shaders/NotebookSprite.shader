// Плоские рисованные персонажи/предметы в 3D-мире: вырезка по альфе, свечение, туман цвета бумаги.
Shader "Notebook/Sprite"
{
    Properties
    {
        _MainTex ("Картинка", 2D) = "white" {}
        _Tint ("Оттенок", Color) = (1,1,1,1)
        _Glow ("Свечение", Float) = 0
        _Cutoff ("Порог альфы", Range(0,1)) = 0.4
        _FogAmount ("Туман (0 — для неба)", Float) = 1
        // метка в буфере трафарета: 4 — вид из окна (RoomBuilder.ViewLayer), экранная обводка его пропускает
        [IntRange] _StencilRef ("Метка трафарета", Range(0, 255)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST; half4 _Tint; float _Glow, _Cutoff, _FogAmount;
        CBUFFER_END
        struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
        struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; };
        V vert(A v)
        {
            V o;
            o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.uv = TRANSFORM_TEX(v.uv, _MainTex);
            return o;
        }
        ENDHLSL

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            Stencil { Ref [_StencilRef] Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 _InkFogColor; float _InkFogStart, _InkFogEnd;
            half4 frag(V i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _Tint;
                clip(c.a - _Cutoff);
                c.rgb *= 1 + _Glow;
                float d = distance(i.positionWS, _WorldSpaceCameraPos);
                float f = saturate((d - _InkFogStart) / max(_InkFogEnd - _InkFogStart, 0.001));
                c.rgb = lerp(c.rgb, _InkFogColor.rgb, f * f * _FogAmount);
                return half4(c.rgb, 1);
            }
            ENDHLSL
        }

        // Глубина и нормали — с той же вырезкой по альфе: экранная обводка (InkEdges) видит рисунок
        // по его силуэту, а не прямоугольником. Без этих проходов спрайт выпадает из текстуры глубины,
        // когда движок строит её отдельным проходом (а так и есть — обводке нужны нормали).
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull Off ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half frag(V i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a * _Tint.a - _Cutoff);
                return i.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 frag(V i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a * _Tint.a - _Cutoff);
                float3 n = normalize(_WorldSpaceCameraPos - i.positionWS); // рисунок всегда «смотрит» на зрителя
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(n);
                    return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0.0);
                #else
                    return half4(n, 0.0);
                #endif
            }
            ENDHLSL
        }
    }
}
