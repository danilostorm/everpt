namespace PristonTale.Simulation.Quests;

// Level-milestone hunts: from the level shown, with the previous milestone finished, an NPC asks for a number of one
// monster; the reward is stat and skill points or a lasting bonus. The level 85 quest also hands out the Tear of Khalia,
// which a death takes back.
public static class LevelQuests
{
    private sealed record Spec(QuestCode Code, LevelQuestLog Bit, LevelQuestLog Requires, int Level, int Prey, int Count, string Documents);

    private static readonly Spec[] Specs =
    [
        new(QuestCode.Level30, LevelQuestLog.Level30, LevelQuestLog.None, 30, QuestMonsters.Bargon, 20, "LevelQuest30"),
        new(QuestCode.Level55, LevelQuestLog.Level55, LevelQuestLog.Level30, 55, QuestMonsters.Muffin, 20, "LevelQuest55"),
        new(QuestCode.Level55Morion, LevelQuestLog.Level55, LevelQuestLog.Level30, 55, QuestMonsters.DireBee, 20, "LevelQuest55-2"),
        new(QuestCode.Level70, LevelQuestLog.Level70, LevelQuestLog.Level55, 70, QuestMonsters.Typhoon, 10, "LevelQuest70"),
        new(QuestCode.Level80, LevelQuestLog.Level80, LevelQuestLog.Level70, 80, QuestMonsters.Ratoo, 5, "LevelQuest80"),
        new(QuestCode.Level85, LevelQuestLog.Level85, LevelQuestLog.Level80, 85, QuestMonsters.Grotesque, 10, "LevelQuest85"),
        new(QuestCode.Level90, LevelQuestLog.Level90, LevelQuestLog.Level85, 90, QuestMonsters.IronFist, 10, "LevelQuest90")
    ];

    public static bool IsLevelQuest(QuestCode code) => Find(code) is not null;

    private static Spec Find(QuestCode code)
    {
        foreach (var spec in Specs)
        {
            if (spec.Code == code)
            {
                return spec;
            }
        }

        return null;
    }

    // NPC trigger codes: 1001 the eight classic classes and 1002 the Morion side for level 30, 1003 and 1004 the two level 55 branches,
    // 1005 the level 70, 80 and 85 quests and 1006 the level 90 quest.
    public static void Talk(QuestContext context, int giverCode)
    {
        switch (giverCode)
        {
            case 1001:
                if (QuestClasses.IsClassicClass(context.Character))
                {
                    Offer(context, QuestCode.Level30);
                }

                break;
            case 1002:
                if (!context.Tempskron)
                {
                    Offer(context, QuestCode.Level30);
                }

                break;
            case 1003:
                if (context.Tempskron)
                {
                    Offer(context, QuestCode.Level55);
                }

                break;
            case 1004:
                if (!context.Tempskron)
                {
                    Offer(context, QuestCode.Level55Morion);
                }

                break;
            case 1005:
                Offer(context, QuestCode.Level70);
                Offer(context, QuestCode.Level80);
                Offer(context, QuestCode.Level85);
                break;
            case 1006:
                Offer(context, QuestCode.Level90);
                break;
        }
    }

    private static bool Eligible(QuestContext context, Spec spec)
    {
        var log = context.Journal.LevelLog;
        return context.Character.Level >= spec.Level && (log & (spec.Bit | spec.Requires)) == spec.Requires;
    }

    private static void Offer(QuestContext context, QuestCode code)
    {
        var spec = Find(code);
        if (!Eligible(context, spec))
        {
            return;
        }

        if (QuestSteps.Blocked(context, code))
        {
            // The level 55 masters stay silent while a rank-up quest runs; every other giver says another quest runs.
            if (!(code is QuestCode.Level55 or QuestCode.Level55Morion && QuestCodes.IsRankUp(context.Journal.Active.Code)))
            {
                context.Block();
            }

            return;
        }

        var active = context.Journal.Active;
        var state = active.Code == code ? active.State : 0;
        context.Show(Prompt(spec, state));
    }

    private static QuestPrompt Prompt(Spec spec, int state)
    {
        if (spec.Code == QuestCode.Level85)
        {
            return state switch
            {
                0 => QuestSteps.Confirm(spec.Code, 0, "LevelQuest85start", QuestTexts.SearchQuest),
                1 => QuestSteps.Proceed(spec.Code, 1, "LevelQuest85Path"),
                2 => QuestSteps.Close(spec.Code, 2, "LevelQuest85Path2"),
                _ => QuestSteps.Proceed(spec.Code, state, "LevelQuest85end")
            };
        }

        return state switch
        {
            0 => QuestSteps.Confirm(spec.Code, 0, spec.Documents + "start", QuestTexts.SearchQuest),
            1 => QuestSteps.Close(spec.Code, 1, spec.Documents + "Path"),
            _ => QuestSteps.Proceed(spec.Code, state, spec.Documents + "end")
        };
    }

    public static bool Accept(QuestContext context, QuestCode code, int state)
    {
        var spec = Find(code);
        if (spec is null || state != 0 || !context.Journal.Active.IsIdle || !Eligible(context, spec))
        {
            return QuestSteps.Stale(context);
        }

        if (code == QuestCode.Level85)
        {
            if (!QuestInventory.Has(context.Character, QuestItems.KhaliaTear) && !QuestInventory.TryGrant(context, [new QuestGrant(QuestItems.KhaliaTear)]))
            {
                return QuestInventory.Refused(context);
            }

            context.Journal.Active = new ActiveQuest { Code = code, State = 2 }.Hunting(spec.Prey, spec.Count);
            return true;
        }

        context.Journal.Active = new ActiveQuest { Code = code, State = 1 }.Hunting(spec.Prey, spec.Count);
        return true;
    }

    public static bool Proceed(QuestContext context, QuestCode code, int state)
    {
        var spec = Find(code);
        var active = context.Journal.Active;
        if (spec is null || active.Code != code || active.State != state)
        {
            return QuestSteps.Stale(context);
        }

        if (code == QuestCode.Level85 && state == 1)
        {
            // After a death the Tear of Khalia comes back and the hunt starts again.
            if (!QuestInventory.Has(context.Character, QuestItems.KhaliaTear) && !QuestInventory.TryGrant(context, [new QuestGrant(QuestItems.KhaliaTear)]))
            {
                return QuestInventory.Refused(context);
            }

            context.Journal.Active = (active with { State = 2 }).Hunting(spec.Prey, spec.Count);
            return true;
        }

        var completeState = code == QuestCode.Level85 ? 3 : 2;
        if (state != completeState)
        {
            return QuestSteps.Stale(context);
        }

        // The level 90 token comes with the completion when it is not already carried.
        if (code == QuestCode.Level90 && !QuestInventory.Has(context.Character, QuestItems.Level90Token)
            && !QuestInventory.TryGrant(context, [new QuestGrant(QuestItems.Level90Token)]))
        {
            return QuestInventory.Refused(context);
        }

        Complete(context, spec);
        return true;
    }

    private static void Complete(QuestContext context, Spec spec)
    {
        var character = context.Character;
        switch (spec.Code)
        {
            case QuestCode.Level30:
                character.ApplyQuestProgress(character.Tier, 5, 0, 0);
                context.Notice(QuestNoticeKind.Message, QuestTexts.StatPointsFive);
                break;
            case QuestCode.Level55:
            case QuestCode.Level55Morion:
                character.ApplyQuestProgress(character.Tier, 0, 1, 0);
                context.Notice(QuestNoticeKind.Message, QuestTexts.SkillPointOne);
                break;
            case QuestCode.Level70:
                character.ApplyQuestProgress(character.Tier, 5, 1, 0);
                context.Notice(QuestNoticeKind.Message, QuestTexts.StatFiveSkillOne);
                break;
            case QuestCode.Level80:
                character.ApplyQuestProgress(character.Tier, 5, 2, 0);
                context.Notice(QuestNoticeKind.Message, QuestTexts.StatFiveSkillTwo);
                break;
            case QuestCode.Level85:
                context.Notice(QuestNoticeKind.Message, QuestTexts.DeathPenaltyReduced);
                break;
            case QuestCode.Level90:
                context.Notice(QuestNoticeKind.Message, QuestTexts.VitalityForty);
                break;
        }

        context.Journal.LevelLog |= spec.Bit;
        QuestSteps.Finish(context, spec.Code);
    }

    // Kill credit: the hunt counts while it is running; a finished hunt moves the quest to its completion dialog.
    public static void OnKill(QuestContext context, int monsterCode)
    {
        var active = context.Journal.Active;
        var spec = Find(active.Code);
        var huntingState = active.Code == QuestCode.Level85 ? 2 : 1;
        if (spec is null || active.State != huntingState || monsterCode != spec.Prey || active.KillsNeeded == 0)
        {
            return;
        }

        var kills = active.Kills + 1;
        context.Notice(QuestNoticeKind.Chat, QuestTexts.Count(QuestMonsters.NameOf(spec.Prey), kills));
        if (kills < active.KillsNeeded)
        {
            context.Journal.Active = active with { Kills = kills };
            return;
        }

        if (active.Code == QuestCode.Level30)
        {
            context.Notice(QuestNoticeKind.Event, QuestTexts.TwentyBargons);
        }

        context.Journal.Active = active with { Kills = kills, State = huntingState + 1 };
    }

    // A death during the level 85 quest takes the Tear of Khalia back; the NPC hands it out again.
    public static bool OnDeath(QuestContext context)
    {
        var active = context.Journal.Active;
        if (active.Code != QuestCode.Level85 || active.State == 0)
        {
            return false;
        }

        context.Journal.Active = active with { State = 1 };
        return true;
    }
}
