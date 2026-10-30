// Unlit, transparent, double-sided. Vertex colours times _Tint (set _UseVertexColor to 0 for meshes without
// colours, e.g. AR plane meshes). No lighting, shadows or textures: cheap on mobile and works under URP.
//
// Stroke anti-aliasing (BoardMeshBuilder ribbons): TEXCOORD1 = (offset from the ribbon centre line to this vertex in
// object space, side * miter). The vertex shader measures the stroke's half-width in screen pixels, widens the ribbon to
// at least _MinWidthPx (fading alpha by the same factor, so coverage is preserved and thin lines do not shimmer) plus
// _AAPadPx on each side, and the fragment shader fades the long edges over one pixel with fwidth. Vertices with
// TEXCOORD1.w = 0 (discs, quads, meshes without that channel) are drawn solid, exactly as before.
Shader "GeoART/BoardUnlit"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        [Toggle] _UseVertexColor ("Use Vertex Color", Float) = 1
        _MinWidthPx ("Min Stroke Width (px)", Float) = 1.5
        _AAPadPx ("Stroke AA Padding (px)", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Tint;
            float _UseVertexColor;
            float _MinWidthPx;
            float _AAPadPx;

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float4 stroke : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                // x: across the stroke in true half-widths (0 = centre line), y: visible edge (|x| at the edge),
                // z: coverage fade for strokes thinner than _MinWidthPx, w: 1 for strokes, 0 for solid fills
                float4 aa : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float2 ToPixels(float4 clip)
            {
                return clip.xy / clip.w * (0.5 * _ScreenParams.xy);
            }

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.color = lerp(_Tint, v.color * _Tint, _UseVertexColor);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.aa = float4(0, 1, 1, 0);

                float miter = abs(v.stroke.w);
                if (miter > 0.5)
                {
                    float side = v.stroke.w > 0 ? 1.0 : -1.0;
                    float3 offset = v.stroke.xyz;
                    float3 centre = v.vertex.xyz - offset;
                    float4 c = UnityObjectToClipPos(float4(centre, 1));
                    float4 e = UnityObjectToClipPos(float4(centre + offset / miter, 1));
                    o.aa = float4(side, 1, 1, 1);
                    if (c.w > 1e-4 && e.w > 1e-4)
                    {
                        float halfPx = max(length(ToPixels(e) - ToPixels(c)), 1e-3);
                        float visibleHalf = max(halfPx, 0.5 * _MinWidthPx);
                        float k = min((visibleHalf + _AAPadPx) / halfPx, 64.0);
                        o.pos = UnityObjectToClipPos(float4(centre + offset * k, 1));
                        o.aa = float4(side * k, visibleHalf / halfPx, halfPx / visibleHalf, 1);
                    }
                }
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = i.color;
                float perPixel = max(fwidth(i.aa.x), 1e-5);
                float coverage = saturate((i.aa.y - abs(i.aa.x)) / perPixel + 0.5) * i.aa.z;
                col.a *= lerp(1.0, coverage, i.aa.w);
                return col;
            }
            ENDCG
        }
    }
}
