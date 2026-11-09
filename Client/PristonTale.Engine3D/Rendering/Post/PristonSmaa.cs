using System.IO.Compression;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering.Post;

// SMAA 1x on the display image (Shaders/Passes/PostSmaa.fx): edge detection, blending weights and neighbourhood
// blending, with the precomputed area and search tables embedded in the assembly (Post/Smaa/CREDITS.md).
internal sealed class PristonSmaa : IDisposable
{
    public const string Asset = "Effects/PostSmaa";
    private const int AreaWidth = 160;
    private const int AreaHeight = 560;
    private const int SearchWidth = 64;
    private const int SearchHeight = 16;

    private readonly GraphicsDevice _device;
    private readonly PristonFullscreenTriangle _triangle;
    private readonly Effect _effect;
    private readonly EffectTechnique _edgesTechnique;
    private readonly EffectTechnique _weightsTechnique;
    private readonly EffectTechnique _blendTechnique;
    private readonly EffectParameter _metrics;
    private readonly EffectParameter _color;
    private readonly EffectParameter _edgesTexture;
    private readonly EffectParameter _blendTexture;
    private readonly Texture2D _area;
    private readonly Texture2D _search;
    private RenderTarget2D _edges;
    private RenderTarget2D _weights;

    public PristonSmaa(GraphicsDevice device, PristonFullscreenTriangle triangle, Effect effect)
    {
        _device = device;
        _triangle = triangle;
        _effect = effect;
        _edgesTechnique = effect.Techniques["EdgeDetection"];
        _weightsTechnique = effect.Techniques["BlendingWeights"];
        _blendTechnique = effect.Techniques["NeighborhoodBlending"];
        var parameters = effect.Parameters;
        _metrics = parameters["RtMetrics"];
        _color = parameters["Color"];
        _edgesTexture = parameters["Edges"];
        _blendTexture = parameters["Blend"];
        _area = LoadTable(device, "AreaTex.r8g8.gz", AreaWidth, AreaHeight, 2);
        _search = LoadTable(device, "SearchTex.r8.gz", SearchWidth, SearchHeight, 1);
        parameters["AreaTex"]?.SetValue(_area);
        parameters["SearchTex"]?.SetValue(_search);
    }

    // Filters input into the bound output: the caller binds its targets and viewport after the two internal passes.
    public void Apply(Texture2D input, Action bindOutput)
    {
        EnsureSize(input.Width, input.Height);
        _device.BlendState = BlendState.Opaque;
        _metrics?.SetValue(new Vector4(1f / input.Width, 1f / input.Height, input.Width, input.Height));
        _color?.SetValue(input);

        // Pixels without an edge are discarded: the edges target is cleared first.
        _device.SetRenderTarget(_edges);
        _device.Clear(Color.Transparent);
        _effect.CurrentTechnique = _edgesTechnique;
        _triangle.Draw(_effect);

        _device.SetRenderTarget(_weights);
        _effect.CurrentTechnique = _weightsTechnique;
        _edgesTexture?.SetValue(_edges);
        _triangle.Draw(_effect);

        bindOutput();
        _effect.CurrentTechnique = _blendTechnique;
        _blendTexture?.SetValue(_weights);
        _triangle.Draw(_effect);
    }

    // The tables ship as raw R8G8 / R8 bytes (gzip), expanded to RGBA8.
    private static Texture2D LoadTable(GraphicsDevice device, string name, int width, int height, int channels)
    {
        var assembly = typeof(PristonSmaa).Assembly;
        using var stream = assembly.GetManifestResourceStream("PristonTale.Engine3D.Smaa." + name)
            ?? throw new InvalidOperationException($"Missing embedded SMAA table {name}.");
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        var raw = new byte[width * height * channels];
        gzip.ReadExactly(raw);
        var rgba = new Color[width * height];
        for (var i = 0; i < rgba.Length; i++)
        {
            rgba[i] = new Color(raw[i * channels], channels > 1 ? raw[i * channels + 1] : (byte)0, (byte)0, (byte)255);
        }

        var texture = new Texture2D(device, width, height, false, SurfaceFormat.Color) { Name = "SMAA " + name };
        texture.SetData(rgba);
        return texture;
    }

    private void EnsureSize(int width, int height)
    {
        if (_edges is not null && _edges.Width == width && _edges.Height == height)
        {
            return;
        }

        ReleaseTargets();
        _edges = new RenderTarget2D(_device, width, height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        _weights = new RenderTarget2D(_device, width, height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    }

    public void Dispose()
    {
        ReleaseTargets();
        _area.Dispose();
        _search.Dispose();
        _effect.Dispose();
    }

    private void ReleaseTargets()
    {
        _edges?.Dispose();
        _weights?.Dispose();
        _edges = null;
        _weights = null;
    }
}
