// Noise fundamentals. Hash -> value noise -> fbm, then hash -> worley -> worley fbm -> erode, in
// that order, because each one is built out of the one above it. Everything later in the ladder
// (T2's placement jitter, T3's raymarched density) is this file with more dimensions.
//
// The thing worth internalising: noise is not randomness. Randomness is `Hash`. Noise is randomness
// that has been made *continuous* — sampled on a lattice and interpolated between. And fbm is not a
// noise function at all; it is a *loop* that adds noise to itself at shrinking scales.
//
// The second thing, which the worley half is here to make concrete: **there are two ways to make a
// lattice continuous, and they are the same two ways the whole cloud ladder splits.** Value noise
// hashes a *value* per cell and interpolates between them — a field. Worley hashes a *point* per
// cell and shades by distance to the nearest one — placement. Shrink worley's cells and it reads as
// a field; grow them and it reads as scattered blobs. Field vs. placement is a dial, not a fork.
#ifndef FALLCALL_NOISE_INCLUDED
#define FALLCALL_NOISE_INCLUDED

// Bounds the fbm loop so the compiler can unroll it. A dynamic loop count in a fragment shader is a
// branch every pixel; a bounded one is straight-line code.
#define FBM_MAX_OCTAVES 8

// ---------------------------------------------------------------------------------------------
// Hash: coordinate in, repeatable garbage out.
//
// No state, no seed, no texture — the coordinate *is* the seed. Same input always gives the same
// output, which is the whole reason procedural noise can be evaluated per-pixel per-frame without
// storing anything. The magic constants are irrational-ish numbers chosen so that `frac` throws away
// any visible structure. They are not meaningful; they are just bad at making patterns.
// ---------------------------------------------------------------------------------------------
float Hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

// ---------------------------------------------------------------------------------------------
// Value noise: hash the lattice corners, interpolate between them.
//
// Split the coordinate into which cell it's in (floor) and where it sits inside that cell (frac).
// Hash the four corners, then blend. Blending with `f` directly gives visible creases at every cell
// edge, because the slope jumps as you cross. `f*f*(3-2f)` — smoothstep — has zero derivative at 0
// and 1, so the slopes match across the boundary and the seams vanish. That single line is the
// difference between "noise" and "a grid of triangles".
// ---------------------------------------------------------------------------------------------
float ValueNoise2D(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);

    float2 u = f * f * (3.0 - 2.0 * f);

    float a = Hash21(i + float2(0.0, 0.0));
    float b = Hash21(i + float2(1.0, 0.0));
    float c = Hash21(i + float2(0.0, 1.0));
    float d = Hash21(i + float2(1.0, 1.0));

    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// ---------------------------------------------------------------------------------------------
// fbm — fractional Brownian motion. Sum the same noise at doubling frequency and halving amplitude.
//
// One octave of value noise looks like blobs. Real clouds have big shapes with smaller shapes on
// their edges and smaller ones on those — detail at every scale you care to look. That is all fbm
// is: a loop that adds a shrunken copy of the noise to itself.
//
//   lacunarity — how much *finer* each octave is. 2.0 = each octave is twice the frequency.
//   gain       — how much *quieter* each octave is. 0.5 = each octave is half as loud.
//
// gain ~= 1/lacunarity gives the natural, cloud-like look. Push gain up and it goes noisy and
// electric; push it down and the fine octaves stop mattering and you've paid for nothing.
//
// The running `norm` matters: without it the output range depends on the octave count, so changing
// octaves would change the brightness and every threshold downstream would need re-tuning. Dividing
// by the summed amplitude keeps the result in 0..1 no matter how many octaves are asked for.
// ---------------------------------------------------------------------------------------------
float Fbm2D(float2 p, int octaves, float lacunarity, float gain)
{
    float sum = 0.0;
    float amp = 0.5;
    float norm = 0.0;

    [unroll(FBM_MAX_OCTAVES)]
    for (int i = 0; i < octaves; i++)
    {
        sum += amp * ValueNoise2D(p);
        norm += amp;
        p *= lacunarity;
        amp *= gain;
    }

    return sum / max(norm, 1e-5);
}

// ---------------------------------------------------------------------------------------------
// Hash22: coordinate in, repeatable *point* out.
//
// Same contract as Hash21, one dimension wider, because worley needs a position inside a cell
// rather than a value at a corner. Dave Hoskins' hash22 (https://www.shadertoy.com/view/4djSRW) —
// no `sin`, which matters because `sin`-based hashes drift between GPUs once the argument gets big.
//
// The output range is 0..1 and that is load-bearing, not cosmetic. See Worley2D.
// ---------------------------------------------------------------------------------------------
float2 Hash22(float2 p)
{
    float3 p3 = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

// Empirical. F1's *theoretical* bound is sqrt(2) — a query and its own cell's point at opposite
// corners — but that case is vanishingly rare, so normalising by it would leave the field bunched
// around 0.2 and every coverage threshold downstream would mean something different than it does
// for value noise. This constant is instead picked so the billow output centres near 0.5, like
// value noise does, which is what lets _CloudCoverage keep its meaning across a basis swap.
// Starting value only — verify step 2 (coverage behaves at the same knob positions) is what
// confirms it. If coverage shifts when you switch basis, retune *here*, not on the material.
#define WORLEY_F1_NORM 0.72

// ---------------------------------------------------------------------------------------------
// Worley (cellular) noise: hash one feature point per cell, return distance to the nearest.
//
// This is placement wearing a field's clothes. There is no interpolation anywhere in it — the
// continuity comes free, because distance-to-a-point is already continuous. Value noise had to
// work for its smoothness (that's what the smoothstep was for); worley gets it for nothing and
// pays elsewhere: 9 hashes per sample against value noise's 4.
//
// Two details that are the whole function:
//
//   1. Search 3x3, not 1. The nearest feature point is very often in a *neighbouring* cell, not
//      the one you landed in. Skipping the neighbours gives a hard discontinuity on every cell
//      line, which reads as a perfect square grid — the exact artefact noise exists to avoid.
//
//   2. The jitter must stay *inside* its own cell, which is why Hash22's 0..1 range is not
//      negotiable. Let a point wander outside its cell and a 3x3 search no longer guarantees the
//      true nearest point is in the search set: F1 jumps, and you get hard *straight* seams along
//      cell boundaries. This is THE worley bug. If you see straight edges, look here first.
//
// Compare squared distances in the loop and take one sqrt at the end. `min` commutes with `sqrt`
// because sqrt is monotonic, so a per-cell sqrt is pure waste — 9 of them per octave.
// ---------------------------------------------------------------------------------------------
float Worley2D(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);

    float minDistSq = 8.0;   // any value past the 3x3 search's reach

    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            float2 cell = float2(x, y);
            float2 featurePoint = Hash22(i + cell);   // 0..1 -> stays inside its own cell
            float2 diff = cell + featurePoint - f;
            minDistSq = min(minDistSq, dot(diff, diff));
        }
    }

    return sqrt(minDistSq);
}

// ---------------------------------------------------------------------------------------------
// Billows: worley, inverted.
//
// F1 is 0 *at* a feature point and grows outward, so raw worley is dark blobs joined by bright
// veins — a Voronoi diagram. Correct, and useless for clouds. Inverting puts a bright billow on
// each feature point instead, which is the rounded, clumped look that reads as cloud and that
// value noise cannot do at any octave count.
// ---------------------------------------------------------------------------------------------
float WorleyBillow2D(float2 p)
{
    return saturate(1.0 - Worley2D(p) / WORLEY_F1_NORM);
}

// ---------------------------------------------------------------------------------------------
// fbm over the billow basis. Deliberately a copy of Fbm2D's loop with one line changed.
//
// Not factored into a shared loop: HLSL has no function pointers, and the alternatives (a macro,
// or a basis-select branch inside the hot loop) both cost more than they save. The duplication is
// the cheap option. The `norm` accumulator carries over for exactly the reason it exists in
// Fbm2D — octave count must not change output brightness, or every threshold downstream needs
// re-tuning whenever an octave is added.
// ---------------------------------------------------------------------------------------------
float WorleyFbm2D(float2 p, int octaves, float lacunarity, float gain)
{
    float sum = 0.0;
    float amp = 0.5;
    float norm = 0.0;

    [unroll(FBM_MAX_OCTAVES)]
    for (int i = 0; i < octaves; i++)
    {
        sum += amp * WorleyBillow2D(p);
        norm += amp;
        p *= lacunarity;
        amp *= gain;
    }

    return sum / max(norm, 1e-5);
}

float Remap(float v, float lo, float hi, float newLo, float newHi)
{
    return newLo + (v - lo) * (newHi - newLo) / max(hi - lo, 1e-5);
}

// ---------------------------------------------------------------------------------------------
// Erode: worley bites chunks out of value noise. This is the one that should look like clouds,
// and it is 2D practice for T3's perlin-worley — there, the same remap runs per raymarch step.
//
// Value fbm carries the big masses; a finer billow field raises the *floor* the mass has to clear.
// Remap(base, floor, 1, 0, 1) rescales base from [floor..1] into [0..1], so:
//
//   base ~ 1 (cloud cores)  -> survives untouched, whatever the floor is
//   base ~ floor (edges)    -> pushed to 0, and the threshold downstream cuts it
//
// So erosion lands exactly where a cloud is marginal and leaves the cores alone, which is what
// distinguishes it from just multiplying two noise fields together (that dims everything evenly).
//
// strength = 0 collapses this to `Remap(base, 0, 1, 0, 1)` == base == the VALUE basis, bit for
// bit. That is a free correctness check: if BASIS=Erode at strength 0 doesn't match BASIS=Value,
// something in here is wrong before you have even started judging the look.
// ---------------------------------------------------------------------------------------------
float ErodeFbm2D(float2 p, int octaves, float lacunarity, float gain, float strength)
{
    float base = Fbm2D(p, octaves, lacunarity, gain);

    // Finer and fewer: the billows are edge detail on the mass, not a second mass. Sharing p's
    // frame (no offset) keeps them registered to the shape they are carving instead of crawling
    // across it.
    float billow = WorleyFbm2D(p * 2.0, max(octaves - 2, 1), lacunarity, gain);

    return saturate(Remap(base, billow * strength, 1.0, 0.0, 1.0));
}

#endif // FALLCALL_NOISE_INCLUDED
