namespace PristonTale.Gameplay.Screens.Framework;

// Characters the window received since the last frame. The window's text input event and the frame both run on the game
// thread; the queue is bounded so a frame that never drains cannot grow it.
public sealed class TypedText
{
    public const int Capacity = 256;
    private readonly Queue<char> _pending = new();

    public void Push(char value)
    {
        if (_pending.Count < Capacity)
        {
            _pending.Enqueue(value);
        }
    }

    public char[] Drain()
    {
        if (_pending.Count == 0)
        {
            return [];
        }

        var drained = _pending.ToArray();
        _pending.Clear();
        return drained;
    }
}
