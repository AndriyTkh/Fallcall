// The LUT contract, GPU side. One home for the ramp globals, the row indices and the sampler, so a
// shader that wants a weather colour includes this instead of copying the rows.
//
// This exists because T0.5 became the *second* consumer. With one shader the rows could live inline;
// with two, an inline copy is a second opinion about what row 5 means, and two clouds in one sky
// disagreeing about the time of day is exactly the failure the LUT contract exists to prevent.
//
// A later tier that needs a new colour adds a ROW here. It does not grow a second LUT.
//
// Still mirrored by hand against SkyRamp.cs — change one, change both. That seam is unavoidable
// (C# and HLSL cannot share a constant) and it is now the only one.
#ifndef FALLCALL_SKYRAMP_INCLUDED
#define FALLCALL_SKYRAMP_INCLUDED

// ---- Globals, pushed by TimeOfDaySky.cs ---------------------------------------------------------
// Outside UnityPerMaterial on purpose: they are set once for the whole frame, not per material, and
// a SetGlobal value inside that CBUFFER breaks SRP Batcher compatibility.
TEXTURE2D(_SkyRampLut);
SAMPLER(sampler_SkyRampLut);
float  _SunElevation01;
float4 _SunDirection;

// Pushed by CloudControls, not TimeOfDaySky — it's a cloud knob (`_Cloud*`), and it lives in this
// file rather than in each shader because it modifies a ROW, and rows are this file's business.
float _CloudShadowLift;

#define RAMP_ROWS         7.0
#define ROW_SKY_TOP       0.0
#define ROW_HORIZON       1.0
#define ROW_FOG           2.0
#define ROW_AMBIENT       3.0
#define ROW_SUN_LIGHT     4.0
#define ROW_CLOUD_LIT     5.0
#define ROW_CLOUD_SHADOW  6.0

// One LUT read. x = sun height (the same for every pixel this frame), y = which channel.
// The +0.5 lands on the row's centre so bilinear filtering can't bleed neighbouring rows in.
half3 SampleRamp(float row)
{
    float2 uv = float2(_SunElevation01, (row + 0.5) / RAMP_ROWS);
    return SAMPLE_TEXTURE2D_LOD(_SkyRampLut, sampler_SkyRampLut, uv, 0).rgb;
}

// The cloud shadow colour, lifted toward the lit colour by _CloudShadowLift. **Every cloud tier
// calls this instead of SampleRamp(ROW_CLOUD_SHADOW)** — the lift is a property of the weather, not
// of a tier, and a tier that sampled the raw row would sit in the same sky shaded differently.
//
// It lifts toward ROW_CLOUD_LIT and not ROW_SUN_LIGHT on purpose. The sun row is the *light*; the
// two cloud rows are what a cloud already looks like under it, gamma and all. Collapsing shadow onto
// lit is "this cloud is barely self-shadowed at all" — which is the question being asked. Reaching
// for the sun row instead would smuggle a light colour into an albedo and go wrong at dawn, when the
// sun is orange and the cloud is not.
//
// At 1 the two rows are the same colour and clouds go unshaded: wrap diffuse, rim and shade jitter
// all still run and all still cost, they just have nothing left to interpolate. That is a legitimate
// flat-lit look, not a bug — but if the boxes have gone strangely flat, look here before the normals.
half3 SampleCloudShadow()
{
    return lerp(SampleRamp(ROW_CLOUD_SHADOW), SampleRamp(ROW_CLOUD_LIT), saturate(_CloudShadowLift));
}

#endif // FALLCALL_SKYRAMP_INCLUDED
