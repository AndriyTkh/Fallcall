// T2 — the scattered primitive. Instanced, flat-shaded, lit off the same LUT rows as every other
// cloud in the sky.
//
// Reads ROW_CLOUD_LIT and ROW_CLOUD_SHADOW via ../T0_Sky/SkyRamp.hlsl — the LUT contract, and the
// reason a box and T0's painted layer can't disagree about the time of day. A new tier adds a row,
// never a second LUT. This shader is T0_CloudGeo with three things added: GPU instancing, a
// switchable blend/cull/depth state, and a per-instance param for density-driven alpha and shade.
//
// It deliberately contains no noise and never asks where it is in the cloud: CloudBoxes.cs asks the
// field once, on the CPU, and hands the answer down as an instance param. Same field, different
// consumer — the box shader is not a fourth opinion about the weather.
//
// Blend state is driven from C# (_SrcBlend/_DstBlend/_ZWrite/_Cull + renderQueue), not by two
// SubShaders. Opaque is the cheap branch and the one that should ship: overlap is nearly free
// because early-Z kills buried fragments before they shade. Transparent inverts that — full fill
// cost per layer, plus it needs sorting (CloudBoxes does it per camera). It buys density
// accumulation for free, which is Beer's law by accident.
Shader "Fallcall/VFX/T2_CloudBox"
{
    Properties
    {
        // Wrap diffuse, not Lambert. A hard terminator makes boxes read as boxes; wrapping the
        // falloff past 90 degrees fakes light bleeding through a cloud's edge. Cheap stand-in for a
        // very expensive thing (T3's Henyey-Greenstein is the real version).
        _Wrap ("Light Wrap", Range(0, 1)) = 0.7
        _RimPower ("Rim Tightness", Range(0.5, 8)) = 3
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.5

        _Alpha ("Alpha", Range(0, 1)) = 1
        // How much of the alpha comes from how deep in the cloud the box was placed. Edge boxes go
        // wispy, cores stay solid — the thing transparency is actually good for.
        _AlphaFromDensity ("Alpha From Density", Range(0, 1)) = 0.6
        // Per-box brightness variation. Without it a cluster of boxes sharing one normal reads as a
        // single flat surface, which is exactly the look the scatter exists to avoid.
        _ShadeJitter ("Shade Jitter", Range(0, 1)) = 0.15

        // Driven by CloudBoxes.cs. Hidden because setting them by hand desyncs them from the
        // component's Surface enum, which also owns the render queue.
        [HideInInspector] _SrcBlend ("", Float) = 1
        [HideInInspector] _DstBlend ("", Float) = 0
        [HideInInspector] _ZWrite ("", Float) = 1
        [HideInInspector] _Cull ("", Float) = 2
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

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // The same include the sky and T0.5's geometry use. Nothing about the time of day is
            // re-derived locally.
            #include "../T0_Sky/SkyRamp.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Wrap;
                float _RimPower;
                float _RimStrength;
                float _Alpha;
                float _AlphaFromDensity;
                float _ShadeJitter;
                float _SrcBlend;
                float _DstBlend;
                float _ZWrite;
                float _Cull;
            CBUFFER_END

            // x = field density at the box's centre (0 at the cloud edge, 1 in a core).
            // y = per-box hash, 0..1.
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstanceParams)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                // Goes through the normal matrix because it has to: instances carry non-uniform
                // scale (the Stretch knob), and a plain rotate would skew these off the surface.
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                float4 inst = UNITY_ACCESS_INSTANCED_PROP(Props, _InstanceParams);
                float density = inst.x;
                float hash = inst.y;

                float3 n = normalize(IN.normalWS);
                // Quads are drawn with Cull Off, so half of every one of them is seen from behind
                // and would shade as if lit from the wrong side. Flipping on the facing sign costs
                // nothing and is the only reason the Quad primitive isn't half-black.
                n *= IS_FRONT_VFACE(facing, 1.0, -1.0);

                float3 viewDir = normalize(GetWorldSpaceViewDir(IN.positionWS));

                half3 lit = SampleRamp(ROW_CLOUD_LIT);
                half3 shadow = SampleCloudShadow();

                // Wrap: remap N.L from [-1,1] into [0,1], _Wrap deciding how far past the terminator
                // light reaches. 0 is plain half-Lambert, 1 lights everything evenly. Faces still
                // read distinctly because each has its own normal — this softens the steps, it does
                // not erase them.
                float ndl = dot(n, _SunDirection.xyz);
                float wrapped = saturate((ndl + _Wrap) / (1.0 + _Wrap));

                half3 col = lerp(shadow, lit, wrapped);

                // Rim: cloud edges scatter forward, so silhouettes are brighter than the mass. On
                // flat-shaded boxes it also does real work outlining the facets.
                float rim = pow(1.0 - saturate(dot(n, viewDir)), _RimPower);
                col += lit * rim * _RimStrength * saturate(ndl + _Wrap);

                // Per-box brightness, centred on 1 so the jitter knob doesn't also dim the sky.
                col *= 1.0 + (hash - 0.5) * 2.0 * _ShadeJitter;

                float alpha = _Alpha * lerp(1.0, density, _AlphaFromDensity);

                // Opaque runs with Blend One Zero, so this alpha is ignored there rather than
                // needing its own variant.
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
