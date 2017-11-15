#include "..\..\..\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"
#include "ParticleCommon.fxh"

uniform float2 SourceCoordinateOffset;

static const float3 Corners[] = {
    { -1, -1, 0 },
    { 1, -1, 0 },
    { 1, 1, 0 },
    { -1, 1, 0 }
};

void vs (
    in  float2 xy          : POSITION0,
    in  float2 offset      : POSITION1,
    in  int2   cornerIndex : BLENDINDICES0, // 0-3
    out float4 result      : POSITION0,
    out float2 _xy         : POSITION1
) {
    float2 actualXy = xy + SourceCoordinateOffset + offset;
    float4 position = tex2Dlod(PositionSampler, float4(actualXy + HalfTexel, 0, 0));
    // FIXME
    result = TransformPosition(
        float4(position.xyz + Corners[cornerIndex.x] * 0.8, 1), 0
    );
    _xy = actualXy;
}

void ps (
    in  float2 xy     : POSITION1,
    out float4 result : COLOR0
) {
    // FIXME
    result = float4(xy * 0.125, 0.1, 1);
}

technique RasterizeParticles {
    pass P0
    {
        vertexShader = compile vs_3_0 vs();
        pixelShader = compile ps_3_0 ps();
    }
}
