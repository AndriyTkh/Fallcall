// T0 sky gradient. The whole tier in one pass: turn the view direction's height into a colour by
// blending three ramp rows, then add a sun disc so the elevation maths is visible on screen.
//
// Every input is a global: TimeOfDaySky.cs pushes the sky/sun/wind ones (_SkyRampLut,
// _SunElevation01, _SunDirection, _CloudWindOffset), CloudControls.cs pushes the cloud ones
// (_Cloud*) plus the basis keywords. This shader never asks what time it is or what the weather is;
// it is told. Which is also why it has almost no material properties — see Properties.
Shader "Fallcall/VFX/T0_Sky"
{
    Properties
    {
        _HorizonPower ("Horizon Falloff", Range(0.1, 8)) = 2.5
        _GroundPower  ("Ground Falloff",  Range(0.1, 8)) = 2.0
        _SunSize      ("Sun Angular Size", Range(0.0005, 0.2)) = 0.02
        _SunGlowPower ("Sun Glow Tightness", Range(1, 4096)) = 400
        _SunDiscBoost ("Sun Disc Intensity", Range(0, 30)) = 6

        // No cloud properties here on purpose. Every cloud knob is a global pushed by
        // CloudControls.cs — see the globals block below for why this material has no inspector to
        // put them in.
    }

    SubShader
    {
        // Background queue + no depth write: the sky is drawn behind everything and occludes nothing.
        // Cull Off because the camera sits inside the skybox mesh and only ever sees its back faces.
        Tags
        {
            "RenderType" = "Background"
            "Queue" = "Background"
            "RenderPipeline" = "UniversalPipeline"
            "PreviewType" = "Skybox"
        }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            // multi_compile, not shader_feature, and global, not _local — both deliberate:
            //
            // shader_feature strips variants by scanning materials for which keywords are set. This
            // shader's material is built at runtime by TimeOfDaySky with HideAndDontSave, so it is
            // invisible to that scan: every variant but the default would be stripped from a build,
            // and basis switching would work in the Editor and silently do nothing in a player.
            // multi_compile always compiles all of them. Six variants of a skybox is nothing.
            //
            // _local likewise can't work: local keywords are per-material state, and there is no
            // material to set them on. CloudControls drives these via Shader.SetKeyword.
            //
            // Basis stays compile-time rather than a uniform branch: a per-pixel branch on a value
            // that's constant for the whole frame would cost the branch *and* keep all three bodies
            // resident. Three cheap variants beat one fat one.
            #pragma multi_compile_fragment _ _CLOUD_LAYER
            #pragma multi_compile_fragment _CLOUDBASIS_VALUE _CLOUDBASIS_WORLEY _CLOUDBASIS_ERODE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // The ramp globals/rows and the cloud globals/field used to be declared inline here.
            // T0.5 made this shader the first of several consumers, so they moved into shared
            // includes: an inline copy is a second opinion about what the weather is.
            #include "SkyRamp.hlsl"
            #include "CloudField.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _HorizonPower;
                float _GroundPower;
                float _SunSize;
                float _SunGlowPower;
                float _SunDiscBoost;
            CBUFFER_END

#ifdef _CLOUD_LAYER
            // Project the view ray onto a flat plane at _CloudHeight and shade an fbm field on it.
            //
            // This is T0's defining limitation, made visible: the plane is anchored to the camera, not
            // to the world, so it can never parallax. Move and the clouds come with you. That is fine
            // for a sky dome and fatal for anything meant to sit *around* the tower — which is exactly
            // why the ladder continues to T1/T2 instead of stopping here.
            //
            // Returns: rgb = cloud colour, a = coverage 0..1.
            half4 CloudLayer(float3 dir, float sunDot)
            {
                // dir.y -> 0 at the horizon, so t -> infinity: the plane compresses into an infinitely
                // dense band right where it meets the sky. That singularity is real, not a bug, and it
                // is what _CloudHorizonFade exists to hide.
                float t = _CloudHeight / max(dir.y, 1e-4);
                float2 uv = dir.xz * t * _CloudScale + _CloudWindOffset.xy;

                // The field. This one call is what T0.5's mesh builder also asks, via the bake —
                // same function, same globals, same basis keyword. This shader turns the answer
                // into a colour; the extruder turns it into geometry.
                float n = CloudField2D(uv);

                // Coverage is a threshold on the noise field, not a multiply. Multiplying fades every
                // cloud toward transparent together; thresholding makes them *grow and merge*, which is
                // what an overcast sky actually does. Same field, same T2 will use, different consumer.
                float threshold = 1.0 - _CloudCoverage;
                float cover = smoothstep(threshold, threshold + _CloudSoftness, n);

                // Fake self-shadowing: treat depth past the threshold as thickness. Thin wisps read as
                // shadow, thick cores as lit. No light transport, but it costs nothing and reads right.
                float thickness = saturate((n - threshold) / max(_CloudSoftness, 1e-4));
                half3 col = lerp(SampleCloudShadow(), SampleRamp(ROW_CLOUD_LIT), thickness);

                // Thin cloud near the sun should glow — light punches through the wisps, not the cores.
                col += SampleRamp(ROW_SUN_LIGHT) * pow(sunDot, 8.0) * (1.0 - thickness) * _CloudSunGlow;

                cover *= smoothstep(0.0, _CloudHorizonFade, dir.y);
                return half4(col, cover);
            }
#endif

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dirWS      : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                // The skybox mesh is centred on the camera and axis-aligned to the world, so a vertex's
                // object-space position *is* the world direction the camera looks to hit it. No matrix needed.
                OUT.dirWS = IN.positionOS.xyz;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 dir = normalize(IN.dirWS);

                half3 skyTop  = SampleRamp(ROW_SKY_TOP);
                half3 horizon = SampleRamp(ROW_HORIZON);
                half3 fog     = SampleRamp(ROW_FOG);

                // dot(viewDir, up) — and in a Y-up world that dot product is just dir.y. This one line is
                // the whole T0 gradient; everything else here is decoration.
                float h = dir.y;

                // pow() biases the blend toward the horizon: linear puts the interesting band in a thin
                // sliver of screen, a power curve stretches it into the space it deserves.
                half3 col = h >= 0.0
                    ? lerp(horizon, skyTop, pow(saturate(h), _HorizonPower))
                    : lerp(horizon, fog,    pow(saturate(-h), _GroundPower));

                // Sun disc + glow. Not part of the gradient, but it is the only on-screen proof that the
                // azimuth/elevation maths and the LUT coordinate actually agree — without it, a sun
                // rising in the wrong compass direction looks completely fine.
                float sunDot = saturate(dot(dir, _SunDirection.xyz));
                float disc = smoothstep(1.0 - _SunSize, 1.0 - _SunSize * 0.5, sunDot);
                float glow = pow(sunDot, _SunGlowPower);
                col += SampleRamp(ROW_SUN_LIGHT) * (disc + glow) * _SunDiscBoost;

#ifdef _CLOUD_LAYER
                // Composited last, over the finished sky *including* the sun disc — so clouds occlude
                // the sun rather than being lit through it.
                half4 clouds = CloudLayer(dir, sunDot);
                col = lerp(col, clouds.rgb, clouds.a);
#endif

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
