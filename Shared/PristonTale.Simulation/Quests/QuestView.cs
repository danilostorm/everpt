using PristonTale.Simulation.Characters;

namespace PristonTale.Simulation.Quests;

// What the owner's client shows of its quests: the running quest with its hunt and timer, the quest window lines and
// whether a skill redistribution is offered. Sent again whenever the journal revision changes.
public sealed record QuestView(
    long Revision,
    QuestCode Code,
    int State,
    string TargetName,
    int Kills,
    int KillsNeeded,
    QuestTimerKind Timer,
    long TimerMs,
    IReadOnlyList<QuestBoardEntry> Progress,
    IReadOnlyList<QuestBoardEntry> Finished)
{
    public bool SkillResetOffered { get; init; }
    public int WeaponStage { get; init; }
    public int WeaponKillsLeft { get; init; }

    public static QuestView Capture(PlayerCharacter character)
    {
        var journal = character.Quests;
        var active = journal.Active;
        return new QuestView(journal.Revision, active.Code, active.State, QuestMonsters.NameOf(active.Target), active.Kills, active.KillsNeeded,
            active.Timer, active.TimerMs, QuestBoard.Progress(character), QuestBoard.Finished(character))
        {
            SkillResetOffered = journal.SkillResetOffered,
            WeaponStage = active.WeaponStage,
            WeaponKillsLeft = active.WeaponKillsLeft
        };
    }
}
