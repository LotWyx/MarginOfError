// Стиль «тетрадь в клетку»: мультяшный свет в 3 ступени, штриховка ручкой в тени,
// клетка/линейки по желанию, туман цвета бумаги. Контур — объёмный (inverted hull) + поверх кадра (InkEdges).
Shader "Notebook/Ink"
{
    Properties
    {
        _BaseColor ("Цвет", Color) = (0.94, 0.9, 0.81, 1)
        _MidColor ("Полутень (множитель)", Color) = (0.82, 0.83, 0.9, 1)
        _ShadowColor ("Тень (множитель)", Color) = (0.62, 0.66, 0.82, 1)
        _InkColor ("Чернила", Color) = (0.12, 0.16, 0.42, 1)
        _HatchScale ("Шаг штриховки (пикс.)", Float) = 9
        _HatchStrength ("Сила штриховки", Range(0,1)) = 0.85
        _HatchWorld ("Размер штриховки на поверхности (м)", Float) = 0.9
        [Toggle] _UseTextures ("Текстуры бумаги и штриховки", Float) = 0
        [NoScaleOffset] _PaperTex ("Бумага", 2D) = "white" {}
        [Normal] [NoScaleOffset] _PaperNormal ("Рельеф бумаги", 2D) = "bump" {}
        [NoScaleOffset] _HatchTex0 ("Редкая штриховка", 2D) = "white" {}
        [NoScaleOffset] _HatchTex1 ("Средняя штриховка", 2D) = "white" {}
        [NoScaleOffset] _HatchTex2 ("Перекрёстная штриховка", 2D) = "white" {}
        [Toggle] _Grid ("Клетка (пол)", Float) = 0
        _GridColor ("Цвет клетки", Color) = (0.56, 0.7, 0.85, 1)
        _GridSize ("Размер клетки", Float) = 1
        _GridStrength ("Насыщенность клетки", Range(0,1)) = 1
        _HatchLit ("Штриховка на свету (множитель)", Range(0,1)) = 1
        [Toggle] _Ruled ("Линейки (стены)", Float) = 0
        _RuledSize ("Шаг линеек", Float) = 0.35
        _Emission ("Свечение", Float) = 0
        _OutlineWidth ("Толщина контура", Float) = 0.035
        _OutlineColor ("Цвет контура", Color) = (0.08, 0.1, 0.27, 1)
        _OutlineFadeStart ("Контур гаснет с, м", Float) = 22
        _OutlineFadeEnd ("Контур исчез на, м", Float) = 45
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _MidColor, _ShadowColor, _InkColor, _GridColor, _OutlineColor;
            float _HatchScale, _HatchStrength, _Grid, _GridSize, _Ruled, _RuledSize, _Emission, _OutlineWidth, _OutlineFadeStart, _OutlineFadeEnd;
            float _UseTextures, _HatchWorld, _GridStrength, _HatchLit;
        CBUFFER_END

        // глобальные (задаёт игра): туман цвета бумаги
        half4 _InkFogColor;
        float _InkFogStart, _InkFogEnd;
        float _InkOutlineOff; // глобальный выключатель всей обводки (задаёт InkGame)
        // глобальные: свет настольной лампы — конус из абажура (задаёт InkGame).
        // _LampPos.xyz — абажур; _LampAxis.xyz — куда светит; _LampCone: x/y — cos внутреннего/внешнего угла,
        // z/w — расстояние, где свет начинает слабеть / сходит на нет. _LampPool.xz — центр лабиринта (там
        // свет всегда полный, чтобы стены лабиринта читались). _LampPoolOuter > 0 — лампа задана.
        // _RoomShade — множитель цвета в полумраке комнаты; _LampTint — цвет и сила света лампы (может быть > 1).
        float4 _LampPool;
        float _LampPoolOuter;
        float4 _LampPos, _LampAxis, _LampCone;
        half4 _RoomShade;
        half4 _LampTint;

        // сколько света лампы получает точка (0…1): конус с мягким краем, спад с расстоянием,
        // поверхность, отвёрнутая от лампы, света не получает (n = 0 — без учёта поворота)
        float LampLight(float3 posWS, float3 n)
        {
            float3 L = posWS - _LampPos.xyz;
            float dist = max(length(L), 1e-3);
            float3 dir = L / dist;
            float cone = smoothstep(_LampCone.y, _LampCone.x, dot(dir, _LampAxis.xyz));
            float fall = 1 - smoothstep(_LampCone.z, _LampCone.w, dist);
            fall *= fall;
            float facing = dot(n, n) > 0.5 ? smoothstep(-0.25, 0.35, dot(n, -dir)) : 1;
            // лабиринт на странице всегда освещён полностью — геймплей важнее
            // (только сама страница и стены лабиринта: от y ≈ 0, стол на −7 — не входит)
            float maze = (posWS.y > -0.5 && posWS.y < 8 && distance(posWS.xz, _LampPool.xz) < 170) ? 1 : 0;
            // столешница заслоняет свет: под столом (ниже −7) лампа не светит — без пятна на полу
            float underDesk = smoothstep(-40, -12, posWS.y);
            return max(cone * fall * facing * underDesk, maze);
        }

        half3 ApplyLampPool(half3 col, float3 posWS, float3 n)
        {
            if (_LampPoolOuter <= 0) return col; // игра не задала лампу — без затухания
            float pool = LampLight(posWS, n);
            half3 lampCol = _LampTint.a > 0 ? _LampTint.rgb : half3(1, 1, 1);
            return col * lerp(_RoomShade.rgb, lampCol, pool);
        }
        half3 ApplyLampPool(half3 col, float3 posWS) { return ApplyLampPool(col, posWS, float3(0, 0, 0)); }

        half3 ApplyPaperFog(half3 col, float3 posWS)
        {
            float d = distance(posWS, _WorldSpaceCameraPos);
            float f = saturate((d - _InkFogStart) / max(_InkFogEnd - _InkFogStart, 0.001));
            return lerp(col, _InkFogColor.rgb, f * f);
        }
        ENDHLSL

        Pass
        {
            Name "InkForward"
            Tags { "LightMode"="UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma target 3.0
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            TEXTURE2D(_PaperTex); SAMPLER(sampler_PaperTex);
            TEXTURE2D(_PaperNormal); SAMPLER(sampler_PaperNormal);
            TEXTURE2D(_HatchTex0); SAMPLER(sampler_HatchTex0);
            TEXTURE2D(_HatchTex1);
            TEXTURE2D(_HatchTex2);

            half3 SamplePaper(float3 positionWS, float3 n, out half3 detailNormal)
            {
                float3 p = positionWS * 0.25;
                half3 weights = abs(n);
                weights *= weights;
                weights /= dot(weights, half3(1, 1, 1));

                half3 paper = SAMPLE_TEXTURE2D(_PaperTex, sampler_PaperTex, p.zy).rgb * weights.x
                            + SAMPLE_TEXTURE2D(_PaperTex, sampler_PaperTex, p.xz).rgb * weights.y
                            + SAMPLE_TEXTURE2D(_PaperTex, sampler_PaperTex, p.xy).rgb * weights.z;
                half2 nx = UnpackNormal(SAMPLE_TEXTURE2D(_PaperNormal, sampler_PaperNormal, p.zy)).xy;
                half2 ny = UnpackNormal(SAMPLE_TEXTURE2D(_PaperNormal, sampler_PaperNormal, p.xz)).xy;
                half2 nz = UnpackNormal(SAMPLE_TEXTURE2D(_PaperNormal, sampler_PaperNormal, p.xy)).xy;
                half3 tilt = half3(0, nx.y, nx.x) * weights.x
                           + half3(ny.x, 0, ny.y) * weights.y
                           + half3(nz.x, nz.y, 0) * weights.z;
                detailNormal = normalize(n + (tilt - n * dot(n, tilt)) * 0.4);

                // Preserve the template colors at the mean color of the paper.
                half3 paperMean = half3(239, 230, 207) / 255.0;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    paperMean = SRGBToLinear(paperMean);
                #endif
                return paper / paperMean;
            }

            // координаты на плоскости, к которой ближе всего грань (пол / стена X / стена Z)
            float2 PlaneUV(float3 p, float3 n)
            {
                float3 a = abs(n);
                if (a.y >= a.x && a.y >= a.z) return p.xz;
                return a.x >= a.z ? p.zy : p.xy;
            }

            half SampleHatch(float2 uv, float lit)
            {
                half lightInk = (1 - SAMPLE_TEXTURE2D(_HatchTex0, sampler_HatchTex0, uv).r) * 0.35;
                half midInk = 1 - SAMPLE_TEXTURE2D(_HatchTex1, sampler_HatchTex0, uv).r;
                half darkInk = 1 - SAMPLE_TEXTURE2D(_HatchTex2, sampler_HatchTex0, uv).r;
                half lightToMid = lerp(midInk, lightInk, smoothstep(0.41, 0.59, lit));
                return lerp(darkInk, lightToMid, smoothstep(0.11, 0.25, lit));
            }

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.screenPos = ComputeScreenPos(p.positionCS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float ndl = saturate(dot(n, light.direction));
                // Тень — одним порогом: мягкий край тени иначе проходит сразу через две ступени
                // (свет → полутень → тень), и граница тени на полу раздваивается.
                float shadow = smoothstep(0.42, 0.58, light.shadowAttenuation);
                float lit = ndl * shadow;

                half3 base = _BaseColor.rgb;
                if (_Grid > 0.5)
                {
                    float2 guv = PlaneUV(i.positionWS, n) / _GridSize;
                    float2 g = abs(frac(guv + 0.5) - 0.5);
                    float2 w = fwidth(guv) * 1.2 + 0.012;   // сглаживание, чтобы вдали не рябило
                    float2 gl2 = 1 - smoothstep(w * 0.5, w, g);
                    float gl = max(gl2.x, gl2.y) * saturate(1.2 - max(fwidth(guv).x, fwidth(guv).y) * 2.5);
                    base = lerp(base, _GridColor.rgb, gl * _GridStrength);
                }
                if (_Ruled > 0.5)
                {
                    float r = frac(i.positionWS.y / _RuledSize);
                    float dl = min(r, 1 - r) * _RuledSize;
                    base = lerp(base, _GridColor.rgb, (1 - smoothstep(0.004, 0.012, dl)) * 0.75);
                }

                // три ступени света
                half3 mul = lit > 0.5 ? half3(1,1,1) : (lit > 0.18 ? _MidColor.rgb : _ShadowColor.rgb);
                half3 col = base * mul;

                // цветной свет от цифр, «Ошибки» и ворот (доп. источники) — с плавным спадом
                half3 addLight = 0;
                #if defined(_ADDITIONAL_LIGHTS) || defined(_CLUSTER_LIGHT_LOOP)
                {
                    InputData inputData = (InputData)0;
                    inputData.positionWS = i.positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                    uint addCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(addCount)
                        Light al = GetAdditionalLight(lightIndex, i.positionWS);
                        float a = al.distanceAttenuation * saturate(dot(n, al.direction) * 0.6 + 0.4);
                        // Раньше затухание округлялось до трёх ступеней (1 / 0.5 / 0.18 / 0 ниже 0.03) — отсюда
                        // резкие кольца ореола на стенах и пятно на полу. Теперь тот же диапазон, но плавно:
                        // ниже 0.02 света нет (как и раньше — мощные «комнатные» источники вдали ничего не засвечивают),
                        // к 0.6 — полная сила, между ними гладкий спад без видимой границы.
                        a = smoothstep(0.02, 0.6, a);
                        addLight += al.color * a;
                    LIGHT_LOOP_END
                }
                #endif

                // Штриховка — смешением трёх проекций (как бумага): на кривых поверхностях (мячи, глобус)
                // нет швов в местах, где ближайшая плоскость сменяется. На ровных стенах и полу вес одной
                // проекции = 1 — картинка та же, что раньше.
                float3 hp = i.positionWS / max(_HatchWorld, 0.05);
                half3 hw = pow(abs(n), 4);
                hw /= max(dot(hw, half3(1, 1, 1)), 1e-4);
                float hatch = 0;
                if (_UseTextures > 0.5)
                {
                    half3 detailNormal;
                    col *= SamplePaper(i.positionWS, n, detailNormal);
                    // A continuous light correction keeps fine fibers from flickering at band edges.
                    half relief = saturate(dot(detailNormal, light.direction)) - ndl;
                    col *= 1 + relief * 0.45 * shadow;
                    hatch = SampleHatch(hp.zy, lit) * hw.x + SampleHatch(hp.xz, lit) * hw.y + SampleHatch(hp.xy, lit) * hw.z;
                }
                else
                {
                    // Процедурная штриховка исходного материала (тоже по трём проекциям).
                    float2 uvs[3] = { hp.zy, hp.xz, hp.xy };
                    float ws[3] = { hw.x, hw.y, hw.z };
                    [unroll] for (int k = 0; k < 3; k++)
                    {
                        float2 huv = uvs[k];
                        float d = (huv.x + huv.y + sin(huv.y * 9.0) * 0.08) * 9.0;
                        float x = abs(frac(d) - 0.5) * 2;
                        float fw = fwidth(d);
                        float hatchLine = smoothstep(0.78 - fw, 0.82 + fw, x) * saturate(1.5 - fw * 2.0);
                        float h = 0;
                        if (lit <= 0.18) h = hatchLine;
                        else if (lit <= 0.5) h = hatchLine * step(0.5, frac(d * 0.5)) * 0.8;
                        hatch += h * ws[k];
                    }
                }
                // На освещённой бумаге (лампа или тёплый свет цифры) штриховка слабее, в тени — полная.
                // _HatchLit = 1 (по умолчанию) — как раньше; меньше — спокойнее освещённые поверхности.
                float litMask = smoothstep(0.3, 0.6, lit);
                float localLit = saturate(dot(addLight, half3(0.3, 0.5, 0.2)) * 1.5);
                hatch *= lerp(1.0, _HatchLit, max(litMask, localLit));
                col = lerp(col, _InkColor.rgb, hatch * _HatchStrength);
                col = ApplyLampPool(col, i.positionWS, n);

                col += base * addLight;
                col += _BaseColor.rgb * _Emission;
                col = ApplyPaperFog(col, i.positionWS);
                return half4(col, 1);
            }
            ENDHLSL
        }

        // Экранная обводка (Hidden/Notebook/InkEdges) дорисовывает то, что оболочка теряет:
        // силуэты больших круглых предметов, стоящих на столе.

        // Объёмный контур (inverted hull) — вместе с экранной обводкой InkEdges.
        // Толщина чуть «дрожит» по миру и подстраивается под расстояние, поэтому при движении и
        // смене угла линия живёт, как нарисованная от руки.
        Pass
        {
            Name "InkOutline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // uv3 (TEXCOORD3) — готовое направление раздувания для цельных мешей (стены);
            // у обычных коробок его нет, и берётся направление от центра вдоль осей объекта.
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float3 outlineOS : TEXCOORD3; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 pWS = TransformObjectToWorld(v.positionOS.xyz);
                // Вершины уходят от центра вдоль осей объекта (без учёта масштаба): совпадающие вершины
                // соседних граней сдвигаются одинаково — оболочка цельная, без «плит» на рёбрах.
                float4x4 m = GetObjectToWorldMatrix();
                float3 s = dot(v.outlineOS, v.outlineOS) > 0.01 ? v.outlineOS : sign(v.positionOS.xyz);
                float3 dirWS = s.x * normalize(m._m00_m10_m20) + s.y * normalize(m._m01_m11_m21) + s.z * normalize(m._m02_m12_m22);
                // «дрожание» толщины, как у живой линии (одинаково для совпадающих вершин)
                float wob = 0.75 + 0.5 * frac(sin(dot(floor(pWS * 3.0), float3(12.9898, 78.233, 37.719))) * 43758.5453);
                // толщина растёт с расстоянием: на экране линия примерно одной ширины
                // (_OutlineWidth — толщина в метрах на расстоянии 8 м)
                float camDist = distance(pWS, _WorldSpaceCameraPos);
                float viewScale = max(camDist / 8.0, 0.05);
                // вдали оболочка плавно сходит на нет: дальние стены и гигантская комната вокруг
                // обводятся только аккуратной экранной обводкой (InkEdges) — без двойных линий и
                // слипшегося в чёрное пятно лабиринта
                float nearFade = 1.0 - smoothstep(_OutlineFadeStart, _OutlineFadeEnd, camDist);
                pWS += dirWS * _OutlineWidth * wob * viewScale * nearFade;
                o.positionWS = pWS;
                o.positionCS = TransformWorldToHClip(pWS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                if (_InkOutlineOff > 0.5) discard; // обводка выключена (без экстремальных координат — без артефактов)
                return half4(ApplyPaperFog(ApplyLampPool(_OutlineColor.rgb, i.positionWS), i.positionWS), 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"

        // Глубина и нормали — свои, с тем же отсечением, что у цвета (Cull Back). Проходы из Lit берут
        // режим из свойства _Cull, которого здесь нет, и рисуют обе стороны: обратные стороны
        // односторонних стен лабиринта попадали в глубину и нормали, хотя на экране их нет, — экранная
        // обводка (InkEdges) рисовала по ним призрачные линии и заливала торцы стен чёрным.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull Back ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct A { float4 positionOS : POSITION; };
            float4 vert(A v) : SV_POSITION { return TransformObjectToHClip(v.positionOS.xyz); }
            half frag(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            V vert(A v)
            {
                V o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
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
