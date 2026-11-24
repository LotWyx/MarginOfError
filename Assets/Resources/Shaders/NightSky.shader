// Ночное небо над тетрадным лабиринтом: градиент от лавандового горизонта к тёмно-синему зениту,
// процедурные мерцающие звёзды и серп луны с ореолом. Ставится как RenderSettings.skybox (см. InkGame).
Shader "Notebook/NightSky"
{
    Properties
    {
        _ZenithColor ("Зенит", Color) = (0.04, 0.05, 0.17, 1)
        _MidColor ("Середина", Color) = (0.17, 0.17, 0.42, 1)
        _HorizonColor ("Горизонт", Color) = (0.42, 0.35, 0.6, 1)
        _MoonColor ("Луна", Color) = (1, 0.95, 0.77, 1)
        _MoonDir ("Направление на луну", Vector) = (0.4, 0.6, 0.7, 0)
        _MoonSize ("Радиус луны (рад)", Float) = 0.055
        _MoonDisc ("Рисовать серп (0 — только ореол)", Float) = 1
        _MoonHalo ("Яркость ореола (0 — без луны)", Float) = 1
        _StarDensity ("Доля ячеек со звездой", Range(0, 1)) = 0.1
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor, _MidColor, _HorizonColor, _MoonColor;
                float4 _MoonDir;
                float _MoonSize, _StarDensity, _MoonDisc, _MoonHalo;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float3 Hash33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            // Слой звёзд: в каждой ячейке 3D-сетки (по направлению) — максимум одна звезда.
            float Stars(float3 d, float scale, float size, float seed, float density)
            {
                float3 p = d * scale;
                float3 cell = floor(p);
                float sum = 0;
                // соседние ячейки — чтобы звезда у границы ячейки не обрезалась
                for (int z = -1; z <= 1; z++)
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float3 c = cell + float3(x, y, z);
                    float3 h = Hash33(c + seed);
                    if (h.x > density) continue;
                    float3 starPos = c + Hash33(c + seed + 17.0);
                    float dist = length(p - starPos);
                    float brightness = lerp(0.35, 1.0, h.y * h.y);
                    float twinkle = 0.75 + 0.25 * sin(_Time.y * (1.5 + h.z * 3.0) + h.z * 40.0);
                    sum += brightness * twinkle * smoothstep(size, size * 0.25, dist);
                }
                return sum;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float up = d.y;

                // градиент: горизонт → середина → зенит
                half3 col = lerp(_HorizonColor.rgb, _MidColor.rgb, smoothstep(0.0, 0.22, up));
                col = lerp(col, _ZenithColor.rgb, smoothstep(0.18, 0.75, up));
                if (up < 0) col = _HorizonColor.rgb;

                // звёзды гаснут к горизонту (там дымка)
                float starFade = smoothstep(0.04, 0.3, up);
                // крупные редкие и мелкие тусклые
                float stars = Stars(d, 90.0, 0.22, 0.0, _StarDensity) + Stars(d, 220.0, 0.2, 51.0, _StarDensity * 0.3) * 0.6;
                col += half3(1.0, 0.96, 0.88) * stars * starFade * 2.2;

                // луна: серп = диск минус смещённый диск, вокруг ореол
                float3 m = normalize(_MoonDir.xyz);
                float facing = dot(d, m);
                float3 right = normalize(cross(float3(0, 1, 0), m));
                float3 upAxis = cross(m, right);
                float2 q = float2(dot(d, right), dot(d, upAxis)) / _MoonSize;
                float disc = smoothstep(1.0, 0.94, length(q)) * step(0, facing);
                float bite = smoothstep(0.92, 0.86, length(q - float2(0.42, 0.28)));
                float crescent = disc * (1 - bite) * _MoonDisc;
                float halo = pow(saturate(facing), 2500.0) * 0.45 + pow(saturate(facing), 120.0) * 0.12;
                col += _MoonColor.rgb * halo * _MoonHalo;
                col = lerp(col, _MoonColor.rgb * 2.4, crescent); // >1 — подхватит bloom

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
