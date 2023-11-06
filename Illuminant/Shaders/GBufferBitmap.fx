#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\GeometryCommon.fxh"
#include "GBufferShaderCommon.fxh"

Texture2D Mask : register(t0);
sampler MaskSampler : register(s0) {
    Texture = (Mask);
    AddressU = CLAMP;
    AddressV = CLAMP;
};

void BillboardVertexShader(
    in    float3 position       : POSITION0, // x, y, z
    inout float2 texCoord       : TEXCOORD0,
    inout float3 normal         : NORMAL0,
    inout float2 dataScaleAndDynamicFlag : NORMAL1,
    inout float3 worldPosition  : TEXCOORD1,
    out   float3 screenPosition : TEXCOORD2,
    out   float4 result         : POSITION0
) {
    // HACK: Offset away from the surface to prevent self occlusion
    worldPosition += (SelfOcclusionHack * normal);
    screenPosition = position;

    result = TransformPosition(float4((position.xy - GetViewportPosition()) * GetViewportScale(), 0, 1), 0);
    result.z = position.z / DistanceFieldExtent.z;
    result.w = 1;
}

void MaskBillboardPixelShader(
    in float2 texCoord       : TEXCOORD0,
    in float3 worldPosition  : TEXCOORD1,
    in float3 screenPosition : TEXCOORD2,
    in float3 normal         : NORMAL0,
    in float2 dataScaleAndDynamicFlag : NORMAL1,
    out float4 result        : COLOR0
) {
    float alpha = tex2D(MaskSampler, texCoord).a;

    const float discardThreshold = (1.0 / 255.0);
    clip(alpha - discardThreshold);

    float dataScale = dataScaleAndDynamicFlag.x;

    float wp = worldPosition.y;
    float sp = screenPosition.y;
    float relativeY = (wp - sp) * dataScale;

    // HACK: We drop the world x axis and the normal y axis,
    //  and reconstruct those two values when sampling the g-buffer
    // FIXME: This is the old encoding!
    result = float4(
        (normal.x / 2) + 0.5,
        (normal.z / 2) + 0.5,
        relativeY,
        ((worldPosition.z + GBUFFER_Z_OFFSET) / GBUFFER_Z_SCALE) * dataScaleAndDynamicFlag.y
    );
}

void GDataBillboardPixelShader(
    in float2 texCoord       : TEXCOORD0,
    in float3 worldPosition  : TEXCOORD1,
    in float3 screenPosition : TEXCOORD2,
    in float3 normalIn       : NORMAL0,
    in float2 dataScaleAndDynamicFlag : NORMAL1,
    out float4 result        : COLOR0
) {
    // x normal, y normal, z offset, mask
    float4 data = tex2D(MaskSampler, texCoord);
    float alpha = data.a;

    const float discardThreshold = (127.0 / 255.0);
    if (alpha < discardThreshold) {
        result = 0;
        discard;
        return;
    }

    float dataScale = dataScaleAndDynamicFlag.x;
    float2 tangentSpaceNormalIn = float3(data.rg, 0);
    float3 tangentSpaceNormal = float3((tangentSpaceNormalIn - 0.5) * 2, 0);
    tangentSpaceNormal.z = sqrt(1 - dot(tangentSpaceNormal.xy, tangentSpaceNormal.xy));
    
    // HACK: Use a fixed space where +x is right, -y is up, and +z is forward. Then
    //  convert the billboard's "normals" from this "tangent space" to world space
    // The billboard's normals have Z reconstructed from x/y so if x/y are both 0
    //  they will point 'forward' and produce +z as one would typically want
    float3 tangent = float3(1, 0, 0),
        bitangent = float3(0, -1, 0),
        normal = float3(0, 0, 1),
        worldSpaceNormal = tangent * tangentSpaceNormal.x + 
            bitangent * tangentSpaceNormal.y + 
            normal * tangentSpaceNormal.z;
        
    float3 resultNormal = normalize(worldSpaceNormal);

#if 0
    result = float4((resultNormal * 0.5) + 0.5, 1);
    return;
#else    
    float effectiveZ = worldPosition.z + (data.b * dataScale);
    float yOffset = effectiveZ * getZToYMultiplier();

    result = encodeGBufferSample(
        resultNormal,
        yOffset,
        effectiveZ,
        false, true, false
    );
#endif
}

technique MaskBillboard
{
    pass P0
    {
        vertexShader = compile vs_3_0 BillboardVertexShader();
        pixelShader = compile ps_3_0 MaskBillboardPixelShader();
    }
}

technique GDataBillboard
{
    pass P0
    {
        vertexShader = compile vs_3_0 BillboardVertexShader();
        pixelShader = compile ps_3_0 GDataBillboardPixelShader();
    }
}