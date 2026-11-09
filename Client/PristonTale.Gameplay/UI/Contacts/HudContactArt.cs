namespace PristonTale.Gameplay.UI.Contacts;

// Asset-relative paths of the party window's friends page. A button image ending in '_' is its idle look; the plain one is
// drawn under the pointer and for the list shown.
public static class HudContactArt
{
    private const string Party = "image/party/";

    public const string Title = Party + "title-NoClan_chatting.tga";
    public const string Whispering = Party + "chattng-icon-c.bmp";
    public const string Online = Party + "chattng-icon_on.bmp";
    public const string Offline = Party + "chattng-icon_off.bmp";

    public const string RecentButton = Party + "icon-c4.bmp";
    public const string RecentButtonIdle = Party + "icon-c4_.bmp";
    public const string RecentInfo = Party + "ButtonInfo-c4.bmp";
    public const string FriendsButton = Party + "icon-c2.bmp";
    public const string FriendsButtonIdle = Party + "icon-c2_.bmp";
    public const string FriendsInfo = Party + "ButtonInfo-c2.bmp";
    public const string IgnoredButton = Party + "icon-c6.bmp";
    public const string IgnoredButtonIdle = Party + "icon-c6_.bmp";
    public const string IgnoredInfo = Party + "ButtonInfo-c3.bmp";
    public const string Previous = Party + "LeftArrow.bmp";
    public const string PreviousIdle = Party + "LeftArrow_.bmp";
    public const string PreviousInfo = Party + "ButtonInfo-qc-pre.bmp";
    public const string Next = Party + "RightArrow.bmp";
    public const string NextIdle = Party + "RightArrow_.bmp";
    public const string NextInfo = Party + "ButtonInfo-qc-nxt.bmp";
    public const string MakeFriend = Party + "icon-8.bmp";
    public const string MakeFriendIdle = Party + "icon-8_.bmp";
    public const string MakeFriendInfo = Party + "ButtonInfo-c8.bmp";
    public const string Ignore = Party + "icon-7.bmp";
    public const string IgnoreIdle = Party + "icon-7_.bmp";
    public const string IgnoreInfo = Party + "ButtonInfo-c7.bmp";
    public const string Delete = Party + "icon-c3.bmp";
    public const string DeleteIdle = Party + "icon-c3_.bmp";
    public const string DeleteInfo = Party + "ButtonInfo-c6.bmp";
    public const string Close = Party + "ButtonExit.bmp";
    public const string CloseIdle = Party + "close_.bmp";
    public const string CloseInfo = Party + "ButtonInfo-5.bmp";

    public static readonly string[] All =
    [
        Title, Whispering, Online, Offline, RecentButton, RecentButtonIdle, RecentInfo, FriendsButton, FriendsButtonIdle, FriendsInfo, IgnoredButton,
        IgnoredButtonIdle, IgnoredInfo, Previous, PreviousIdle, PreviousInfo, Next, NextIdle, NextInfo, MakeFriend, MakeFriendIdle, MakeFriendInfo,
        Ignore, IgnoreIdle, IgnoreInfo, Delete, DeleteIdle, DeleteInfo, Close, CloseIdle, CloseInfo
    ];
}
