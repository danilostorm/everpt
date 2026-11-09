using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Rendering.Lighting;

namespace PristonTale.Engine3D.Rendering.Post;

// Screen-space reflections of smooth surfaces (Shaders/Passes/Reflections.fx): the opaque scene copied, then rays
// through the pre-pass depth added to the scene as the difference to the sky's reflection the lit pass used, so where
// a ray misses the image-based reflection stays.
internal sealed class PristonScreenReflections : IDisposable
{
    public const string Asset = "Effects/Reflections";
    // The longest reflected ray, world units.
    public const float MaxRayLength = 3000f;

    private static readonly BlendState AddSigned = new()
    {
        Name = "PristonReflectionAdd",
        ColorSourceBlend = Blend.One,
        ColorDestinationBlend = Blend.One,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.One
    };

    private readonly GraphicsDevice _device;
    private readonly PristonFullscreenTriangle _triangle;
    private readonly Effect _effect;
    private readonly EffectTechnique _copy;
    private readonly EffectTechnique _reflect;
    private readonly EffectParameter _scene;
    private readonly EffectParameter _depth;
    private readonly EffectParameter _normals;
    private readonly EffectParameter _environmentMap;
    private readonly EffectParameter _environmentSh;
    private readonly EffectParameter _size;
    private readonly EffectParameter _projection;
    private readonly EffectParameter _right;
    private readonly EffectParameter _up;
    private readonly EffectParameter _forward;
    private readonly EffectParameter _worldToClip;
    private readonly EffectParameter _environmentInfo;
    private RenderTarget2D _copyTarget;

    public PristonScreenReflections(GraphicsDevice device, PristonFullscreenTriangle triangle, Effect effect)
    {
        _device = device;
        _triangle = triangle;
        _effect = effect;
        _copy = effect.Techniques["Copy"];
        _reflect = effect.Techniques["Reflect"];
        var parameters = effect.Parameters;
        _scene = parameters["SceneCopy"];
        _depth = parameters["DepthMap"];
        _normals = parameters["NormalMap"];
        _environmentMap = parameters["EnvironmentMap"];
        _environmentSh = parameters["EnvironmentSh"];
        _size = parameters["ReflectSize"];
        _projection = parameters["ReflectProjection"];
        _right = parameters["CameraRight"];
        _up = parameters["CameraUp"];
        _forward = parameters["CameraForward"];
        _worldToClip = parameters["WorldToClip"];
        _environmentInfo = parameters["EnvironmentInfo"];
    }

    // Adds the reflections to scene (bound again by bindScene after the copy).
    public void Run(RenderTarget2D scene, Texture2D depth, Texture2D normals, Matrix view, Matrix projection,
        PristonEnvironmentFrame environment, Texture2D neutral, StructuredBuffer neutralSh, float noiseOffset, Action bindScene)
    {
        EnsureSize(scene.Width, scene.Height);
        _device.SetRenderTarget(_copyTarget);
        _device.BlendState = BlendState.Opaque;
        _scene.SetValue(scene);
        _effect.CurrentTechnique = _copy;
        _triangle.Draw(_effect);

        bindScene();
        var rotation = view;
        rotation.Translation = Vector3.Zero;
        var environmentOn = environment.Atlas is not null && environment.Sh is not null;
        _scene.SetValue(_copyTarget);
        _depth.SetValue(depth);
        _normals.SetValue(normals);
        _environmentMap.SetValue(environmentOn ? environment.Atlas : neutral);
        _environmentSh.SetValue(environmentOn ? environment.Sh : neutralSh);
        _size.SetValue(new Vector4(scene.Width, scene.Height, 1f / scene.Width, 1f / scene.Height));
        _projection.SetValue(new Vector4(1f / projection.M11, 1f / projection.M22, MaxRayLength, noiseOffset));
        _right.SetValue(new Vector3(view.M11, view.M21, view.M31));
        _up.SetValue(new Vector3(view.M12, view.M22, view.M32));
        _forward.SetValue(-new Vector3(view.M13, view.M23, view.M33));
        _worldToClip.SetValue(rotation * projection);
        _environmentInfo.SetValue(environmentOn
            ? new Vector4(1f, PristonEnvironmentLighting.BaseSize, PristonEnvironmentLighting.AtlasWidth, PristonEnvironmentLighting.Levels)
            : Vector4.Zero);
        _device.BlendState = AddSigned;
        _effect.CurrentTechnique = _reflect;
        _triangle.Draw(_effect);
        _device.BlendState = BlendState.Opaque;
    }

    private void EnsureSize(int width, int height)
    {
        if (_copyTarget is not null && _copyTarget.Width == width && _copyTarget.Height == height)
        {
            return;
        }

        _copyTarget?.Dispose();
        _copyTarget = new RenderTarget2D(_device, width, height, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0,
            RenderTargetUsage.PreserveContents);
    }

    public void Dispose()
    {
        _copyTarget?.Dispose();
        _effect.Dispose();
    }
}
