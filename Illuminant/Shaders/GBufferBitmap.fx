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

float4 yawPitchRoll(float yaw, float pitch, float roll) {
    float halfRoll = roll * 0.5;
    float sinRoll = sin(halfRoll);
    float cosRoll = cos(halfRoll);
    float halfPitch = pitch * 0.5;
    float sinPitch = sin(halfPitch);
    float cosPitch = cos(halfPitch);
    float halfYaw = yaw * 0.5;
    float sinYaw = sin(halfYaw);
    float cosYaw = cos(halfYaw);
    return float4(
        ((cosYaw * sinPitch) * cosRoll) + ((sinYaw * cosPitch) * sinRoll),
        ((sinYaw * cosPitch) * cosRoll) - ((cosYaw * sinPitch) * sinRoll),
        ((cosYaw * cosPitch) * sinRoll) - ((sinYaw * sinPitch) * cosRoll),
        ((cosYaw * cosPitch) * cosRoll) + ((sinYaw * sinPitch) * sinRoll)
    );
}

// Quaternion multiplication
// http://mathworld.wolfram.com/Quaternion.html
float4 qmul(float4 q1, float4 q2) {
    return float4(
        q2.xyz * q1.w + q1.xyz * q2.w + cross(q1.xyz, q2.xyz),
        q1.w * q2.w - dot(q1.xyz, q2.xyz)
    );
}

float3 rotateLocalPosition(float3 localPosition, float4 rotation) {
    float4 r_c = rotation * float4(-1, -1, -1, 1);
    return qmul(rotation, qmul(float4(localPosition, 0), r_c)).xyz;
}

void GDataBillboardPixelShader(
    in float2 texCoord       : TEXCOORD0,
    in float3 worldPosition  : TEXCOORD1,
    in float3 screenPosition : TEXCOORD2,
    in float3 normal         : NORMAL0,
    in float2 dataScaleAndDynamicFlag : NORMAL1,
    out float4 result        : COLOR0
) {
    float4 data = tex2D(MaskSampler, texCoord);
    float alpha = data.a;

    const float discardThreshold = (127.0 / 255.0);
    if (alpha < discardThreshold) {
        result = 0;
        discard;
        return;
    }

    // x|pitch y|yaw z|roll
    float dataScale = dataScaleAndDynamicFlag.x;
    float2 rotationIn = data.gr;
    float yOffset = 0;
    if (abs(getInvZToYMultiplier()) >= 0.001) {
        rotationIn.y = 0;
        yOffset = data.g * dataScale;
    }
    float3 rotation = float3((rotationIn - 0.5) * 2, 0) * PI,
        resultNormal;
    
    if (length(rotation) > 0.01) {
        float4 quat = yawPitchRoll(rotation.y, rotation.x, rotation.z);
        resultNormal = normalize(rotateLocalPosition(normal, quat));
    } else
        resultNormal = normal;

    /*    
    result = float4((resultNormal * 0.5) + 0.5, 1);
    return;
    */
    
    float effectiveZ = worldPosition.z + (yOffset * getInvZToYMultiplier()) + (data.b * dataScale);

    result = encodeGBufferSample(
        resultNormal,
        yOffset,
        effectiveZ,
        false, true, false
    );
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