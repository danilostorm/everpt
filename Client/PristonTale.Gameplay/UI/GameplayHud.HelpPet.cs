using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.UI.Help;
using PristonTale.Gameplay.UI.HelpPet;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.UI;

// The beginner's helper pet: what the HUD tells it each frame, and the parts of the interface it speaks of when pointed at
// (0), clicked (1) or right clicked (2), as the original's: the menu buttons on all three, the walk and camera toggles
// when pointed at and the camera and map toggles when clicked, the skill sockets and the minimap on all three, the chat
// window when pointed at or clicked, and the gauges and the day clock when clicked. The keys speak in GameplayHud.Input.
public sealed partial class GameplayHud
{
    // By HudMenuButton and HudToggle.
    private static readonly string[] MenuTopics = ["CarStatus", "InvenTory", "Skill", "Party", "Quest", "System"];
    private static readonly string[] ToggleTopics = ["RunAndWalk", "Camera_Hand", "Mini_Map"];
    private const int KeyIndex = 3;
    private readonly HudHelpPet _helpPet;

    // The local player's helper pet as the scene draws it (GameScene.SelfHelperPet).
    public HudHelperPetView HelperPet { get; set; }

    private void UpdateHelpPet(OwnerState state, HudLayout layout, Point pointer, Point design, MouseState mouse, double nowMs)
    {
        if (state is not null)
        {
            var percent = (int)(HudBar.ExperienceFraction(state) * 100);
            _helpPet.Update(new HelpPetState(HelperPet.Kind, state.Level, percent, HudLevelGuide.Fresh(state.Level, state.Experience), _class, InTown, PlayerActing),
                nowMs);
        }

        if (ModalOpen)
        {
            _helpPet.Point(null, nowMs);
            return;
        }

        _helpPet.Point(PointedTopic(layout, pointer, design, 0), nowMs);
        var action = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released ? 1
            : mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released ? 2
            : 0;
        if (action != 0 && PointedTopic(layout, pointer, design, action) is { } topic)
        {
            _helpPet.Say(topic, action, nowMs);
        }
    }

    private string PointedTopic(HudLayout layout, Point pointer, Point design, int action)
    {
        if (_hoveredButton is { } button)
        {
            return MenuTopics[(int)button];
        }

        if (_hoveredToggle is { } toggle)
        {
            var speaks = action == 0 ? toggle != HudToggle.Map : action == 1 && toggle != HudToggle.Walk;
            return speaks ? ToggleTopics[(int)toggle] : null;
        }

        if (HudBar.Socket(false).Contains(design))
        {
            return "LeftSkill";
        }

        if (HudBar.Socket(true).Contains(design))
        {
            return "RightSkill";
        }

        if (MinimapShown && HudMinimap.Box(layout).Contains(pointer))
        {
            return "Mini_Map";
        }

        if (action == 2)
        {
            return null;
        }

        if (_chat.Window.Contains(layout, pointer, SideWindowOpen))
        {
            return "ChatBox";
        }

        if (action == 0)
        {
            return null;
        }

        return HudBar.LifeHover.Contains(design) ? "Bar_Life"
            : HudBar.ManaHover.Contains(design) ? "Bar_Mana"
            : HudBar.StaminaHover.Contains(design) ? "Bar_Stamina"
            : HudBar.ExperienceHover.Contains(design) ? "Bar_Exp"
            : HudBar.DayClockHover.Contains(design) ? "DayandNight"
            : null;
    }

    private void HelpPetKey(string topic, int index = KeyIndex) => _helpPet.Say(topic, index, _clock.Elapsed.TotalMilliseconds);
}
