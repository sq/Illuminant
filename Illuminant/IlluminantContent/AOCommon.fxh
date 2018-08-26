float computeAO (
    float3 shadedPixelPosition,
    float3 shadedPixelNormal,
    float4 moreLightProperties, 
    DistanceFieldConstants vars,
    bool visible
) {
    [branch]
    if ((moreLightProperties.x >= 0.5) && (DistanceField.Extent.x > 0) && visible) {
        float distance = sampleDistanceField(shadedPixelPosition + float3(0, 0, shadedPixelNormal.z * moreLightProperties.x), vars);
        float aoOpacity = clamp(moreLightProperties.w, 0, 1);
        float aoRamp = (clamp(distance / moreLightProperties.x, 0, 1) * aoOpacity) + (1 - aoOpacity);
        return aoRamp;
    } else {
        return 1;
    }
}