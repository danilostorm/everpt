namespace PristonTale.Simulation.Quests;

// First rank-up of the Tempskron classes: a claw, a horn and a tail from the three kinds of wolverine (one of each)
// are brought to the master, who then promotes the class.
public static class RankUpByTrophies
{
    public const QuestCode Code = QuestCode.RankUpTrophies;
    private const int Collecting = 1;

    private static readonly uint[] Trophies = [QuestItems.SilverWolverineTrophy, QuestItems.BronzeWolverineTrophy, QuestItems.GoldenWolverineTrophy];

    public static bool IsTrophy(uint code) => Array.IndexOf(Trophies, code) >= 0;

    private static bool HasAll(QuestContext context) => Trophies.All(code => QuestInventory.Has(context.Character, code));

    public static bool TalkAtMaster(QuestContext context)
    {
        var active = context.Journal.Active;
        if (active.Code != Code || active.State != Collecting || context.Character.Tier != 0 || !HasAll(context))
        {
            return false;
        }

        context.Show(RankUpRules.RankUpPrompt(context, Code, Collecting, "ChangeJob2"));
        return true;
    }

    public static bool Accept(QuestContext context, int state)
    {
        var journal = context.Journal;
        if (state == 0)
        {
            if (!journal.Active.IsIdle || RankUpRules.Offer(context) != Code)
            {
                return QuestSteps.Stale(context);
            }

            journal.Active = new ActiveQuest { Code = Code, State = Collecting };
            return true;
        }

        if (state != Collecting || journal.Active.Code != Code || journal.Active.State != Collecting || context.Character.Tier != 0 || !HasAll(context)
            || !context.Npc.HomeMaster || context.Npc.ClassChangeGroup <= 0)
        {
            return QuestSteps.Stale(context);
        }

        foreach (var trophy in Trophies)
        {
            QuestInventory.Remove(context.Character, trophy);
        }

        QuestSteps.Finish(context, Code);
        RankUpRules.RankUp(context);
        return true;
    }

    // One of each trophy while the quest runs.
    public static bool Accepts(QuestContext context, uint code) =>
        context.Journal.Active.Code == Code && IsTrophy(code) && !QuestInventory.Has(context.Character, code);
}
