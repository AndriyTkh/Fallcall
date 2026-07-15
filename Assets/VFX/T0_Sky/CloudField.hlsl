// The cloud field: one definition, every consumer.
//
// **This file is the whole reason T0.5 works.** The field lives in HLSL, but T0.5 builds a mesh on
// the CPU, so the field has to reach C# somehow. The options were: port the noise to C# (two
// implementations, silent drift), or evaluate the *same* HLSL and read the answer back. The second
// was chosen — see _VFX_PLAN.md — and that only means anything if "the same HLSL" is literally one
// piece of code. Two shaders now sample this field:
//
//   T0_Sky.shader        — per view-ray, per pixel   ("what colour is this ray?")
//   CloudFieldBake.shader — per grid cell, once       ("where is there cloud?")
//
// Same field, different consumer. That sentence is the T0.5 lesson, and it is only true because the
// basis dispatch below exists exactly once. Copy it into a second shader and the two tiers become
// two fields that merely look alike, which makes the whole field-vs-placement comparison a lie.
#ifndef FALLCALL_CLOUDFIELD_INCLUDED
#define FALLCALL_CLOUDFIELD_INCLUDED

#include "Noise.hlsl"

// ---- Globals, pushed by CloudControls.cs --------------------------------------------------------
// Outside UnityPerMaterial, like every global. These were material properties until 2026-07-15,
// which made them unreachable: TimeOfDaySky builds the sky material at runtime with HideAndDontSave,
// so it has no inspector and never had one. See _VFX.md, "Weather cycle is a state machine".
//
// A consumer is free to ignore the ones it doesn't need — the bake never reads _CloudHeight, the
// sky never reads it on the CPU. Unused uniforms cost nothing.
float _CloudHeight;
float _CloudScale;
float _CloudCoverage;
float _CloudSoftness;
float _CloudOctaves;
float _CloudLacunarity;
float _CloudGain;
float _CloudErode;
float _CloudHorizonFade;
float _CloudSunGlow;
float4 _CloudWindOffset;   // xy = cloud-layer drift. A function of TimeOfDay, not _Time, so it scrubs.

// ---------------------------------------------------------------------------------------------
// The field, in noise space. `uv` is already scaled and wind-offset by the caller — this function
// deliberately knows nothing about where the sample came from, which is what lets a view-ray and a
// grid cell ask the same question.
//
// Basis is compile-time (`_CLOUDBASIS_*`, global keywords set by CloudControls). Any shader that
// includes this must declare:
//
//     #pragma multi_compile_fragment _CLOUDBASIS_VALUE _CLOUDBASIS_WORLEY _CLOUDBASIS_ERODE
//
// multi_compile and not shader_feature: shader_feature strips variants by scanning materials, and
// both consumers' materials are generated at runtime, so the scan sees nothing and strips
// everything but the default. See _VFX.md.
//
// Returns the raw field, 0..1. **Not** thresholded — coverage is the consumer's business, and that
// separation is the point: the sky smoothsteps it into an alpha, the extruder compares it to pick
// which cells exist. One field, two answers.
// ---------------------------------------------------------------------------------------------
float CloudField2D(float2 uv)
{
    int octaves = (int)_CloudOctaves;

#if defined(_CLOUDBASIS_WORLEY)
    return WorleyFbm2D(uv, octaves, _CloudLacunarity, _CloudGain);
#elif defined(_CLOUDBASIS_ERODE)
    return ErodeFbm2D(uv, octaves, _CloudLacunarity, _CloudGain, _CloudErode);
#else
    return Fbm2D(uv, octaves, _CloudLacunarity, _CloudGain);
#endif
}

#endif // FALLCALL_CLOUDFIELD_INCLUDED
