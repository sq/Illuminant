uniform const float3 TapSpacingAndBias;
uniform const float2 DisplacementScale;
uniform const bool NormalsAreSigned, NormalElevationClamping;
// HACK: The surface normals can be very small (effectively denormal) in some cases,
//  this tries to compensate by scaling them up before storing them to a floating-point
//  texture so they don't round down to zero.
// FIXME: This still isn't enough to prevent artifacts at edges for some reason :(
// uniform const float2 DenormalCompensation = float2(50, 1.0 / 50);
// HACK: Disabled because it makes HLSprites awkward to deal with
uniform const float2 DenormalCompensation = float2(1.0, 1.0);

float tap (
    float2 uv,
    float4 texRgn,
    float4 traits,
    out float alpha
) {
    float4 rgba = tex2Dbias(HeightmapSampler, float4(clamp(uv, texRgn.xy, texRgn.zw), 0, TapSpacingAndBias.z));
    float luminance;
    ExtractLuminanceAlpha(rgba, traits, luminance, alpha);
    return luminance;
}

float synthesizeAlpha (float value) {
    if (abs(value) < 0.01)
        return 0;
    return smoothstep(0.01, 0.15, abs(value));
}

float3 calculateNormal (
    float2 texCoord, float4 texRgn, float2 halfTexel, float4 traits, out float alpha
) {
    float3 spacing = float3(TapSpacingAndBias.xy, 0);
    if (spacing.x <= 0)
        spacing = float3(halfTexel, 0);
    float epsilon = 0.001, temp;

    float a = tap(texCoord - spacing.xz, texRgn, traits, temp), b = tap(texCoord + spacing.xz, texRgn, traits, temp),
        c = tap(texCoord - spacing.zy, texRgn, traits, temp), d = tap(texCoord + spacing.zy, texRgn, traits, temp),
        center = tap(texCoord, texRgn, traits, alpha);

    // If the current pixel is entirely influenced by heightmap values that are nearly zero, we should
    //  give it a low alpha value so that when refraction shaders consume it they can avoid performing
    //  mip bias for this pixel. Without doing this, heightmap=0 pixels will be blurry when mip bias
    //  is enabled, and that isn't what we want.
    // This alpha value isn't used to govern refraction itself (if it was, this would produce weird
    //  hard edges or other artifacts.)
    alpha = max(
        synthesizeAlpha(center), 
        max(
            synthesizeAlpha(a), max(
                synthesizeAlpha(b), max(
                    synthesizeAlpha(c), 
                    synthesizeAlpha(d)
                )
            )
        )
    );

    // HACK: When generating normals from a heightmap, it may be desirable to not
    //  have 'higher elevation' pixels influence the normals of pixels at lower
    //  elevation. This set of mins provides an approximation of that, and prevents
    //  false shadows from appearing on surfaces when using these generated normals
    //  for lighting.
    if (NormalElevationClamping) {
        a = min(a, center);
        b = min(b, center);
        c = min(c, center);
        d = min(d, center);
    }

    if (
        (abs(center) < epsilon) && (abs(a) < epsilon) &&
        (abs(b) < epsilon) && (abs(c) < epsilon) &&
        (abs(d) < epsilon)
    )
        alpha = 0;

    return normalize(float3(
        a - b,
        c - d,
        // Normally if we were sampling a 3d space, we'd be subtracting two taps here.
        // But we're sampling a 2d space so the delta of the taps would always be zero.
        // We use a constant instead to get somewhat consistent behavior.
        0.5
    ));
}