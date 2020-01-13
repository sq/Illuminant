float computeAO (
    in float3 shadedPixelPosition,
    in float3 shadedPixelNormal,
    // ao radius, ?, ?, ao opacity
    in float4 moreLightProperties, 
    in DistanceFieldConstants vars,
    in bool visible
) {
    float aoRadius = moreLightProperties.x, aoOpacity = moreLightProperties.w;
    PREFER_BRANCH
    if ((aoRadius >= 0.5) && (DistanceField.Extent.x > 0) && visible) {
        float distance = sampleDistanceFieldEx(shadedPixelPosition + float3(0, 0, shadedPixelNormal.z * moreLightProperties.x), vars);
        float clampedDistance = clamp(distance, 0, aoRadius);
        float result = 1 - saturate(clampedDistance / moreLightProperties.x);
        result *= result;
        result = 1 - result;
        return (1 - aoOpacity) + (result * aoOpacity);
    }
    return 1;
}