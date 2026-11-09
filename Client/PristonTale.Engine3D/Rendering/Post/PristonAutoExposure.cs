using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Rendering.Lighting;

namespace PristonTale.Engine3D.Rendering.Post;

// The metering of the frame (Shaders/Passes/PostExposure.fx): a luminance histogram of the HDR scene and the adapted
// EV100 in a GPU buffer that the final pass reads, all without read-back. The adaptation runs every frame from the
// histogram of that frame, so a frame's display multiplier follows its own image.
internal sealed class PristonAutoExposure : IDisposable
{
    public const string Asset = "Effects/PostExposure";
    public const int Bins = 128;
    // Adaptation time constants: toward more light (the eye's fast light adaptation) and toward less.
    public const float BrighterSeconds = 0.6f;
    public const float DarkerSeconds = 2.2f;
    // Share of the light model's incident reading in the target by day; the night uses the image alone.
    public const float DayIncidentWeight = 0.6f;

    private readonly GraphicsDevice _device;
    private readonly Effect _effect;
    private readonly EffectTechnique _histogram;
    private readonly EffectTechnique _adapt;
    private readonly EffectParameter _source;
    private readonly EffectParameter _sourceSize;
    private readonly EffectParameter _meter;
    private readonly EffectParameter _adaptation;
    private readonly EffectParameter _compensation;
    private readonly EffectParameter _nightCurve;
    private readonly EffectParameter _histogramBuffer;
    private readonly EffectParameter _stateBuffer;
    private readonly EffectParameter _previousMeter;
    private float _previousIncidentEv100;
    private bool _snap = true;

    public PristonAutoExposure(GraphicsDevice device, Effect effect)
    {
        _device = device;
        _effect = effect;
        _histogram = effect.Techniques["Histogram"];
        _adapt = effect.Techniques["Adapt"];
        _source = effect.Parameters["Source"];
        _sourceSize = effect.Parameters["SourceSize"];
        _meter = effect.Parameters["Meter"];
        _adaptation = effect.Parameters["Adaptation"];
        _compensation = effect.Parameters["Compensation"];
        _nightCurve = effect.Parameters["NightCurve"];
        _histogramBuffer = effect.Parameters["Histogram"];
        _stateBuffer = effect.Parameters["State"];
        _previousMeter = effect.Parameters["PreviousMeter"];
        Histogram = new StructuredBuffer(device, sizeof(uint), Bins, BufferShaderAccess.ReadWrite);
        State = new StructuredBuffer(device, 16, 1, BufferShaderAccess.ReadWrite);
    }

    public StructuredBuffer Histogram { get; }

    // State[0] = (adapted EV100, display multiplier, reflected EV100, 1 once written); the final pass reads .y.
    public StructuredBuffer State { get; }

    // The next frame adopts its target at once (start, teleports, scene changes).
    public void Snap() => _snap = true;

    public void Run(Texture2D scene, in PristonExposureInputs inputs, float deltaSeconds, float? fixedEv100)
    {
        var width = scene.Width;
        var height = scene.Height;
        _source.SetValue(scene);
        _sourceSize.SetValue(new Vector4(width, height, 1f / width, 1f / height));
        _meter.SetValue(new Vector4(
            1f / inputs.PreExposure,
            inputs.IncidentEv100,
            inputs.IncidentWeight,
            MathHelper.Clamp(deltaSeconds, 0f, 0.5f)));
        _adaptation.SetValue(new Vector4(BrighterSeconds, DarkerSeconds, _snap ? 1f : 0f, fixedEv100 ?? -1000f));
        _compensation.SetValue(new Vector4(
            inputs.CompensationStops,
            PristonPhotometry.DayKeyStops,
            inputs.Underground ? 1f : 0f,
            inputs.PreExposure));
        _nightCurve.SetValue(new Vector4(
            PristonPhotometry.DimStartEv,
            PristonPhotometry.MaxDimStops,
            PristonPhotometry.DimScaleEv,
            (float)width / height));
        var previousIncident = _snap ? inputs.IncidentEv100 : _previousIncidentEv100;
        _previousMeter.SetValue(new Vector4(previousIncident, 0f, 0f, 0f));
        _previousIncidentEv100 = inputs.IncidentEv100;
        _histogramBuffer.SetValue(Histogram);
        _stateBuffer.SetValue(State);

        _effect.CurrentTechnique = _histogram;
        var pass = _histogram.Passes[0];
        pass.Apply();
        var blocksX = (width / 2 + pass.ThreadGroupSizeX - 1) / pass.ThreadGroupSizeX;
        var blocksY = (height / 2 + pass.ThreadGroupSizeY - 1) / pass.ThreadGroupSizeY;
        _device.Dispatch(Math.Max(blocksX, 1), Math.Max(blocksY, 1), 1);

        _effect.CurrentTechnique = _adapt;
        _adapt.Passes[0].Apply();
        _device.Dispatch(1, 1, 1);
        _snap = false;
    }

    public void Dispose()
    {
        Histogram.Dispose();
        State.Dispose();
        _effect.Dispose();
    }
}

// The light model's side of the exposure: the pre-exposure of the scene's values, the incident reading of the light
// model, its weight in the target, the user's compensation, whether the night curve applies and the hue the eye gives
// the night (the final pass's mesopic vision; zero is neutral grey).
internal readonly record struct PristonExposureInputs(
    float PreExposure,
    float IncidentEv100,
    float IncidentWeight,
    float CompensationStops,
    bool Underground,
    Vector3 NightHue = default);
