// Lit clouds, in world space. The T0.5 half of the ladder — same LUT, same sun, real geometry.
//
// Reads ROW_CLOUD_LIT and ROW_CLOUD_SHADOW: the exact two rows T0's camera-anchored layer uses. That
// is the LUT contract doing its job — a new tier adds a row, never a second LUT. If this shader grew
// its own idea of what a cloud looks like at dusk, the two cloud systems in one sky could disagree
// about the time of day, and the whole point of driving everything off one scalar would be gone.
//
// Opaque, so overlapping cells are nearly free: early-Z kills buried fragments before they shade.
// That is the same property that makes T2's scattered boxes affordable — this shader is a rehearsal
// for it as much as the mesh builder is.
Shader "Fallcall/VFX/T0_CloudGeo"
{
    Properties
    {
        // Wrap diffuse, not Lambert. A hard terminator makes boxes read as boxes; wrapping the
        // falloff past 90 degrees fakes the way light bleeds through a cloud's edge and is what
        // stops a slab of cubes looking like a slab of cubes. Cheap approximation of a very
        // expensive thing (see T3's Henyey-Greenstein, which is the real version).
        _Wrap ("Light Wrap", Range(0, 1)) = 0.7
        _RimPower ("Rim Tightness", Range(0.5, 8)) = 3
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Same include the sky uses. _SunDirection and the rows come from here; nothing about
            // the time of day is re-derived locally.
            #include "SkyRamp.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Wrap;
                float _RimPower;
                float _RimStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                // Normals are flat per face and the mesh is only ever translated, never scaled or
                // rotated — but go through the normal matrix anyway rather than betting on that.
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);
                float3 viewDir = normalize(GetWorldSpaceViewDir(IN.positionWS));

                half3 lit = SampleRamp(ROW_CLOUD_LIT);
                half3 shadow = SampleCloudShadow();

                // Wrap: remap N.L from [-1,1] into [0,1] with _Wrap controlling how far past the
                // terminator the light reaches. _Wrap = 0 is plain half-Lambert; 1 lights the whole
                // thing evenly. The faces still read distinctly because each one has a different
                // normal — the goal is softening the steps, not erasing them.
                float ndl = dot(n, _SunDirection.xyz);
                float wrapped = saturate((ndl + _Wrap) / (1.0 + _Wrap));

                half3 col = lerp(shadow, lit, wrapped);

                // Rim: cloud edges scatter light forward, so the silhouette should be brighter than
                // the mass. On flat-shaded boxes this also does real work outlining the facets.
                float rim = pow(1.0 - saturate(dot(n, viewDir)), _RimPower);
                col += lit * rim * _RimStrength * saturate(ndl + _Wrap);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
