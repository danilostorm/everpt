using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.Quests;

// The beginner chain for levels up to 39, run in order from the instructor in town. Each quest is offered by the
// instructor and finished either with her or with a second NPC of the player's tribe; hunts show the quest timer icon.
public static class ElementaryQuests
{
    public const int LevelCap = 39;
    private const int InstructorCode = 1014;

    // Quest trigger codes of the NPCs taking part: whether the NPC is the instructor, the quest it expects next (1-based,
    // 0 for any) and the tribe it serves (0 for both).
    private readonly record struct Role(bool Instructor, int Key, int Tribe);

    private const int TempskronTribe = 1;
    private const int MorionTribe = 2;

    private static bool TryRole(int giverCode, out Role role)
    {
        role = giverCode switch
        {
            InstructorCode => new Role(true, 0, 0),
            1015 => new Role(false, 1, TempskronTribe),
            1016 => new Role(false, 1, MorionTribe),
            1017 => new Role(false, 3, MorionTribe),
            1018 => new Role(false, 3, TempskronTribe),
            1019 => new Role(false, 4, TempskronTribe),
            1020 => new Role(false, 4, MorionTribe),
            1021 => new Role(false, 5, 0),
            1022 => new Role(false, 7, 0),
            _ => default
        };
        return giverCode is >= InstructorCode and <= 1022;
    }

    public static int Completed(ElementaryQuestLog log)
    {
        var count = 0;
        for (var index = 0; index < 7; index++)
        {
            if ((log & QuestCodes.ElementaryBit(index)) != 0)
            {
                count++;
            }
        }

        return count;
    }

    public static void Talk(QuestContext context, int giverCode)
    {
        if (!TryRole(giverCode, out var role))
        {
            return;
        }

        var journal = context.Journal;
        var active = journal.Active;
        if (!active.IsIdle && !QuestCodes.IsElementary(active.Code) && active.Code != QuestCode.FuryOfPhantom)
        {
            context.Block();
            return;
        }

        if (context.Character.Level > LevelCap)
        {
            if (role.Instructor)
            {
                // Past level 39 the instructor drops an unfinished beginner quest and its items.
                if (QuestCodes.IsElementary(active.Code))
                {
                    journal.Active = ActiveQuest.Idle;
                    QuestInventory.Remove(context.Character, QuestItems.Candy);
                    QuestInventory.Remove(context.Character, QuestItems.Cake);
                    QuestInventory.Remove(context.Character, QuestItems.LostHammer);
                }

                context.Show(QuestSteps.Close(QuestCode.None, 0, "haQuestLevel_Over"));
            }

            return;
        }

        var done = Completed(journal.ElementaryLog);
        if (role.Key != 0 && role.Key != done + 1)
        {
            return;
        }

        var tribe = context.Tempskron ? TempskronTribe : MorionTribe;
        var clanMatches = role.Tribe == 0 || role.Tribe == tribe;
        var code = QuestCodes.Elementary(done);
        var state = active.Code == code ? active.State : 0;
        var suffix = context.Tempskron ? "_T" : "_M";
        switch (done)
        {
            case 0:
                TalkA(context, role, clanMatches, state);
                break;
            case 1:
                if (role.Instructor)
                {
                    TalkHuntAtInstructor(context, QuestCode.ElementaryB, state, "haQuestterrified_Bstart" + suffix, "haQuestterrified_Bpass" + suffix,
                        "haQuestterrified_Bend");
                }

                break;
            case 2:
                TalkC(context, role, clanMatches, state, suffix);
                break;
            case 3:
                TalkHuntWithFinisher(context, role, clanMatches, QuestCode.ElementaryD, state, "haQuestGoblin_Dstart" + suffix, "haQuestGoblin_Dpass" + suffix,
                    "haQuestGoblin_Dend");
                break;
            case 4:
                TalkHuntWithFinisher(context, role, true, QuestCode.ElementaryE, state, "haQuestundead_Estart" + suffix, "haQuestundead_Epass" + suffix,
                    "haQuestundead_Eend");
                break;
            case 5:
                if (role.Instructor)
                {
                    TalkHuntAtInstructor(context, QuestCode.ElementaryF, state, "haQuestcyclops_Fstart", "haQuestcyclops_Fpass", "haQuestcyclops_Fend");
                }

                break;
            case 6:
                TalkG(context, role, state);
                break;
        }
    }

    // A: the instructor's cake goes to the warehouse keeper of the player's tribe.
    private static void TalkA(QuestContext context, Role role, bool clanMatches, int state)
    {
        const QuestCode code = QuestCode.ElementaryA;
        if (role.Instructor)
        {
            context.Show(state switch
            {
                0 => QuestSteps.Confirm(code, 0, "haQuesttired_Astart", QuestTexts.GetQuest),
                1 => QuestSteps.Close(code, 1, "haQuesttired_Apass1"),
                _ => QuestSteps.Proceed(code, 2, "haQuesttired_Aend")
            });
            return;
        }

        if (clanMatches && state >= 1 && QuestInventory.Has(context.Character, QuestItems.Cake))
        {
            QuestInventory.Remove(context.Character, QuestItems.Cake);
            context.Journal.Active = context.Journal.Active with { State = 2 };
            context.Show(QuestSteps.Close(code, 2, "haQuesttired_Apass"));
        }
    }

    // C: the instructor's candy goes to the smith of the player's tribe.
    private static void TalkC(QuestContext context, Role role, bool clanMatches, int state, string suffix)
    {
        const QuestCode code = QuestCode.ElementaryC;
        if (role.Instructor)
        {
            if (state == 0)
            {
                context.Show(QuestSteps.Confirm(code, 0, "haQuestcandy_Cstart" + suffix, QuestTexts.GetQuest));
            }
            else if (state == 1)
            {
                context.Show(QuestSteps.Close(code, 1, "haQuestcandy_Cpass"));
            }

            return;
        }

        if (clanMatches && state >= 1 && QuestInventory.Has(context.Character, QuestItems.Candy))
        {
            context.Journal.Active = context.Journal.Active with { State = 2 };
            context.Show(QuestSteps.Proceed(code, 2, "haQuestcandy_Cend"));
        }
    }

    // B and F: offered and finished by the instructor.
    private static void TalkHuntAtInstructor(QuestContext context, QuestCode code, int state, string start, string pass, string end)
    {
        context.Show(state switch
        {
            0 => QuestSteps.Confirm(code, 0, start, QuestTexts.GetQuest),
            2 => QuestSteps.Close(code, 2, pass),
            _ => QuestSteps.Proceed(code, state, end)
        });
    }

    // D and E: offered by the instructor and finished by a guard (D) or the smith (E).
    private static void TalkHuntWithFinisher(QuestContext context, Role role, bool clanMatches, QuestCode code, int state, string start, string pass,
        string end)
    {
        if (role.Instructor)
        {
            if (state == 0)
            {
                context.Show(QuestSteps.Confirm(code, 0, start, QuestTexts.GetQuest));
            }
            else if (state == 2)
            {
                context.Show(QuestSteps.Close(code, 2, pass));
            }

            return;
        }

        if (clanMatches && state == 3)
        {
            context.Show(QuestSteps.Proceed(code, 3, end));
        }
    }

    // G: the lost hammer of the smith, dropped by Bargons while the quest runs.
    private static void TalkG(QuestContext context, Role role, int state)
    {
        const QuestCode code = QuestCode.ElementaryG;
        if (role.Instructor)
        {
            if (state == 0)
            {
                context.Show(QuestSteps.Confirm(code, 0, "haQuesthammer_Gstart", QuestTexts.GetQuest));
            }
            else if (state == 1)
            {
                context.Show(QuestSteps.Close(code, 1, "haQuesthammer_Gpass"));
            }

            return;
        }

        if (state < 1)
        {
            return;
        }

        if (state < 3 && QuestInventory.Has(context.Character, QuestItems.LostHammer))
        {
            QuestInventory.Remove(context.Character, QuestItems.LostHammer);
            context.Journal.Active = context.Journal.Active with { State = 3 };
            state = 3;
        }

        if (state == 3)
        {
            context.Show(QuestSteps.Proceed(code, 3, "haQuesthammer_Gend"));
        }
    }

    public static bool Accept(QuestContext context, QuestCode code, int state)
    {
        var journal = context.Journal;
        var index = QuestCodes.ElementaryIndex(code);
        if (state != 0 || !journal.Active.IsIdle || context.Character.Level > LevelCap || index != Completed(journal.ElementaryLog))
        {
            return QuestSteps.Stale(context);
        }

        var quest = new ActiveQuest { Code = code, State = 1 };
        switch (code)
        {
            case QuestCode.ElementaryA:
                if (!QuestInventory.Has(context.Character, QuestItems.Cake) && !QuestInventory.TryGrant(context, [new QuestGrant(QuestItems.Cake)]))
                {
                    return QuestInventory.Refused(context);
                }

                break;
            case QuestCode.ElementaryC:
                if (!QuestInventory.Has(context.Character, QuestItems.Candy) && !QuestInventory.TryGrant(context, [new QuestGrant(QuestItems.Candy)]))
                {
                    return QuestInventory.Refused(context);
                }

                break;
            case QuestCode.ElementaryB:
                quest = QuestSteps.StartHunt(context, quest with { State = 2 }, context.Tempskron ? QuestMonsters.Hopy : QuestMonsters.Rabie, 5,
                    QuestTimerKind.Display);
                break;
            case QuestCode.ElementaryD:
                quest = QuestSteps.StartHunt(context, quest with { State = 2 }, context.Tempskron ? QuestMonsters.Hobgoblin : QuestMonsters.NorthGoblin, 5,
                    QuestTimerKind.Display);
                break;
            case QuestCode.ElementaryE:
                quest = QuestSteps.StartHunt(context, quest with { State = 2 }, context.Tempskron ? QuestMonsters.Skeleton : QuestMonsters.Corrupt, 10,
                    QuestTimerKind.Display);
                break;
            case QuestCode.ElementaryF:
                quest = QuestSteps.StartHunt(context, quest with { State = 2 }, QuestMonsters.Cyclops, 10, QuestTimerKind.Display);
                break;
        }

        journal.Active = quest;
        return true;
    }

    public static bool Proceed(QuestContext context, QuestCode code, int state)
    {
        var active = context.Journal.Active;
        var completeState = code switch
        {
            QuestCode.ElementaryA or QuestCode.ElementaryC => 2,
            _ => 3
        };
        if (active.Code != code || active.State != state || state != completeState)
        {
            return QuestSteps.Stale(context);
        }

        var character = context.Character;
        if (code == QuestCode.ElementaryC && !QuestInventory.Has(character, QuestItems.Candy))
        {
            return QuestSteps.Stale(context);
        }

        if (code == QuestCode.ElementaryC)
        {
            QuestInventory.Remove(character, QuestItems.Candy);
        }

        if (!QuestInventory.TryGrant(context, Rewards(context, code)))
        {
            return QuestInventory.Refused(context);
        }

        context.Journal.ElementaryLog |= QuestCodes.ElementaryBit(QuestCodes.ElementaryIndex(code));
        QuestSteps.Finish(context, code);
        return true;
    }

    private static IReadOnlyList<QuestGrant> Rewards(QuestContext context, QuestCode code) => code switch
    {
        QuestCode.ElementaryA => QuestClasses.BeginnerWeapon(QuestClasses.ClassOf(context.Character)) is var weapon and not 0
            ? [new QuestGrant(weapon, ClassSpecific: true)]
            : [],
        QuestCode.ElementaryB => [new QuestGrant(ItemCodes.Parse("OA202"), ClassSpecific: true)],
        QuestCode.ElementaryC =>
        [
            new QuestGrant(ItemCodes.Parse("PL101"), 10), new QuestGrant(ItemCodes.Parse("PS101"), 10), new QuestGrant(ItemCodes.Parse("PM101"), 10)
        ],
        QuestCode.ElementaryD =>
        [
            new QuestGrant(ItemCodes.Parse("OA106"), ClassSpecific: true), new QuestGrant(ItemCodes.Parse("OR106"), Copies: 2, ClassSpecific: true)
        ],
        QuestCode.ElementaryE => [new QuestGrant(ItemCodes.Parse("FO104"), Copies: 2)],
        QuestCode.ElementaryF =>
        [
            new QuestGrant(ItemCodes.Parse("PL103"), 20), new QuestGrant(ItemCodes.Parse("PS103"), 20), new QuestGrant(ItemCodes.Parse("PM103"), 20)
        ],
        QuestCode.ElementaryG => [new QuestGrant(ItemCodes.Parse("FO106"), Copies: 2)],
        _ => []
    };

    // Kill credit for the B, D, E and F hunts; a finished hunt sends the player back to the NPC that ends the quest.
    public static void OnKill(QuestContext context, int monsterCode)
    {
        var active = context.Journal.Active;
        if (!QuestCodes.IsElementary(active.Code) || active.State != 2 || active.Target == 0 || active.Target != monsterCode || active.KillsNeeded == 0)
        {
            return;
        }

        var kills = active.Kills + 1;
        var name = QuestMonsters.NameOf(active.Target);
        context.Notice(QuestNoticeKind.Chat, QuestTexts.Count(name, kills));
        if (kills < active.KillsNeeded)
        {
            context.Journal.Active = active with { Kills = kills };
            return;
        }

        context.Notice(QuestNoticeKind.Event, QuestTexts.HuntFinished(active.KillsNeeded, name));
        context.Journal.Active = active with { Kills = kills, State = 3 };
    }
}
