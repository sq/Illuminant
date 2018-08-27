uniform float DeltaTimeSeconds;
uniform float Friction;
uniform float MaximumVelocity;

float3 applyFrictionAndMaximum (float3 velocity) {
    float l = length(velocity);
    if (l > MaximumVelocity)
        l = MaximumVelocity;

    float friction = l * Friction;

    l -= (friction * DeltaTimeSeconds);
    l = clamp(l, 0, MaximumVelocity);

    return normalize(velocity) * l;
}
