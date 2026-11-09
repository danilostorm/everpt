using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Quests;

namespace PristonTale.Application.World;

// What the owner's client learns about its quests in one snapshot: the journal view when it changed, quest notices
// (chat lines, messages, the fanfare) and a dialog the world opened on its own, such as the victory of a duel.
public sealed record QuestUpdate(QuestView? View, IReadOnlyList<QuestNotice> Notices, QuestPrompt? Prompt)
{
    public bool IsEmpty => View is null && Notices.Count == 0 && Prompt is null;
}

// Tick-owned quest bookkeeping of one connected player: work for the next quest upkeep, notices and an opened dialog
// waiting for publication, and the duel the player fights.
public sealed class PlayerQuestState
{
    public const int MaxPendingDrops = 64;
    public const int MaxPendingNotices = 64;

    private readonly List<PendingQuestDrop> _drops = [];
    private readonly List<QuestNotice> _notices = [];
    private QuestPrompt? _prompt;
    private long _publishedRevision = -1;

    public IReadOnlyList<PendingQuestDrop> Drops => _drops;

    // The quest upkeep has work: pending drops, a grown or expired quest weapon or a leftover item.
    public bool UpkeepDue { get; set; }

    // The upkeep worker was told and has not finished yet.
    public bool UpkeepSignaled { get; set; }

    public long NextUpkeepCheckMs { get; set; }

    // Running-quest time not yet handed to the quest step.
    public float StepCarryMs { get; set; }

    // A death handled for the quests; cleared when the player stands again.
    public bool DeathHandled { get; set; }

    // A field change the world applies after the field phase (back home after dying in the arena).
    public QuestTravel? PendingTravel { get; set; }

    // The opponent of the player's current duel (0 without one).
    public ulong DuelOpponentId { get; set; }

    public void AddDrop(PendingQuestDrop drop)
    {
        if (_drops.Count < MaxPendingDrops)
        {
            _drops.Add(drop);
        }

        UpkeepDue = true;
    }

    public PendingQuestDrop[] TakeDrops()
    {
        var drops = _drops.ToArray();
        _drops.Clear();
        return drops;
    }

    public void Post(IReadOnlyList<QuestNotice> notices, QuestPrompt? prompt = null)
    {
        foreach (var notice in notices)
        {
            if (_notices.Count < MaxPendingNotices)
            {
                _notices.Add(notice);
            }
        }

        if (prompt is not null)
        {
            _prompt = prompt;
        }
    }

    public QuestUpdate? TakeUpdate(PlayerCharacter character)
    {
        var journal = character.Quests;
        QuestView? view = null;
        if (journal.Revision != _publishedRevision)
        {
            _publishedRevision = journal.Revision;
            view = QuestView.Capture(character);
        }

        if (view is null && _notices.Count == 0 && _prompt is null)
        {
            return null;
        }

        var update = new QuestUpdate(view, _notices.Count == 0 ? [] : _notices.ToArray(), _prompt);
        _notices.Clear();
        _prompt = null;
        return update;
    }
}

// Accumulates the quest updates of the snapshots an outbox coalesces: notices stay in order (the oldest beyond the bound
// are dropped), the newest view and dialog win.
internal sealed class QuestUpdateBuffer
{
    public const int MaxNotices = 256;

    private readonly List<QuestNotice> _notices = [];
    private QuestView? _view;
    private QuestPrompt? _prompt;

    public void Add(QuestUpdate? update)
    {
        if (update is null)
        {
            return;
        }

        _view = update.View ?? _view;
        _prompt = update.Prompt ?? _prompt;
        _notices.AddRange(update.Notices);
        if (_notices.Count > MaxNotices)
        {
            _notices.RemoveRange(0, _notices.Count - MaxNotices);
        }
    }

    public QuestUpdate? Take()
    {
        if (_view is null && _notices.Count == 0 && _prompt is null)
        {
            return null;
        }

        var update = new QuestUpdate(_view, _notices.ToArray(), _prompt);
        _view = null;
        _prompt = null;
        _notices.Clear();
        return update;
    }
}
