namespace PristonTale.Application.World;

// What the network edge caches on a frame (encoded entries every receiver shares); the field resets it when it reuses
// the frame for a later tick.
public interface IPresenceFrameAttachment
{
    void Reset(int entityCount);
}

// The entity state of one field at one tick, shared by every receiver's snapshot of that tick: the views, their grid and
// whatever the edge encodes once for all receivers. Frames are reused by later ticks of the same field instead of being
// allocated per tick. A receiver pins the frame while it encodes; the field reuses only frames nobody pinned and that no
// outbox can still hand out (older than the field's latest published frame).
public sealed class PresenceFrame
{
    // Thousands of receivers pin the same frame each tick, so pins are counted on separate cache lines (one stripe per
    // receiver group) instead of one contended counter.
    private const int Stripes = 64;
    private const int StripeStride = 16;
    private const int Rebuilding = -1;
    private readonly int[] _pins = new int[Stripes * StripeStride];
    // The frame's generation, or Rebuilding while the field rewrites it.
    private int _state = Rebuilding;
    private int _generation;
    private IPresenceFrameAttachment? _attachment;

    internal PresenceFrame(int entityCount)
    {
        Views = new EntityView[entityCount];
        Grid = EntityGrid.Build(Views);
    }

    public EntityView[] Views { get; private set; }
    public EntityGrid Grid { get; }

    // The tick's events by the entities they name; rebuilt with the frame.
    public EventRoutes Routes { get; } = new();
    public int Generation => _generation;

    // Set when a snapshot of this frame went to a writer that never releases it (a plain channel in tests); such a
    // frame is never reused.
    internal bool Retained { get; set; }

    // The edge's shared cache for this frame; the first encoder creates it, the others reuse it.
    public T Attachment<T>(Func<int, T> create) where T : class, IPresenceFrameAttachment
    {
        if (Volatile.Read(ref _attachment) is T existing)
        {
            return existing;
        }

        var created = create(Views.Length);
        return Interlocked.CompareExchange(ref _attachment, created, null) as T ?? created;
    }

    // Pins the frame for reading (by the receiver of the given id) if it still holds the given generation; false when the
    // field already reused it. A pin and a rebuild each write their own word and then read the other's, both through full
    // fences, so at least one of them sees the other and backs off.
    public bool TryPin(int generation, ulong receiver)
    {
        ref var pin = ref _pins[(int)(receiver % Stripes) * StripeStride];
        Interlocked.Increment(ref pin);
        if (Volatile.Read(ref _state) == generation)
        {
            return true;
        }

        Interlocked.Decrement(ref pin);
        return false;
    }

    public void Release(ulong receiver) => Interlocked.Decrement(ref _pins[(int)(receiver % Stripes) * StripeStride]);

    // Field tick only: claims an unpinned frame for a new tick, resizing the views when the entity count changed.
    internal bool TryRebuild(int entityCount, ViewArrays arrays)
    {
        if (Retained)
        {
            return false;
        }

        var open = Interlocked.Exchange(ref _state, Rebuilding);
        for (var stripe = 0; stripe < Stripes; stripe++)
        {
            if (Volatile.Read(ref _pins[stripe * StripeStride]) != 0)
            {
                Volatile.Write(ref _state, open);
                return false;
            }
        }

        _generation++;
        if (Views.Length != entityCount)
        {
            arrays.Return(Views);
            Views = arrays.Rent(entityCount);
        }

        _attachment?.Reset(entityCount);
        return true;
    }

    internal void Publish() => Interlocked.Exchange(ref _state, _generation);
}

// The frames of one field: a small pool reused tick after tick by the field's own step.
internal sealed class FieldPresenceFrames
{
    // Beyond this many frames (encoders holding many ticks at once) new frames are not kept for reuse.
    private const int MaxFrames = 16;
    private readonly List<PresenceFrame> _frames = new();
    private readonly ViewArrays _arrays = new();
    private PresenceFrame? _latest;

    public PresenceFrame Rent(int entityCount)
    {
        foreach (var frame in _frames)
        {
            if (!ReferenceEquals(frame, _latest) && frame.TryRebuild(entityCount, _arrays))
            {
                return frame;
            }
        }

        _frames.RemoveAll(frame => frame.Retained);
        var created = new PresenceFrame(entityCount);
        created.TryRebuild(entityCount, _arrays);
        if (_frames.Count < MaxFrames)
        {
            _frames.Add(created);
        }

        return created;
    }

    public void Published(PresenceFrame frame)
    {
        frame.Publish();
        _latest = frame;
    }
}

// View arrays of the exact lengths a field's entity count moves between (monsters spawn and die every few seconds), kept
// for reuse: a large field's views would otherwise be a fresh large-object allocation at every change.
internal sealed class ViewArrays
{
    private const int MaxKept = 32;
    private readonly Dictionary<int, Stack<EntityView[]>> _byLength = new();
    private int _kept;

    public EntityView[] Rent(int length) =>
        _byLength.TryGetValue(length, out var stack) && stack.TryPop(out var array) ? Taken(array) : new EntityView[length];

    public void Return(EntityView[] array)
    {
        if (array.Length == 0 || _kept >= MaxKept)
        {
            return;
        }

        if (!_byLength.TryGetValue(array.Length, out var stack))
        {
            stack = new Stack<EntityView[]>();
            _byLength.Add(array.Length, stack);
        }

        stack.Push(array);
        _kept++;
    }

    private EntityView[] Taken(EntityView[] array)
    {
        _kept--;
        return array;
    }
}
