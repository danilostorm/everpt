using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;

namespace PristonTale.Engine3D.Rendering;

// Unlit textured VertexPositionColorTexture geometry in the HDR scene (Shaders/Passes/Unlit.fx): lamp glows and original
// mesh effects. The texture is an sRGB format (ColorSRgb) and vertex colours are decoded in the shader; Intensity scales
// the result into scene radiance, so additive effects add light in linear space. Each owner holds its own clone of the
// effect, so the parameters of one never leak into another's draws.
public sealed class PristonUnlitEffect : IDisposable
{
    public const string AssetName = "Effects/Unlit";

    private readonly Effect _effect;
    private readonly EffectParameter _worldViewProjection;
    private readonly EffectParameter _texture;
    private readonly EffectParameter _tint;
    private readonly EffectParameter _exposure;
    private readonly EffectParameter _displayOutput;

    public PristonUnlitEffect(ContentManager content, IClientLog log = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        _effect = PristonShaderLibrary.LoadRequired(content, AssetName, log ?? ConsoleClientLog.Instance);
        _worldViewProjection = _effect.Parameters["WorldViewProjection"];
        _texture = _effect.Parameters["Texture"];
        _tint = _effect.Parameters["Tint"];
        _exposure = _effect.Parameters["Exposure"];
        _displayOutput = _effect.Parameters["DisplayOutput"];
    }

    public Matrix World { get; set; } = Matrix.Identity;

    public Matrix View { get; set; } = Matrix.Identity;

    public Matrix Projection { get; set; } = Matrix.Identity;

    public Texture2D Texture { get; set; }

    // Linear colour factor and opacity of every vertex.
    public Vector3 DiffuseColor { get; set; } = Vector3.One;

    public float Alpha { get; set; } = 1f;

    // Scale of the authored colour; 1 displays it at its authored level once the scene's exposure is applied.
    public float Intensity { get; set; } = 1f;

    // The nominal display scale of the scene the effect draws into: the colours are divided by it, so they display as
    // authored. Null follows SceneExposure, which the renderer sets every frame for the effects drawn into its scene.
    public float? Exposure { get; set; }

    // The nominal display scale of the HDR scene being drawn (PristonFrameContext.Exposure), set by PristonRenderer.Draw on
    // the render thread.
    public static float SceneExposure { get; internal set; } = 1f;

    // Filter and addressing of the texture: clamped sprites, wrapped mesh effects.
    public SamplerState Sampler { get; set; } = SamplerState.LinearClamp;

    // True when drawing into an 8-bit target outside the HDR scene: the shader writes display-referred sRGB.
    public bool DisplayOutput { get; set; }

    public void Apply()
    {
        _worldViewProjection.SetValue(World * View * Projection);
        _texture.SetValue(Texture);
        _tint.SetValue(new Vector4(DiffuseColor * Intensity, Alpha));
        _displayOutput?.SetValue(DisplayOutput ? 1f : 0f);
        _exposure?.SetValue(Exposure ?? SceneExposure);
        _effect.CurrentTechnique.Passes[0].Apply();
        _effect.GraphicsDevice.SamplerStates[0] = Sampler;
    }

    public void Dispose() => _effect.Dispose();
}
