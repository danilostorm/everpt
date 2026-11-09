namespace PristonTale.Simulation.Social;

public enum ChatFloodVerdict
{
    Allowed,

    // This line froze the speaker for ChatRules.FloodPenaltyMs.
    Froze,

    Frozen,

    Repeated
}

// The flood rules of ChatRules for one speaker. The client checks what it sends and shows the complaint; the server
// checks what it receives with the same guard, so a client that skips the check gains nothing.
public sealed class ChatFloodGuard
{
    private string _lastSent = "";
    private double _lastSentMs = double.NegativeInfinity;
    private int _rapidLines;
    private double _frozenUntilMs = double.NegativeInfinity;

    public ChatFloodVerdict Check(string line, bool command, bool whisper, double nowMs)
    {
        if (nowMs < _frozenUntilMs)
        {
            return ChatFloodVerdict.Frozen;
        }

        if (!whisper && nowMs - _lastSentMs < ChatRules.FloodGapMs)
        {
            _rapidLines++;
            if (_rapidLines > ChatRules.FloodLines)
            {
                _frozenUntilMs = nowMs + ChatRules.FloodPenaltyMs;
                return ChatFloodVerdict.Froze;
            }
        }
        else
        {
            _rapidLines = 0;
        }

        if (!command && nowMs - _lastSentMs < ChatRules.RepeatWindowMs && string.Equals(_lastSent, line, StringComparison.OrdinalIgnoreCase))
        {
            return ChatFloodVerdict.Repeated;
        }

        _lastSent = line;
        _lastSentMs = nowMs;
        return ChatFloodVerdict.Allowed;
    }
}
