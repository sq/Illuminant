#include "..\..\..\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"
#include "ParticleCommon.fxh"

uniform float2 SourceCoordinateOffset;

uniform float2 Size;

Texture2D BitmapTexture;
sampler BitmapSampler {
    Texture = (BitmapTexture);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MipFilter = LINEAR;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

uniform float4 BitmapTextureRegion;

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
    out float2 texCoord    : TEXCOORD0,
    out float4 attributes  : COLOR0,
    out float2 _xy         : POSITION1
) {
    float2 actualXy = xy + SourceCoordinateOffset + offset;
    float4 position, velocity;
    readState(actualXy, position, velocity, attributes);

    // FIXME
    result = TransformPosition(
        float4(position.xyz + (Corners[cornerIndex.x] * float3(Size, 0)), 1), 0
    );
    texCoord = (Corners[cornerIndex.x].xy / 2) + 0.5;
    texCoord = lerp(BitmapTextureRegion.xy, BitmapTextureRegion.zw, texCoord);
    _xy = actualXy;
}

void PS_White (
    in  float2 xy       : POSITION1,
    in  float2 texCoord : TEXCOORD0,
    out float4 result   : COLOR0
) {
    // FIXME
    float4 texColor = tex2D(BitmapSampler, texCoord);
    // float4 tint = float4(xy, 1, 1);
    result = texColor;
}

void PS_AttributeColor (
    in  float2 xy       : POSITION1,
    in  float2 texCoord : TEXCOORD0,
    in  float4 color    : COLOR0,
    out float4 result   : COLOR0
) {
    // FIXME
    float4 texColor = tex2D(BitmapSampler, texCoord);
    result = texColor * color;
}

technique White {
    pass P0
    {
        vertexShader = compile vs_3_0 vs();
        pixelShader = compile ps_3_0 PS_White();
    }
}

technique AttributeColor {
    pass P0
    {
        vertexShader = compile vs_3_0 vs();
        pixelShader = compile ps_3_0 PS_AttributeColor();
    }
}
