namespace PristonTale.Client.Net.WorldCatalog;

public abstract record ModelRenderInfo(uint TemplateId, string ModelInxPath, float ModelScale, string Name)
{
    // The ground shadow drawn while shadow maps are off: -1 none, 0 small to 3 large.
    public int ShadowSize { get; init; }
}
