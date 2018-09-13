float3 applyFrictionAndMaximum (float3 velocity) {
    float l = length(velocity);
    if (l > System.MaximumVelocity)
        l = System.MaximumVelocity;

    float friction = l * System.Friction;

    l -= (friction * System.DeltaTimeSeconds);
    l = clamp(l, 0, System.MaximumVelocity);

    return normalize(velocity) * l;
}
