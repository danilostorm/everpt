using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.UI.Help;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Gameplay.UI;

// The help window of the system menu and the level guide's notice: the character the guide reads, the levels reached,
// the starter gifts, and the On/Off switch kept in the client settings.
public sealed partial class GameplayHud
{
    private HudTextFiles _helpTexts;
    private HudHelpWindow _helpWindow;
    private HudLevelGuide _levelGuide;
    // The level the guide last saw (0 before the first owner state), and a starter gift's order whose page waits for the
    // guide's files (-1 none).
    private int _guideLevel;
    private int _pendingStarter = -1;

    internal HudHelpWindow HelpWindow => _helpWindow;
    internal HudLevelGuide LevelGuide => _levelGuide;

    private bool LevelGuideOn => _context.Presentation.Requested.LevelGuide;

    // The help window and the notice sit under the dialogs, the settings and the restart window, which take their clicks;
    // the notice also waits while the helper pet's box holds its corner.
    private bool HelpCovered => ModalOpen || _settingsOpen || _restart.Open;

    private bool GuideBlocked => HelpCovered || _helpPet.Shown is not null;

    private void SetUpHelp()
    {
        _helpTexts = new HudTextFiles(_context.AssetRoot, Assets.Cp949.Encoding, _context.Log, "Help");
        _helpWindow = new HudHelpWindow(_helpTexts, _sprites.Request);
        _levelGuide = new HudLevelGuide(_helpTexts, _sprites.Request);
        _sprites.Request(HudHelpArt.All);
    }

    // A new character's first weapon and armour open their guide summaries, whatever the switch (the switch governs the
    // level pages only).
    public void ObserveGuide(PresenceUpdate update)
    {
        foreach (var effect in update.ItemEffects)
        {
            if (effect.Effect == ItemEffectKind.StarterGift && effect.Entity == SelfId && effect.Variant is 0 or 1)
            {
                _pendingStarter = effect.Variant;
            }
        }
    }

    private void UpdateHelp(OwnerState state)
    {
        if (state is null)
        {
            return;
        }

        var tempskron = SkillClasses.Get(_class).Brood == CharacterBrood.Tempskron;
        _helpWindow.Level = state.Level;
        _helpWindow.Tempskron = tempskron;
        var book = _helpWindow.Book;
        if (book.Subjects.Count == 0)
        {
            return;
        }

        if (_pendingStarter >= 0)
        {
            if (book.StarterPage(weapon: _pendingStarter == 0) is { } starter)
            {
                _levelGuide.Offer(starter, atOnce: true);
            }

            _pendingStarter = -1;
        }

        if (state.Level == _guideLevel)
        {
            return;
        }

        var offered = HudLevelGuide.Reached(_guideLevel, state.Level, state.Experience);
        _guideLevel = state.Level;
        if (offered && LevelGuideOn && book.HasPage(state.Level) && book.Page(state.Level, tempskron) is { } page)
        {
            _levelGuide.Offer(page, atOnce: false);
        }
    }

    private bool ClickHelp(Point pointer)
    {
        if (HelpCovered)
        {
            return false;
        }

        if (_helpWindow.Click(pointer, () => _context.Presentation.Requested = _context.Presentation.Requested with { LevelGuide = !LevelGuideOn }))
        {
            return true;
        }

        if (!_levelGuide.Click(pointer, GuideBlocked, out var more))
        {
            return false;
        }

        if (more is not null)
        {
            _helpWindow.OpenPage(more);
        }

        return true;
    }

    private bool CapturesHelp(Point pointer) => !HelpCovered && (_helpWindow.Contains(pointer) || _levelGuide.Captures(pointer, GuideBlocked));

    private void DrawHelp(Point pointer)
    {
        _painter.Flush();
        _levelGuide.Draw(_painter, pointer, _clock.Elapsed.TotalMilliseconds, GuideBlocked);
        _painter.Flush();
        _helpWindow.Draw(_painter, pointer, LevelGuideOn);
    }
}
