namespace PristonTale.Engine3D.Rendering;

public readonly record struct DisplayModeOption(int Monitor, int Width, int Height, int RefreshRate);

public sealed class PresentationSettings
{
    public ClientSettings Requested { get; set; } = new();
    public string EffectiveDisplay { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool AwaitingConfirmation { get; set; }
    public int ConfirmationSeconds { get; set; }
    public bool ConfirmRequested { get; set; }
    public bool RevertRequested { get; set; }
    public int MonitorCount { get; set; } = 1;
    // The modes the monitor reports (at least 800 x 600), and its desktop size: the only sizes the settings offer.
    public IReadOnlyList<DisplayModeOption> DisplayModes { get; set; } = Array.Empty<DisplayModeOption>();
    public DisplayModeOption Desktop { get; set; }
    public ClientSettings Rendering { get; private set; } = new();
    public event Action<ClientSettings> RenderingChanged;

    public void ApplyRendering(ClientSettings settings)
    {
        if (settings.HasSameRendering(Rendering))
        {
            return;
        }

        Rendering = settings;
        RenderingChanged?.Invoke(settings);
    }

    public bool VerticalSync
    {
        get => Requested.VerticalSync;
        set => Requested = Requested with { VerticalSync = value };
    }
}
