float computeAO (
    float3 shadedPixelPosition,
    float3 shadedPixelNormal,
    float4 moreLightProperties, 
    DistanceFieldConstants vars,
    bool visible
) {
    [branch]
    if ((moreLightProperties.x >= 0.5) && (DistanceField.Extent.x > 0) && visible) {
        float distance = sampleDistanceFieldEx(shadedPixelPosition + float3(0, 0, shadedPixelNormal.z * moreLightProperties.x), vars);
        float aoOpacity = saturate(moreLightProperties.w);
        float aoRamp = (saturate(distance / moreLightProperties.x) * aoOpacity) + (1 - aoOpacity);
        return aoRamp;
    } else {
        return 1;
    }
}