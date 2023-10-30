#define ENABLE_DITHERING 1

#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\DitherCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "ConeTrace.fxh"
#include "RampCommon.fxh"
#include "AOCommon.fxh"

#define SELF_OCCLUSION_HACK 1.5
#define SHADOW_OPACITY_THRESHOLD (0.75 / 255.0)

#define SHAPE_ELLIPSOID 0
#define SHAPE_CONE 1
#define SHAPE_BOX 2

uniform const float FrameIndex;

float dot2(float3 v) {
    return dot(v, v);
}

float sdEllipsoid(float3 p, float3 r) {
    float k0 = length(p / r);
    float k1 = length(p / (r * r));
    return k0 * (k0 - 1.0) / k1;
}

float sdRoundCone(float3 p, float3 a, float3 b, float r1, float r2) {
  // sampling independent computations (only depend on shape)
    float3 ba = b - a;
    float l2 = dot(ba, ba);
    float rr = r1 - r2;
    float a2 = l2 - rr * rr;
    float il2 = 1.0 / l2;
    
  // sampling dependant computations
    float3 pa = p - a;
    float y = dot(pa, ba);
    float z = y - l2;
    float x2 = dot2(pa * l2 - ba * y);
    float y2 = y * y * l2;
    float z2 = z * z * l2;

  // single square root!
    float k = sign(rr) * rr * rr * x2;
    if (sign(z) * a2 * z2 > k)
        return sqrt(x2 + z2) * il2 - r2;
    if (sign(y) * a2 * y2 < k)
        return sqrt(x2 + y2) * il2 - r1;
    return (sqrt(x2 * a2 * il2) + y * rr) * il2 - r1;
}

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

float sdCapsule(float3 p, float3 a, float3 b, float r) {
    float3 pa = p - a, ba = b - a;
    float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
    return length(pa - ba * h) - r;
}

float sdBox(in float3 p, in float3 b) {
    float3 d = abs(p) - b;
    return length(max(d, 0.0001)) + min(max(max(d.x, d.y), d.z), 0.0001);
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

// cone defined by extremes pa and pb, and radious ra and rb.
bool roundConeIntersect(float3 ro, float3 rd, float3 pa, float3 pb, in float ra, in float rb, out float4 result) {
    float3 ba = pb - pa;
    float3 oa = ro - pa;
    float3 ob = ro - pb;
    float rr = ra - rb;
    float m0 = dot(ba, ba);
    float m1 = dot(ba, oa);
    float m2 = dot(ba, rd);
    float m3 = dot(rd, oa);
    float m5 = dot(oa, oa);
    float m6 = dot(ob, rd);
    float m7 = dot(ob, ob);
    
    // body
    float d2 = m0 - rr * rr;
    float k2 = d2 - m2 * m2;
    float k1 = d2 * m3 - m1 * m2 + m2 * rr * ra;
    float k0 = d2 * m5 - m1 * m1 + m1 * rr * ra * 2.0 - m0 * ra * ra;
    float h = k1 * k1 - k0 * k2;
    if (h < 0.0) {
        result = -1.0;
        return false;
    }
    float t = (-sqrt(h) - k1) / k2;
  //if( t<0.0 ) return vec4(-1.0);
    float y = m1 - ra * rr + t * m2;
    if (y > 0.0 && y < d2) {
        result = float4(t, normalize(d2 * (oa + t * rd) - ba * y));
        return true;
    }

    // caps
    float h1 = m3 * m3 - m5 + ra * ra;
    float h2 = m6 * m6 - m7 + rb * rb;
    if (max(h1, h2) < 0.0) {
        result = -1.0;
        return false;        
    }
    
    result = 1e20;
    if (h1 > 0.0)
    {
        t = -m3 - sqrt(h1);
        result = float4(t, (oa + t * rd) / ra);
        return true;
    }
    if (h2 > 0.0)
    {
        t = -m6 - sqrt(h2);
        if (t < result.x) {
            result = float4(t, (ob + t * rd) / rb);
            return true;
        }
    }
    
    return true;
}

bool ellipsoidIntersect(in float3 ro, in float3 rd, in float3 ra, out float4 result) {
    float3 ocn = ro / ra;
    float3 rdn = rd / ra;
    float a = dot(rdn, rdn);
    float b = dot(ocn, rdn);
    float c = dot(ocn, ocn);
    float h = b * b - a * (c - 1.0);
    if (h < 0.0) {
        result = -1;
        return false;
    }
    h = sqrt(h);
    result = float4(float2(-b - h, -b + h) / a, 0, 0);
    return true;
}

// capsule defined by extremes pa and pb, and radious ra
// Note that only ONE of the two spherical caps is checked for intersections,
// which is a nice optimization
bool capsuleIntersect(in float3 ro, in float3 rd, in float3 pa, in float3 pb, in float ra, out float4 result) {
    float3 ba = pb - pa;
    float3 oa = ro - pa;
    float baba = dot(ba, ba);
    float bard = dot(ba, rd);
    float baoa = dot(ba, oa);
    float rdoa = dot(rd, oa);
    float oaoa = dot(oa, oa);
    float a = baba - bard * bard;
    float b = baba * rdoa - baoa * bard;
    float c = baba * oaoa - baoa * baoa - ra * ra * baba;
    float h = b * b - a * c;
    if (h >= 0.0)
    {
        float t = (-b - sqrt(h)) / a;
        float y = baoa + t * bard;
        // body
        if (y > 0.0 && y < baba)
        {
            result = t;
            return true;
        }
        // caps
        float3 oc = (y <= 0.0) ? oa : ro - pb;
        b = dot(rd, oc);
        c = dot(oc, oc) - ra * ra;
        h = b * b - c;
        if (h > 0.0)
        {
            result = -b - sqrt(h);
            return true;
        }            
    }
    
    result = -1;
    return false;
}

// axis aligned box centered at the origin, with size boxSize
bool boxIntersect(in float3 ro, in float3 rd, float3 boxSize, out float3 outNormal, out float4 result) {
    float3 m = float3(
        rd.x != 0 ? rcp(rd.x) : 0,
        rd.y != 0 ? rcp(rd.y) : 0,
        rd.z != 0 ? rcp(rd.z) : 0
    ); // can precompute if traversing a set of aligned boxes
    float3 n = m * ro; // can precompute if traversing a set of aligned boxes
    float3 k = abs(m) * boxSize;
    float3 t1 = -n - k;
    float3 t2 = -n + k;
    float tN = max(max(t1.x, t1.y), t1.z);
    float tF = min(min(t2.x, t2.y), t2.z);
    if (tN > tF || tF < 0.0)
    {
        outNormal = 0;
        result = -1;
        return false; // no intersection
    }
    outNormal = (tN > 0.0) ? step(tN, t1) : // ro ouside the box
                           step(t2, tF); // ro inside the box
    outNormal *= -sign(rd);
    result = float4(tN, tF, 0, 0);
    return true;
}

float eval (
    in float3 position,
    in float4 startPosition,
    in float4 endPosition,
    in float4 lightProperties,
    in float4 moreLightProperties,
    in float4 evenMoreLightProperties
) {
    [branch]
    if (evenMoreLightProperties.w <= SHAPE_ELLIPSOID)
        return sdEllipsoid(position - startPosition.xyz, endPosition.xyz);    
    
    [branch]
    if (evenMoreLightProperties.w <= SHAPE_CONE)
        return sdRoundCone(position, startPosition.xyz, endPosition.xyz, startPosition.w, endPosition.w);
    
    return sdBox(position - startPosition.xyz, endPosition.xyz);
}

bool intersect(float3 ro, float3 rd, float3 pa, float3 pb, in float ra, in float rb, in float4 evenMoreLightProperties, out float4 result) {
    result = 0;
    return true;
    float3 temp3;
    
    if (evenMoreLightProperties.w <= SHAPE_ELLIPSOID)
        return ellipsoidIntersect(ro - pa, rd, pb, result);
    
    [branch]
    if (evenMoreLightProperties.w <= SHAPE_CONE)
        return roundConeIntersect(ro, rd, pa, pb, ra, rb, result);
    
    return boxIntersect(ro, rd, pb, temp3, result);
}

float volumetricTrace (
    in float4 startPosition,
    in float4 endPosition,
    in float3 shadedPixelPosition,
    in float4 lightProperties,
    in float4 moreLightProperties,
    in float4 evenMoreLightProperties,
    in DistanceFieldConstants vars,
    in float2 vpos,
    in bool   enableDistance
) { 
    float steps = getStepLimit(),
        occlusion = 1.0,
        hits = 0,
        z2 = max(shadedPixelPosition.z, getGroundZ()),
        z1 = max(getMaximumZ(), z2),
        step = max(abs(z2 - z1), 1) / steps,
    // HACK: Apply dithering to the initial Z coordinate.
    // This significantly reduces visible banding caused by low resolution
        dither = (Dither17(vpos, (FrameIndex % 4) + 0.5) * 3) - 1.5;
    
    [loop]
    for (float z = z1 + dither; z >= z2; z -= step) {
        float3 pos = float3(shadedPixelPosition.xy, z);
        float sd = eval(pos, startPosition, endPosition, lightProperties, moreLightProperties, evenMoreLightProperties);
        if (sd >= 0)
            continue;
        
        if (enableDistance)
        {
            // If the path between the camera and the shaded pixel is fully
            //  occluded, we should stop tracing
            float sample = sampleDistanceFieldEx(pos, vars);
            occlusion = min(occlusion, smoothstep(-1, 1, sample));
            // FIXME: smoothstep
            if (occlusion <= 0)
                break;
        }

        float ramp = pow(saturate(-sd / lightProperties.y), evenMoreLightProperties.y);
        hits += ramp * occlusion;
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
    // blowout, interior ramping power, distance attenuation, unused
    in float4 evenMoreLightProperties,
    in float2 vpos
) {
    float3 lightCenter;
    bool visible = (shadedPixelPosition.x > -9999);
    float4 temp;
    
    // HACK: Early-out if we know the trace will not ever intersect the cone.
    // We fudge the radiuses slightly to give ourselves breathing room.
    float radiusBias = 1.0;
    if (!intersect(
        float3(shadedPixelPosition.xy, getMaximumZ()), float3(0, 0, -1),
        startPosition.xyz, endPosition.xyz,
        startPosition.w + radiusBias, endPosition.w + radiusBias, 
        evenMoreLightProperties, temp
    ))
        visible = false;

    [branch]    
    if (!visible)
        return 0;

    DistanceFieldConstants vars = makeDistanceFieldConstants();

    // HACK: AO is only on upward-facing surfaces
    moreLightProperties.x *= max(0, shadedPixelNormal.z);

    float aoOpacity = computeAO(shadedPixelPosition, shadedPixelNormal, moreLightProperties, vars, visible);
    bool traceShadows = visible && lightProperties.w && (aoOpacity >= SHADOW_OPACITY_THRESHOLD) &&
        (DistanceField.Extent.z > 0);

    float volumetricOpacity = volumetricTrace(
        startPosition, endPosition, shadedPixelPosition,
        lightProperties, moreLightProperties, evenMoreLightProperties,
        vars, vpos, traceShadows
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
    float fullLength = evenMoreLightProperties.w == SHAPE_CONE
        ? length(trajectory)
        : length(endPosition.xyz);
    float normalOpacity = computeNormalFactor(normalize(shadedPixelPosition - startPosition.xyz), shadedPixelNormal);
    normalOpacity = lerp(normalOpacity, normalOpacity * 2 - 1, evenMoreLightProperties.x);
    float contactDistance = eval(shadedPixelPosition, startPosition, endPosition, lightProperties, moreLightProperties, evenMoreLightProperties);    
    float shapeOpacity = contactDistance < 0 
        ? pow(saturate(-contactDistance / lightProperties.y), evenMoreLightProperties.y)
        : 0;
    float distanceOpacity = 1 - saturate(
        length(shadedPixelPosition - startPosition.xyz) / (fullLength * evenMoreLightProperties.z)
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
