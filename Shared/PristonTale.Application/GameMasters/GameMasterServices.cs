using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Contracts.Characters;

namespace PristonTale.Application.GameMasters;

// Server switches the game-master commands read. A test server lets every game master use the experience and gold
// commands; a debug server turns a slash and an item code into a dropped copy of that item.
public sealed record GameMasterOptions(bool TestServer = false, bool DebugServer = false, bool PotionMonitor = true, int ServerCode = 0)
{
    public static readonly GameMasterOptions Default = new();
}

public readonly record struct BlockedLogin(string Account, long RemainingMs);

// Server-wide operations of the host that the commands may trigger.
public interface IServerOperations
{
    // Open connections, logged in or not.
    int OpenConnections { get; }

    // Players a new connection may join beyond; 0 when unlimited.
    int UserLimit { get; set; }

    string ServerName { get; set; }

    // Starts the shutdown countdown; false when one is already running.
    bool BeginShutdown();

    // Accounts whose logins are refused for now (too many failed passwords).
    IReadOnlyList<BlockedLogin> BlockedLogins();

    int ClearBlockedLogins();

    bool ReleaseBlockedLogin(string account);
}

// Used without a host (tools and tests): nothing is open and nothing can be shut down.
public sealed class DetachedServer : IServerOperations
{
    public int OpenConnections => 0;

    public int UserLimit { get; set; }

    public string ServerName { get; set; } = "";

    public bool ShutdownStarted { get; private set; }

    public bool BeginShutdown()
    {
        if (ShutdownStarted)
        {
            return false;
        }

        ShutdownStarted = true;
        return true;
    }

    public IReadOnlyList<BlockedLogin> BlockedLogins() => [];

    public int ClearBlockedLogins() => 0;

    public bool ReleaseBlockedLogin(string account) => false;
}

// The item overrides a game master sets for the copies the next commands make: a class specialization and the middle
// mode, both for one minute from the last of them.
public sealed class ItemCreationOverrides
{
    public const long LifetimeMs = 60_000;
    private readonly object _gate = new();
    private int _specJob;
    private int _performance;
    private long _specUntilMs;

    public void SetSpecialization(int jobCode, long nowMs)
    {
        lock (_gate)
        {
            _specJob = jobCode;
            _specUntilMs = nowMs + LifetimeMs;
        }
    }

    public void SetPerformance(int value, long nowMs)
    {
        lock (_gate)
        {
            _performance = value;
            _specUntilMs = nowMs + LifetimeMs;
        }
    }

    public ItemRollOverrides Current(long nowMs)
    {
        lock (_gate)
        {
            if (nowMs >= _specUntilMs)
            {
                return ItemRollOverrides.None;
            }

            var job = Enum.IsDefined((PlayerClass)_specJob) ? (uint)JobBits.Of((PlayerClass)_specJob) : 0u;
            return new ItemRollOverrides(job, _performance != 0);
        }
    }
}
