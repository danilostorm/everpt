using System.Text.Json.Serialization;

namespace PristonTale.Engine3D.Rendering;

// Quality presets of the renderer (ClientSettings.WithQuality). Custom marks settings edited one by one.
[JsonConverter(typeof(JsonStringEnumConverter<GraphicsQuality>))]
public enum GraphicsQuality
{
    Custom,
    Low,
    Medium,
    High,
    Ultra
}

// Anti-aliasing of the HDR scene: none, FXAA or SMAA 1x on the display image, MSAA of the scene target
// (MultiSampleCount samples), or temporal anti-aliasing (jittered frames, motion vectors and a history).
[JsonConverter(typeof(JsonStringEnumConverter<AntiAliasingMode>))]
public enum AntiAliasingMode
{
    Off,
    Fxaa,
    Smaa,
    Msaa,
    Taa
}

// Display transform of the final pass (Shaders/Passes/PostFinal.fx).
[JsonConverter(typeof(JsonStringEnumConverter<TonemapperMode>))]
public enum TonemapperMode
{
    PbrNeutral,
    AgX,
    Aces
}

// Strength of the bloom (the point-spread function of the lens): Low 3 %, High 5 % of the image.
[JsonConverter(typeof(JsonStringEnumConverter<BloomQuality>))]
public enum BloomQuality
{
    Off,
    Low,
    High
}

// Filter of the sun's cascaded shadows: Pcf, a fixed 16-tap kernel, or Pcss, percentage-closer soft shadows (a penumbra
// that widens with the distance to the occluder).
[JsonConverter(typeof(JsonStringEnumConverter<ShadowFilterMode>))]
public enum ShadowFilterMode
{
    Pcf,
    Pcss
}
