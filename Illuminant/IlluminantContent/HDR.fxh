uniform float Offset;
uniform float ExposureMinusOne;


uniform float MiddleGray;
uniform float AverageLuminance, MaximumLuminanceSquared;

static const float3 RGBToLuminance = float3(0.299f, 0.587f, 0.114f);

float4 GammaCompress (float4 color) {
    float3 rgb = max(color.rgb + Offset, 0);
    float resultLuminance = dot(rgb, RGBToLuminance);
    float scaledLuminance = (resultLuminance * MiddleGray) / AverageLuminance;
    float compressedLuminance = (scaledLuminance * (1 + (scaledLuminance / MaximumLuminanceSquared))) / (1 + scaledLuminance);
    float rescaleFactor = compressedLuminance / resultLuminance;
    return float4(rgb * rescaleFactor, color.a);
}

// http://frictionalgames.blogspot.com/2012/09/tech-feature-hdr-lightning.html

uniform float WhitePoint;

static const float kA = 0.15;
static const float kB = 0.50;
static const float kC = 0.10;
static const float kD = 0.20;
static const float kE = 0.02;
static const float kF = 0.30;

float Uncharted2Tonemap1(float value)
{
    return (
        (value * (kA * value + kC * kB) + kD * kE) /
        (value * (kA * value + kB) + kD * kF)
    ) - kE / kF;
}

float3 Uncharted2Tonemap(float3 rgb)
{
    return (
        (rgb * (kA * rgb + kC * kB) + kD * kE) /
        (rgb * (kA * rgb + kB) + kD * kF)
    ) - kE / kF;
}