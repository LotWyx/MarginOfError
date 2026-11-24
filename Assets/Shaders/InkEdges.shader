// Чернильная обводка поверх кадра в духе cel-shading: ровная линия одной толщины по силуэтам
// и по рёбрам предметов, одна для всей игры — комнаты, предметов из кода, лабиринта и «Ошибки».
// Подключается Full Screen Pass Renderer Feature в Settings/PC_Renderer (до прозрачных:
// пыль и пар рисуются после и не обводятся). Нужны глубина и нормали (requirements = Depth | Normal).
//  • силуэт — разрыв глубины; линия на ближней стороне разрыва — ровно по контуру, без двоения;
//    1/z на плоскости линейна по экрану: пол и стены под острым углом не дают ложных линий;
//  • ребро — излом нормалей, только выпуклый (ребро коробки, грань карандаша); вогнутые углы
//    (стена с полом, углы комнаты) линий не дают — комната не расчерчивается;
//    скругления и цилиндры с гладкими нормалями — без линий;
//  • сглаживание: край считается в 4 точках внутри пикселя и усредняется — линия без «лесенки»;
//  • постоянная толщина на экране; вдали тоньше и бледнее, в тумане уходит вместе с предметом;
//  • вид из окна не обводится (у нарисованного вида контуры свои): его слои ставят метку 4 в буфер трафарета
//    (NotebookSprite, _StencilRef), проход её пропускает — точно и для любой камеры, без восстановления позиции.
Shader "Hidden/Notebook/InkEdges"
{
    Properties
    {
        _LineColor ("Чернила (подмешиваются)", Color) = (0.05, 0.06, 0.17, 1)
        _InkTint ("Множитель цвета под линией", Color) = (0.1, 0.1, 0.2, 1)
        _LineMix ("Доля чистых чернил", Range(0, 1)) = 0.5
        _Thickness ("Толщина (пикс. при 1080p)", Float) = 2.4
        _DepthSensitivity ("Порог разрыва глубины (доля)", Float) = 0.012
        _LaplaceSensitivity ("Порог второй производной", Float) = 0.009
        _NormalSensitivity ("Порог излома нормалей (1 − cos)", Float) = 0.25
        _ConcaveSensitivity ("Порог вогнутости (доля)", Float) = 0.002
        _DistanceBias ("Рост порога вдали (на 1000 ед.)", Float) = 0.004
        _ThinStart ("Линия тоньше с", Float) = 900
        _ThinEnd ("Линия тонкая на", Float) = 3500
        _FarStrength ("Сила линии вдали", Range(0, 1)) = 0.55
        _FadeStart ("Линии гаснут с", Float) = 6000
        _FadeEnd ("Линии исчезли на", Float) = 11000
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "InkEdges"
            Stencil { Ref 4 ReadMask 4 Comp NotEqual } // нужен bindDepthStencilAttachment в Settings/PC_Renderer
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            half4 _LineColor, _InkTint;
            float _ThinStart, _ThinEnd, _FarStrength;
            float _LineMix, _Thickness, _DepthSensitivity, _LaplaceSensitivity, _NormalSensitivity, _ConcaveSensitivity;
            float _DistanceBias, _FadeStart, _FadeEnd;
            float _InkEdgesOff;   // глобальный выключатель экранной обводки (задаёт InkGame)
            float _InkEdgeWidthScale; // глобальная: множитель толщины для текущей камеры (меню — тоньше, MenuCamera); 0 — как 1

            float InvDepth(float2 uv)
            {
                return 1.0 / max(LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams), 1e-3);
            }

            // сила линии (0…1) в точке uv; px — радиус выборки (толщина линии) в долях экрана, rPx — он же в пикселях.
            // Глубина читается по ближайшему пикселю: точка выборки смещена до полупикселя, и на поверхности,
            // видной вскользь (пол коридора вдали), «плоскость» даёт ненулевую вторую производную — до
            // |a − b| / rPx. Этот допуск добавляется к порогам, иначе пол у дальней стены обводится полосой.
            float EdgeAt(float2 uv, float2 px, float rPx, float thr)
            {
                float c = InvDepth(uv);
                float3 nc = SampleSceneNormals(uv);
                // 4 направления на одинаковом расстоянии (диагонали — ×0.707): ширина линии одна во все стороны
                const float2 dirs[4] = { float2(1, 0), float2(0, 1), float2(0.7071, 0.7071), float2(0.7071, -0.7071) };
                float sil = 0, crease = 0;
                [unroll] for (int k = 0; k < 4; k++)
                {
                    float2 d = dirs[k] * px;
                    float a = InvDepth(uv + d), b = InvDepth(uv - d);
                    float lap = (a + b - 2 * c) / max(c, 1e-6);     // ≈ 0 на плоскости; < 0 — выпукло, > 0 — вогнуто
                    float tol = abs(a - b) / max(c, 1e-6) / rPx;     // ошибка выборки на наклонной плоскости

                    // силуэт: этот пиксель ближе соседа на заметную долю (ближняя сторона разрыва) и это не плоскость
                    float jump = max(c - a, c - b) / max(c, 1e-6);
                    float lapThr = _LaplaceSensitivity + tol * 1.2;
                    sil = max(sil, smoothstep(thr, thr * 1.3, jump)
                                   * smoothstep(lapThr, lapThr * 1.3, abs(lap)));

                    // ребро: нормали по обе стороны (на половине радиуса — линия ребра той же ширины, что у силуэта)
                    float3 na = SampleSceneNormals(uv + d * 0.5), nb = SampleSceneNormals(uv - d * 0.5);
                    float bend = max(1 - dot(nc, na), 1 - dot(nc, nb));
                    // выпукло — только если вторая производная отрицательна с запасом на ошибку выборки:
                    // вдали стык стены с полом (вогнутый) не должен из-за шума стать «ребром»
                    float convex = 1 - smoothstep(_ConcaveSensitivity - tol * 1.2, _ConcaveSensitivity * 2 - tol * 1.2, lap);
                    crease = max(crease, smoothstep(_NormalSensitivity, _NormalSensitivity * 1.4, bend) * convex);
                }
                return max(sil, crease);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                if (_InkEdgesOff > 0.5) return color;

                float raw = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                    if (raw <= 1e-6) return color;              // небо
                #else
                    if (raw >= 1 - 1e-6) return color;
                #endif

                float eyeDist = LinearEyeDepth(raw, _ZBufferParams);
                // вдали линия тоньше (до 45 %) — мелкие дальние предметы не превращаются в каракули
                float thin = lerp(1.0, 0.45, smoothstep(_ThinStart, _ThinEnd, eyeDist));
                float2 onePx = 1.0 / _ScreenParams.xy;
                float widthScale = _InkEdgeWidthScale > 0 ? _InkEdgeWidthScale : 1.0;
                float rPx = max(1.0, _Thickness * thin * widthScale * _ScreenParams.y / 1080.0);
                float2 px = rPx * onePx;
                float thr = _DepthSensitivity + _DistanceBias * eyeDist / 1000.0; // вдали точность глубины хуже

                // сглаживание: 4 точки внутри пикселя (повёрнутая решётка)
                float edge = 0.25 * (EdgeAt(uv + onePx * float2( 0.125,  0.375), px, rPx, thr)
                                   + EdgeAt(uv + onePx * float2(-0.375,  0.125), px, rPx, thr)
                                   + EdgeAt(uv + onePx * float2(-0.125, -0.375), px, rPx, thr)
                                   + EdgeAt(uv + onePx * float2( 0.375, -0.125), px, rPx, thr));

                edge *= 1.0 - smoothstep(_FadeStart, _FadeEnd, eyeDist);
                edge *= lerp(1.0, _FarStrength, smoothstep(_ThinStart, _ThinEnd, eyeDist)); // вдали — бледнее

                half3 ink = lerp(color.rgb * _InkTint.rgb, _LineColor.rgb, _LineMix);
                ink = min(ink, color.rgb * 0.8); // линия никогда не светлее того, что под ней (в тёмном углу не «светится»)
                return half4(lerp(color.rgb, ink, edge), color.a);
            }
            ENDHLSL
        }
    }
}
