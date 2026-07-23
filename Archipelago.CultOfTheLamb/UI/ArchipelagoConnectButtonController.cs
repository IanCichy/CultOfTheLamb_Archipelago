namespace Archipelago.CultOfTheLamb.UI;

/// <summary>
/// TODO: RoR2's equivalent adds a "Connect to Archipelago" button to the character select
/// screen. Cult of the Lamb's menu flow is different (no character select) - this needs a
/// real hook point in the game's main menu or pause menu once one is identified. Until then
/// connecting only works via config values set before launch (see ArchipelagoPlugin).
/// </summary>
public static class ArchipelagoConnectButtonController
{
    public delegate void ConnectClick();
    public static event ConnectClick OnConnectClick;

    public static System.Action<string> OnSlotChanged;
    public static System.Action<string> OnPasswordChanged;
    public static System.Action<string> OnUrlChanged;
    public static System.Func<string, string> OnPortChanged;

    public static void RaiseConnectClick() => OnConnectClick?.Invoke();

    public static void ChangeButtonWhenConnected()
    {
        // TODO: update real UI once it exists.
    }

    public static void ChangeButtonWhenDisconnected()
    {
        // TODO: update real UI once it exists.
    }
}
