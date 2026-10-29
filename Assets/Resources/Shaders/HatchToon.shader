// Мультяшный шейдер для 3D-моделей животных в галерее:
// две ступени освещения, мягкий контурный свет и маленький блик. Источник света задан в шейдере,
// поэтому сцене не нужны лампы. Лежит в Resources, чтобы точно попасть в сборку.
Shader "HatchMerge/Toon"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _ShadeColor ("Shade", Color) = (0.78, 0.72, 0.88, 1)
        _Rim ("Rim", Range(0, 1)) = 0.22
        _Gloss ("Gloss", Range(0, 1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(fixed4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)
            fixed4 _ShadeColor;
            half _Rim, _Gloss;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 n : TEXCOORD0;
                float3 v : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            v2f vert(appdata i)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                o.pos = UnityObjectToClipPos(i.vertex);
                o.n = UnityObjectToWorldNormal(i.normal);
                o.v = normalize(WorldSpaceViewDir(i.vertex));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                fixed4 col = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                float3 n = normalize(i.n);
                float3 v = normalize(i.v);
                float3 l = normalize(float3(-0.45, 0.8, -0.55));
                float d = dot(n, l) * 0.5 + 0.5;
                float step1 = smoothstep(0.42, 0.5, d);
                fixed3 c = lerp(col.rgb * _ShadeColor.rgb, col.rgb, step1);
                float rim = pow(1.0 - saturate(dot(n, v)), 3.0) * _Rim;
                c += rim * (0.6 + 0.4 * step1);
                float spec = pow(saturate(dot(reflect(-l, n), v)), 48.0);
                c += smoothstep(0.45, 0.55, spec) * _Gloss;
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
    Fallback "Unlit/Color"
}
