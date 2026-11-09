namespace PristonTale.Engine3D.Rendering;

internal enum PristonRenderPass
{
    Opaque,
    AlphaTest,
    Transparent
}

internal readonly record struct PristonRenderQueueItem<TBatch>(
    TBatch Batch,
    ulong StateSortKey,
    float SortDepth);

internal sealed class PristonRenderQueue<TBatch>
{
    private readonly List<PristonRenderQueueItem<TBatch>> _opaque = [];
    private readonly List<PristonRenderQueueItem<TBatch>> _alphaTest = [];
    private readonly List<PristonRenderQueueItem<TBatch>> _transparent = [];
    private readonly List<PristonRenderQueueItem<TBatch>> _shadowCasters = [];

    public IReadOnlyList<PristonRenderQueueItem<TBatch>> Opaque => _opaque;
    public IReadOnlyList<PristonRenderQueueItem<TBatch>> AlphaTest => _alphaTest;
    public IReadOnlyList<PristonRenderQueueItem<TBatch>> Transparent => _transparent;
    public IReadOnlyList<PristonRenderQueueItem<TBatch>> ShadowCasters => _shadowCasters;

    public int Count => _opaque.Count + _alphaTest.Count + _transparent.Count;

    public void Clear()
    {
        _opaque.Clear();
        _alphaTest.Clear();
        _transparent.Clear();
        _shadowCasters.Clear();
    }

    public void AddShadowCaster(TBatch batch, ulong stateSortKey, float sortDepth)
    {
        _shadowCasters.Add(new PristonRenderQueueItem<TBatch>(batch, stateSortKey, sortDepth));
    }

    public void Add(TBatch batch, PristonRenderPass pass, ulong stateSortKey, float sortDepth)
    {
        var item = new PristonRenderQueueItem<TBatch>(batch, stateSortKey, sortDepth);
        switch (pass)
        {
            case PristonRenderPass.Opaque:
                _opaque.Add(item);
                break;
            case PristonRenderPass.AlphaTest:
                _alphaTest.Add(item);
                break;
            case PristonRenderPass.Transparent:
                _transparent.Add(item);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(pass), pass, null);
        }
    }

    public void Sort(bool includeShadowCasters = true)
    {
        _opaque.Sort(CompareStateThenFrontToBack);
        _alphaTest.Sort(CompareStateThenFrontToBack);
        _transparent.Sort(CompareBackToFront);
        if (includeShadowCasters)
        {
            _shadowCasters.Sort(CompareStateThenFrontToBack);
        }
    }

    private static int CompareStateThenFrontToBack(PristonRenderQueueItem<TBatch> left, PristonRenderQueueItem<TBatch> right)
    {
        var state = left.StateSortKey.CompareTo(right.StateSortKey);
        return state != 0 ? state : left.SortDepth.CompareTo(right.SortDepth);
    }

    private static int CompareBackToFront(PristonRenderQueueItem<TBatch> left, PristonRenderQueueItem<TBatch> right)
    {
        return right.SortDepth.CompareTo(left.SortDepth);
    }
}
