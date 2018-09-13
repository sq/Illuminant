float3 applyFrictionAndMaximum (float3 velocity) {
    float l = length(velocity);
    if (l > getMaximumVelocity())
        l = getMaximumVelocity();

    float friction = l * getFriction();

    l -= (friction * getDeltaTime());
    l = clamp(l, 0, getMaximumVelocity());

    return normalize(velocity) * l;
}
