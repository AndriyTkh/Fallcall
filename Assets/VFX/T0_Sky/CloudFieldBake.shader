// Bakes the cloud field to a texture, so the CPU can read it and build geometry from it.
//
// This is the bridge in "the field is HLSL, the mesh builder is C#". It does not evaluate *a* field
// — it evaluates *the* field, by calling the same CloudField2D() that the sky's fragment shader
// calls, under the same global keyword. That is the entire reason this file exists rather than a
// C# port of the noise: a port would be a second implementation, and a second implementation drifts
// the moment someone edits Noise.hlsl and forgets it. Here there is nothing to keep in sync.
//
// The result is also the *weather map* that _VFX.md specifies for T2 (spawn density) and T3
// (coverage). Built here early, for the same reason T0 built the LUT first: it is a spine, and
// things hang off it.
Shader "Fallcall/VFX/CloudFieldBake"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            // Same three variants as the sky. CloudField2D switches on these, and CloudControls sets
            // them globally — so this material picks up the basis with no wiring at all.
            #pragma multi_compile_fragment _CLOUDBASIS_VALUE _CLOUDBASIS_WORLEY _CLOUDBASIS_ERODE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "CloudField.hlsl"

            // Which slice of the world this bake covers. A material property, not a global: it is
            // this bake's business, not the weather's. xy = world-XZ origin, zw = world-XZ size.
            float4 _BakeWorldRect;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                // Texel -> world XZ -> noise space. The *2D grid* is the only difference from the
                // sky, which gets its world XZ by projecting a view ray onto a plane instead.
                float2 worldXZ = _BakeWorldRect.xy + IN.uv * _BakeWorldRect.zw;

                // NOTE: no _CloudWindOffset here, and that is deliberate and load-bearing.
                //
                // The sky adds wind into its uv, so its clouds drift because the *field* slides
                // under a fixed view. The extruder cannot afford that: wind changes every time
                // TimeOfDay moves, and folding it in here would re-bake and re-mesh every frame of
                // a scrub.
                //
                // Instead the field is baked wind-free, in its own space, and CloudExtrude moves the
                // *transform* to account for wind. Static field, moving frame — which is how
                // Minecraft's clouds work too. The two consumers agree because it is the same field;
                // they just disagree about who moves. See CloudExtrude.WindWorldOffset for the
                // conversion that keeps them registered.
                float2 uv = worldXZ * _CloudScale;

                // Raw field, un-thresholded. Coverage is the consumer's business — see CloudField.hlsl.
                return float4(CloudField2D(uv), 0, 0, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
