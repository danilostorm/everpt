// Test 5 of the repro (monogame/upstream/11-pixel-shader-constants.md): a pixel shader whose only input is a
// constant, under a vertex shader that reads none. Two draws that change only the constant must give two colours.
cbuffer PixelConstants : register(b0)
{
    float4 Colour;
};

struct VertexIn
{
    float4 Position : POSITION0;
};

struct VertexOut
{
    float4 Position : SV_Position;
};

VertexOut VS(VertexIn i)
{
    VertexOut o;
    o.Position = float4(i.Position.xy, 0.0, 1.0);
    return o;
}

float4 PS(VertexOut i) : SV_Target0
{
    return Colour;
}

technique Constant { pass P0 { VertexShader = compile vs_6_0 VS(); PixelShader = compile ps_6_0 PS(); } }
