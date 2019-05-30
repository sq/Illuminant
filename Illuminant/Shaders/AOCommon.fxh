float computeAO (
    in float3 shadedPixelPosition,
    in float3 shadedPixelNormal,
    in float4 moreLightProperties, 
    in DistanceFieldConstants vars,
    in bool visible
) {
    PREFER_BRANCH
    if ((moreLightProperties.x >= 0.5) && (DistanceField.Extent.x > 0) && visible) {
        float distance = sampleDistanceFieldEx(shadedPixelPosition + float3(0, 0, shadedPixelNormal.z * moreLightProperties.x), vars);
        float clampedDistance = clamp(distance, 0, moreLightProperties.x);
        return (clampedDistance / moreLightProperties.x) * moreLightProperties.w;
    }
    return 1;
}