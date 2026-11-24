// Полноэкранная текстура бумаги поверх картинки (умножение) — для uGUI RawImage.
Shader "Notebook/PaperOverlay"
{
    Properties
    {
        _MainTex ("Бумага", 2D) = "white" {}
        _Strength ("Сила", Range(0,1)) = 0.6
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend DstColor Zero
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_ST; float _Strength;
            struct A { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A v) { V o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = TRANSFORM_TEX(v.uv, _MainTex); return o; }
            fixed4 frag(V i) : SV_Target
            {
                fixed4 p = tex2D(_MainTex, i.uv);
                // текстура: тёмные волокна/пятна с альфой → множитель около 1
                return fixed4(lerp(fixed3(1,1,1), p.rgb, p.a * _Strength), 1);
            }
            ENDHLSL
        }
    }
}
