using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering.Post;

// Ambient occlusion (GTAO) and contact shadows at half resolution from the depth pre-pass
// (Shaders/Passes/AmbientOcclusion.fx), then a depth-aware blur. The lit passes read the result (Shaders/Lighting/
// Shading.hlsli ScreenOcclusion).
internal sealed class PristonScreenOcclusion : IDisposable
{
    public const string Asset = "Effects/AmbientOcclusion";
    // World radius of the occlusion (about a character's height), the share of it over which occluders fade out, and
    // the largest screen radius in half-resolution pixels.
    public const float Radius = 90f;
    public const float FalloffShare = 0.4f;
    public const float MaxRadiusPixels = 48f;
    // Length of the contact-shadow march toward the sun and the thickness a depth sample is assumed to have.
    public const float ContactLength = 24f;
    public const float ContactThickness = 12f;

    private readonly GraphicsDevice _device;
    private readonly PristonFullscreenTriangle _triangle;
    private readonly Effect _effect;
    private readonly EffectTechnique _occlusion;
    private readonly EffectTechnique _blur;
    private readonly EffectParameter _depthMap;
    private readonly EffectParameter _normalMap;
    private readonly EffectParameter _input;
    private readonly EffectParameter _size;
    private readonly EffectParameter _projection;
    private readonly EffectParameter _radius;
    private readonly EffectParameter _contact;
    private readonly EffectParameter _right;
    private readonly EffectParameter _up;
    private readonly EffectParameter _forward;
    private readonly EffectParameter _sun;
    private readonly EffectParameter _worldToClip;
    private RenderTarget2D _raw;
    private RenderTarget2D _result;

    public PristonScreenOcclusion(GraphicsDevice device, PristonFullscreenTriangle triangle, Effect effect)
    {
        _device = device;
        _triangle = triangle;
        _effect = effect;
        _occlusion = effect.Techniques["Occlusion"];
        _blur = effect.Techniques["Blur"];
        var parameters = effect.Parameters;
        _depthMap = parameters["DepthMap"];
        _normalMap = parameters["NormalMap"];
        _input = parameters["AoInput"];
        _size = parameters["AoSize"];
        _projection = parameters["AoProjection"];
        _radius = parameters["AoRadius"];
        _contact = parameters["ContactParams"];
        _right = parameters["CameraRight"];
        _up = parameters["CameraUp"];
        _forward = parameters["CameraForward"];
        _sun = parameters["SunDirectionWorld"];
        _worldToClip = parameters["WorldToClip"];
    }

    // view and projection: the camera of the pre-pass; towardSun: unit vector toward the sun (or the moon).
    public Texture2D Run(Texture2D depth, Texture2D normals, Matrix view, Matrix projection, Vector3 towardSun,
        bool ambientOcclusion, bool contactShadows)
    {
        var width = Math.Max(depth.Width / 2, 1);
        var height = Math.Max(depth.Height / 2, 1);
        EnsureSize(width, height);
        var tanX = 1f / projection.M11;
        var tanY = 1f / projection.M22;
        var rotation = view;
        rotation.Translation = Vector3.Zero;
        _depthMap.SetValue(depth);
        _normalMap.SetValue(normals);
        _size.SetValue(new Vector4(width, height, 1f / width, 1f / height));
        _projection.SetValue(new Vector4(tanX, tanY, height * 0.5f / tanY, 0f));
        _radius.SetValue(new Vector4(Radius, FalloffShare, MaxRadiusPixels, ambientOcclusion ? 1f : 0f));
        _contact.SetValue(new Vector4(ContactLength, ContactThickness, contactShadows ? 1f : 0f, 0f));
        _right.SetValue(new Vector3(view.M11, view.M21, view.M31));
        _up.SetValue(new Vector3(view.M12, view.M22, view.M32));
        _forward.SetValue(-new Vector3(view.M13, view.M23, view.M33));
        _sun.SetValue(towardSun);
        _worldToClip.SetValue(rotation * projection);

        _device.BlendState = BlendState.Opaque;
        _device.SetRenderTarget(_raw);
        _effect.CurrentTechnique = _occlusion;
        _triangle.Draw(_effect);
        _device.SetRenderTarget(_result);
        _input.SetValue(_raw);
        _effect.CurrentTechnique = _blur;
        _triangle.Draw(_effect);
        return _result;
    }

    private void EnsureSize(int width, int height)
    {
        if (_raw is not null && _raw.Width == width && _raw.Height == height)
        {
            return;
        }

        _raw?.Dispose();
        _result?.Dispose();
        _raw = new RenderTarget2D(_device, width, height, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        _result = new RenderTarget2D(_device, width, height, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    }

    public void Dispose()
    {
        _raw?.Dispose();
        _result?.Dispose();
        _effect.Dispose();
    }
}
