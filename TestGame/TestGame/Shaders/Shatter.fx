// https://www.shadertoy.com/view/Nd3cz2

#include "..\..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "..\..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\..\Fracture\Squared\RenderLib\Shaders\FormatCommon.fxh"
#include "..\..\..\..\Fracture\Squared\RenderLib\Shaders\BitmapCommon.fxh"
#include "..\..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "..\..\..\..\Fracture\Squared\RenderLib\Shaders\sRGBCommon.fxh"

#define PHASE_POWER 2.0

uniform float Time;

float2 hash2 (float2 p) {
    // texture based white noise
    // return texture(iChannel0, (p + 0.5) / 256.0, -100.0).xy;

    // procedural white noise
    return frac(sin(float2(dot(p,float2(127.1,311.7)),dot(p,float2(269.5,183.3))))*43758.5453);
}

float4 voronoi (in float2 x) {
    float2 n = floor(x);
    float2 f = frac(x);
    float2 o;
    //----------------------------------
    // first pass: regular voronoi
    //----------------------------------
    float2 mg, mr;
    float oldDist;

    float md = 8.0;
    for (int j = -1; j <= 1; j++)
        for (int i = -1; i <= 1; i++)
        {
            float2 g = float2(float(i), float(j));
            o = hash2(n + g);
            float2 r = g + o - f;
            float d = dot(r, r);

            if (d < md)
            {
                md = d;
                mr = r;
                mg = g;
            }
        }

    oldDist = md;

    //----------------------------------
    // second pass: distance to borders
    //----------------------------------
    md = 8.0;
    for (int j = -2; j <= 2; j++)
        for (int i = -2; i <= 2; i++)
        {
            float2 g = mg + float2(float(i), float(j));
            o = hash2(n + g);
            float2 r = g + o - f;

            if (dot(mr - r, mr - r) > 0.00001)
                md = min(md, dot(0.5*(mr + r), normalize(r - mr)));
        }

    return float4(md, mr, oldDist);
}

void eval (
    float2 xy, float4 userData, out float4 c, out float cellPhase, out float phase
) {
    float2 zoom = 4 * abs(userData.y), p = (xy * 2 - 1) * zoom, sc;
    bool reverse = userData.y < 0;
    sincos(userData.z, sc.x, sc.y);
    // c = (min distance, ???, ???)
    c = voronoi(p + userData.w);
    // c.x = 1.0 - pow(1.0 - c.x, 2.0);

    float waveScale = 0.025,
        // lower values will make the band of unbroken surface larger
        bandSizeFactor = 2.0,
        bandSizeFactor2 = 2.0,
        // higher value = more prominent sine wave ripple
        sineStrength = 0.33,
        // higher value = higher sine wave frequency and weirder output
        sineRate = 0.9,
        sineOffset = (userData.w * 1.73),
        xScale = 0.8,
        yScale = 0.4;

    // no idea why these factors are here
    float offset = 0.115, scale = 0.216,
        timeStep = (userData.x * scale) + offset,
        v1 = (c.z*xScale + c.y*yScale) * sineRate + sineOffset,
        s1 = sineStrength * sin(v1);

    // decreases 1->0 as cell falls away from the surface
    cellPhase = c.y + s1;
    cellPhase *= waveScale;
    cellPhase = clamp((cellPhase + timeStep) * bandSizeFactor, 0.0, 1.0);
    cellPhase = pow(clamp(cellPhase*bandSizeFactor2 - 0.5, 0.0, 1.0), PHASE_POWER);

    // fixme: why the hell doesn't this work
    if (reverse)
        cellPhase = 1 - cellPhase;

    phase = cellPhase;
}


void ShatterPixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    // progress, direction * scale, angle, offset
    in float4 userData : COLOR2,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    in float2 texCoord2 : TEXCOORD2,
    in float4 texRgn2 : TEXCOORD3,
    out float4 result : COLOR0
) {
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    float4 texColor = tex2Dbias(TextureSampler, float4(clamp2(texCoord, texRgn.xy, texRgn.zw), 0, MIP_BIAS));
    texColor = ExtractRgba(texColor, BitmapTraits);

    float edgeSizeDivisor = 0.25, edgeAmplification = 2.2,
        edgeSuppressionPower = 2.0;

    float cellPhase, phase;
    float4 c;
    eval((texCoord.xy - texRgn.xy) / (texRgn.zw - texRgn.xy), userData, c, cellPhase, phase);

    float3 col, fill, bg;
    float
        // as the pieces first start to fall we darken the edges to create the illusion
        //  of a shadow and distinguish them from the surface
        edgeDarkness = (1.0 - clamp((c.x - cellPhase) / edgeSizeDivisor, 0.0, 1.0)) * cellPhase * edgeAmplification,
        // as the piece falls away we want to stop darkening the edges
        edgeSuppression = pow(1.0 - clamp(cellPhase, 0.0, 1.0), edgeSuppressionPower),
        // and then we darken the entire pieces instead
        darkness = max(edgeDarkness * edgeSuppression, cellPhase),
        alpha = smoothstep(phase - 0.025, phase, c.x);

    float4 bgc = float4(0, 0.66, 0.2, 1);
    texColor = texColor + (bgc * (1 - texColor.a));
    texColor.rgb *= (1 - darkness);
    result = texColor * multiplyColor * alpha;
    result += (addColor * result.a);

    const float discardThreshold = (1.0 / 255.0);
    clip(result.a - discardThreshold);
}

technique ShatterTechnique
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 ShatterPixelShader();
    }
}
