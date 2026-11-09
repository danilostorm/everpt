namespace PristonTale.Engine3D.Rendering.Lighting;

// The exposure side of a frame's light (PristonPhotometry): PreExposure multiplies every radiance the scene passes
// write (pi / incident illuminance), IncidentEv100 is the light model's reading, DisplayScale is the final pass's
// multiplier when the exposure sits at that reading (compensation included) and EmissionScale the radiance of an
// emissive albedo of 1.
internal readonly record struct PristonFrameExposure(
    float PreExposure,
    float IncidentEv100,
    float DisplayScale,
    float EmissionScale,
    bool Underground)
{
    public static PristonFrameExposure Neutral { get; } = new(1f, 0f, 1f, 1f, false);

    public static PristonFrameExposure ForIlluminance(float horizontalLux, bool underground, float compensationStops, float emissiveLuminance)
    {
        var lux = MathF.Max(horizontalLux, 1e-3f);
        var ev = PristonPhotometry.Ev100FromIlluminance(lux);
        var preExposure = PristonPhotometry.ExposureAt(ev);
        var displayScale = MathF.Pow(2f, PristonPhotometry.CompensationStops(ev, underground) + compensationStops);
        var emission = MathF.Max(1f / displayScale, emissiveLuminance * preExposure);
        return new PristonFrameExposure(preExposure, ev, displayScale, emission, underground);
    }
}
