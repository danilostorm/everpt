namespace PristonTale.Gameplay.UI.Help;

// The art of the help window, the game guide and the level guide's notice.
public static class HudHelpArt
{
    private const string Help = "image/Sinimage/Help/";

    // The help window's icon and label, lit and grey; the guide's.
    public const string HelpIcon = Help + "help_icon01.tga";
    public const string HelpIconGrey = Help + "help_icon01_.tga";
    public const string HelpLabel = Help + "help_icon01t.tga";
    public const string GuideIcon = Help + "help_icon02.tga";
    public const string GuideIconGrey = Help + "help_icon02_.tga";
    public const string GuideLabel = Help + "help_icon02t.tga";
    // The guide pages' "help see" label beside the On/Off switch, and the label back to the guide's list.
    public const string NoticeLabel = Help + "help_icon00.tga";
    public const string ListLabel = Help + "help_Glist.tga";
    public const string SwitchOn = Help + "G_help-On.bmp";
    public const string SwitchOff = Help + "G_help-Off.bmp";
    // The level guide's notice: its two blinking pictures, and its box's close button.
    public static readonly string[] Notice = [Help + "help_icon03.tga", Help + "help_icon04.tga"];
    public const string Close = "image/clanImage/MessageBox/c.bmp";
    public const string CloseLit = "image/clanImage/MessageBox/c1.bmp";

    public static readonly string[] All =
        [HelpIcon, HelpIconGrey, HelpLabel, GuideIcon, GuideIconGrey, GuideLabel, NoticeLabel, ListLabel, SwitchOn, SwitchOff, .. Notice, Close, CloseLit];
}
