#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\BitmapCommon.fxh"
#include "Common.fxh"

void VisualizePixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord : TEXCOORD0,
    in float2 texTL : TEXCOORD1,
    in float2 texBR : TEXCOORD2,
    out float4 result : COLOR0
) {
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    float4 encoded = tex2Dgrad(TextureSampler, clamp(texCoord, texTL, texBR), 0, 0);
    float decoded = decodeDistance(encoded);

    float r = 1.0 - clamp(decoded / 256, 0, 1);
    float g = abs(min(0, decoded / 16));

    float4 visualized = float4(r, g, 0, 1);

    result = multiplyColor * visualized;
    result += (addColor * result.a);
}

technique Visualize
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 VisualizePixelShader();
    }
}