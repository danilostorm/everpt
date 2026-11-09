using Microsoft.Xna.Framework.Content;

namespace PristonTale.Tests;

// The probes read the client's compiled content from wherever it was built. MonoGame's Native platform resolves content
// only under the executable's folder (TitleContainer rejects rooted paths and normalises away '..'), so the probes open
// the .xnb files from an absolute root themselves.
internal sealed class AbsoluteContentManager : ContentManager
{
    public AbsoluteContentManager(IServiceProvider services, string root)
        : base(services, Path.GetFullPath(root))
    {
    }

    protected override Stream OpenStream(string assetName)
    {
        var path = Path.Combine(RootDirectory, assetName + ".xnb");
        try
        {
            return File.OpenRead(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new ContentLoadException($"The content file was not found: {path}", exception);
        }
    }
}
