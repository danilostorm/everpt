namespace PristonTale.Gameplay.UI.Rendering;

// A number a window shows every frame (gold, a price), formatted with digit groups only when it changes.
public sealed class HudNumberText
{
    private long _value;
    private string _text = "0";

    // Empty when the number is not shown.
    public string Of(long value, bool shown = true)
    {
        if (!shown)
        {
            return "";
        }

        if (value != _value)
        {
            _value = value;
            _text = value.ToString("N0");
        }

        return _text;
    }
}
