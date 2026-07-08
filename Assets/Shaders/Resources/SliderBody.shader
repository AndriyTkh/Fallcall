// Slider-body / border shader. Paints each pixel exactly once via stencil so the
// self-overlapping slider mesh (round joins, curve-backs) never alpha-blends on top of
// itself — which is what produced the dark "artifact" patches.
//
// Each drawable using this picks a _StencilRef: border and body use different refs so the
// narrower body still layers once over its wider border without either blocking the other.
// _Color is a per-slider tint (combo colour * fade alpha) so alpha fades are a cheap
// material set rather than a per-frame mesh-colour rewrite.
// Cross-slider overlap (two sliders sharing screen pixels) can still bite; the stencil
// buffer clears each frame. Acceptable for now.
Shader "Osu/SliderBody"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [IntRange] _StencilRef ("Stencil Ref", Range(0,255)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        // First fragment on a pixel: buffer(0) != Ref -> pass, then write Ref.
        // Later fragments on the same pixel: buffer == Ref -> fail, discarded (no re-blend).
        Stencil
        {
            Ref [_StencilRef]
            Comp NotEqual
            Pass Replace
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv) * i.color * _Color;
            }
            ENDCG
        }
    }
}
