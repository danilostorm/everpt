using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering.Post;

// The bloom pyramid (Shaders/Passes/PostBloom.fx): six half-resolution levels down, then each level blended into the
// next larger one. Top() is the half-resolution blurred image the final pass mixes in.
internal sealed class PristonBloom : IDisposable
{
    public const string Asset = "Effects/PostBloom";
    public const int Levels = 6;
    // Weight of each wider level when folding the pyramid: larger is a wider, softer veil.
    private const float Spread = 0.6f;

    private readonly GraphicsDevice _device;
    private readonly PristonFullscreenTriangle _triangle;
    private readonly Effect _effect;
    private readonly EffectTechnique _downKaris;
    private readonly EffectTechnique _down;
    private readonly EffectTechnique _up;
    private readonly EffectParameter _source;
    private readonly EffectParameter _texel;
    private readonly EffectParameter _weight;
    private readonly RenderTarget2D[] _chain = new RenderTarget2D[Levels];
    private int _width;
    private int _height;

    public PristonBloom(GraphicsDevice device, PristonFullscreenTriangle triangle, Effect effect)
    {
        _device = device;
        _triangle = triangle;
        _effect = effect;
        _downKaris = effect.Techniques["DownsampleKaris"];
        _down = effect.Techniques["Downsample"];
        _up = effect.Techniques["Upsample"];
        _source = effect.Parameters["Source"];
        _texel = effect.Parameters["SourceTexel"];
        _weight = effect.Parameters["Weight"];
    }

    public Texture2D Top => _chain[0];

    public void Run(Texture2D scene)
    {
        EnsureSize(scene.Width, scene.Height);
        _device.BlendState = BlendState.Opaque;
        Texture2D source = scene;
        for (var level = 0; level < Levels; level++)
        {
            _device.SetRenderTarget(_chain[level]);
            _effect.CurrentTechnique = level == 0 ? _downKaris : _down;
            _source.SetValue(source);
            _texel.SetValue(new Vector2(1f / source.Width, 1f / source.Height));
            _triangle.Draw(_effect);
            source = _chain[level];
        }

        _device.BlendState = BlendState.NonPremultiplied;
        _effect.CurrentTechnique = _up;
        _weight.SetValue(Spread);
        for (var level = Levels - 1; level > 0; level--)
        {
            _device.SetRenderTarget(_chain[level - 1]);
            _source.SetValue(_chain[level]);
            _texel.SetValue(new Vector2(1f / _chain[level].Width, 1f / _chain[level].Height));
            _triangle.Draw(_effect);
        }

        _device.BlendState = BlendState.Opaque;
    }

    private void EnsureSize(int width, int height)
    {
        if (width == _width && height == _height && _chain[0] is not null)
        {
            return;
        }

        _width = width;
        _height = height;
        var levelWidth = width;
        var levelHeight = height;
        for (var level = 0; level < Levels; level++)
        {
            levelWidth = Math.Max(1, levelWidth / 2);
            levelHeight = Math.Max(1, levelHeight / 2);
            _chain[level]?.Dispose();
            _chain[level] = new RenderTarget2D(
                _device,
                levelWidth,
                levelHeight,
                false,
                SurfaceFormat.HalfVector4,
                DepthFormat.None,
                0,
                RenderTargetUsage.PreserveContents);
        }
    }

    public void Dispose()
    {
        foreach (var target in _chain)
        {
            target?.Dispose();
        }

        _effect.Dispose();
    }
}
