#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "ConeTrace.fxh"
#include "RampCommon.fxh"
#include "AOCommon.fxh"

#define SELF_OCCLUSION_HACK 1.5
#define SHADOW_OPACITY_THRESHOLD (0.75 / 255.0)

float sdCappedCone(float3 p, float3 a, float3 b, float ra, float rb) {
    float rba = rb - ra;
    float baba = dot(b - a, b - a);
    float papa = dot(p - a, p - a);
    float paba = dot(p - a, b - a) / baba;

    float x = sqrt(papa - paba * paba * baba);

    float cax = max(0.0, x - ((paba < 0.5) ? ra : rb));
    float cay = abs(paba - 0.5) - 0.5;

    float k = rba * rba + baba;
    float f = clamp((rba * (x - ra) + paba * baba) / k, 0.0, 1.0);

    float cbx = x - ra - f * rba;
    float cby = paba - f;
    
    float s = (cbx < 0.0 && cay < 0.0) ? -1.0 : 1.0;
    
    return s * sqrt(min(cax * cax + cay * cay * baba,
                       cbx * cbx + cby * cby * baba));
}

float dot2(float3 v) {
    return dot(v, v);
}

bool coneIntersect(
    in float3 ro, in float3 rd, in float3 pa, in float3 pb, in float ra, in float rb,
    out float4 result
) {
    result = 0;
    float3 ba = pb - pa;
    float3 oa = ro - pa;
    float3 ob = ro - pb;
    float m0 = dot(ba, ba);
    float m1 = dot(oa, ba);
    float m2 = dot(rd, ba);
    float m3 = dot(rd, oa);
    float m5 = dot(oa, oa);
    float m9 = dot(ob, ba);
    
    // caps
    if (m1 < 0.0)
    {
        if (dot2(oa * m2 - rd * m1) < (ra * ra * m2 * m2)) { // delayed division
            result = float4(-m1 / m2, -ba * rsqrt(m0));
            return true;
        }
    }
    else if (m9 > 0.0)
    {
        float t = -m9 / m2; // NOT delayed division
        if (dot2(ob + rd * t) < (rb * rb)) {
            result = float4(t, ba * rsqrt(m0));
            return true;
        }
    }
    
    // body
    float rr = ra - rb;
    float hy = m0 + rr * rr;
    float k2 = m0 * m0 - m2 * m2 * hy;
    float k1 = m0 * m0 * m3 - m1 * m2 * hy + m0 * ra * (rr * m2 * 1.0);
    float k0 = m0 * m0 * m5 - m1 * m1 * hy + m0 * ra * (rr * m1 * 2.0 - m0 * ra);
    float h = k1 * k1 - k2 * k0;
    if (h < 0.0)
        return false; //no intersection
    float t = (-k1 - sqrt(h)) / k2;
    float y = m1 + t * m2;
    if (y < 0.0 || y > m0)
        return false; //no intersection
    result = float4(t, normalize(m0 * (m0 * (oa + t * rd) + rr * ba * ra) - ba * hy * y));
    return true;
}

float volumetricTrace (
    in float4 startPosition,
    in float4 endPosition,
    in float3 shadedPixelPosition,
    in float4 lightProperties,
    in float4 moreLightProperties,
    in DistanceFieldConstants vars,
    in bool   enableDistance
) {
    float4 temp;
    
    // HACK: Early-out if we know the trace will not ever intersect the cone.
    // We fudge the radiuses slightly to give ourselves breathing room.
    [branch]
    if (!coneIntersect(
        float3(shadedPixelPosition.xy, getMaximumZ()), float3(0, 0, -1),
        startPosition.xyz, endPosition.xyz,
        startPosition.w + 1, endPosition.w + 1, temp
    ))
        return 0;
    
    float steps = getStepLimit(),
        occlusion = 1.0,
        hits = 0,
        step = (getMaximumZ() - getGroundZ()) / steps;

    [loop]
    for (float z = getMaximumZ(), z2 = max(shadedPixelPosition.z, getGroundZ()); z >= z2; z -= step) {
        float3 pos = float3(shadedPixelPosition.xy, z);
        float sd = sdCappedCone(pos, startPosition.xyz, endPosition.xyz, startPosition.w, endPosition.w);
        
        if (enableDistance) {
            // If the path between the camera and the shaded pixel is fully
            //  occluded, we should stop tracing
            float sample = sampleDistanceFieldEx(pos, vars);
            occlusion = min(occlusion, smoothstep(-1, 1, sample));
            // FIXME: smoothstep
            if (occlusion <= 0)
                break;
        }
        
        hits += (1 - smoothstep(-1, 1, sd)) * occlusion;
    }

    return saturate(hits / steps / lightProperties.x);
}

float VolumetricLightPixelCore(
    in float3 shadedPixelPosition,
    in float3 shadedPixelNormal,
    in float4 startPosition,
    in float4 endPosition,
    // radius, ramp length, ramp mode, enable shadows
    in float4 lightProperties,
    // ao radius, distance falloff, y falloff factor, ao opacity
    in float4 moreLightProperties,
    // blowout
    in float4 evenMoreLightProperties
) {
    float4 coneLightProperties = lightProperties;
    float lengthOfCone = length(endPosition.xyz - startPosition.xyz);

    float3 lightCenter;
    // FIXME: Early-cull with a ray-cone intersection
    bool visible = (shadedPixelPosition.x > -9999);

    clip(visible ? 1 : -1);

    DistanceFieldConstants vars = makeDistanceFieldConstants();

    // HACK: AO is only on upward-facing surfaces
    moreLightProperties.x *= max(0, shadedPixelNormal.z);

    float aoOpacity = computeAO(shadedPixelPosition, shadedPixelNormal, moreLightProperties, vars, visible);
    bool traceShadows = visible && lightProperties.w && (aoOpacity >= SHADOW_OPACITY_THRESHOLD) &&
        (DistanceField.Extent.z > 0);

    float distanceFromStartOfCone = length(shadedPixelPosition.xy - startPosition.xy),
        volumetricOpacity = volumetricTrace(
            startPosition, endPosition, shadedPixelPosition,
            lightProperties, moreLightProperties,
            vars, traceShadows
        );
    
    float preTraceOpacity = aoOpacity * volumetricOpacity;
    
    // FIXME: This isn't quite right
    // Attempt to figure out whether light can reach this column of space
    //  by traveling from the start of the cone
    /*
    float coneOpacity = coneTrace(
        startPosition.xyz, lightProperties.xy,
        float2(getConeGrowthFactor(), moreLightProperties.y),
        // FIXME: We want to trace along an approximate trajectory towards the end of the cone
        shadedPixelPosition + (SELF_OCCLUSION_HACK * shadedPixelNormal),
        vars, true
    );
    */

    // FIXME: Do diffuse lighting for the shaded surface.
    // FIXME: Do specular for the shaded surface?

    float3 trajectory = endPosition.xyz - startPosition.xyz;
    float fullLength = length(trajectory);
    float normalOpacity = computeNormalFactor(trajectory / fullLength, shadedPixelNormal);
    normalOpacity = lerp(normalOpacity, normalOpacity * 2 - 1, evenMoreLightProperties.x);
    float shapeOpacity = 1 - smoothstep(
        -1, 1, sdCappedCone(shadedPixelPosition, startPosition.xyz, endPosition.xyz, startPosition.w, endPosition.w)
    );
    float distanceOpacity = 1 - saturate(
        length(shadedPixelPosition - startPosition.xyz) / (fullLength * lightProperties.y)
    );
    float diffuse = normalOpacity * shapeOpacity * distanceOpacity;
    PREFER_FLATTEN
    // FIXME: other modes
    if (lightProperties.z >= 1)
    {
        distanceOpacity *= distanceOpacity;
    }
    
    // HACK: Support blowout
    if (diffuse < 0)
        return preTraceOpacity + diffuse;
    else
        return max(
            preTraceOpacity, 
            diffuse
        );
}

void VolumetricLightVertexShader(
    in    float3 cornerWeights       : NORMAL2,
    inout float4 startPosition       : TEXCOORD0,
    inout float4 endPosition         : TEXCOORD1,
    // radius, ramp length, ramp mode, enable shadows
    inout float4 lightProperties     : TEXCOORD2,
    // ao radius, distance falloff, y falloff factor, ao opacity
    inout float4 moreLightProperties : TEXCOORD3,
    inout float4 startColor          : TEXCOORD4,
    inout float4 endColor            : TEXCOORD5,
    inout float4 evenMoreLightProperties : TEXCOORD7,
    out float3 worldPosition         : POSITION1,
    out float4 result                : POSITION0
) {
    float3 vertex = cornerWeights;

    float  radius = lightProperties.x + lightProperties.y + 1;
    float  deltaY = (radius) - (radius / moreLightProperties.z);
    float3 radius3;

    if (1)
        // HACK: How the hell do we compute bounds for this in the first place?
        radius3 = float3(9999, 9999, 0);
    else if (0)
        // HACK: Scale the y axis some to clip off dead pixels caused by the y falloff factor
        radius3 = float3(radius, radius - (deltaY / 2.0), 0);
    else
        radius3 = float3(radius, radius, 0);

    float3 p1 = min(startPosition, endPosition).xyz, p2 = max(startPosition, endPosition).xyz;
    float3 tl = p1 - radius3, br = p2 + radius3;

    // Unfortunately we need to adjust both by the light's radius (to account for pixels above/below the center point
    //  being lit in 2.5d projection), along with adjusting by the z of the light's centerpoint (to deal with pixels
    //  at high elevation)
    float radiusOffset = radius * getInvZToYMultiplier();
    // FIXME
    float effectiveZ = startPosition.z;
    float zOffset = effectiveZ * getZToYMultiplier();

    worldPosition = lerp(tl, br, vertex);

    if (vertex.y < 0.5) {
        worldPosition.y -= radiusOffset;
        worldPosition.y -= zOffset;
    }

    float3 screenPosition = (worldPosition - float3(GetViewportPosition(), 0));
    screenPosition.xy *= GetViewportScale() * getEnvironmentRenderScale();
    float4 transformedPosition = mul(mul(float4(screenPosition.xyz, 1), Viewport.ModelView), Viewport.Projection);
    result = float4(transformedPosition.xy, 0, transformedPosition.w);
}
