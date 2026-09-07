namespace Archipelago.CultOfTheLamb.Console;

/// <summary>
/// Event surface for the connect, disconnect and reconnect actions, regardless of what triggers
/// them.
/// </summary>
/// <remarks>
/// A trigger can be the in-game UI button, a debug keybind, or a real dev console if COTL_API
/// exposes one. Separate from ArchipelagoClient so the trigger source can change without
/// touching connection logic.
/// </remarks>
public static class ArchipelagoConsoleCommand
{
    public delegate void ArchipelagoCommandCalled(string url, int port, string slot, string password);
    public static event ArchipelagoCommandCalled OnArchipelagoCommandCalled;

    public delegate void ArchipelagoDisconnectCommandCalled();
    public static event ArchipelagoDisconnectCommandCalled OnArchipelagoDisconnectCommandCalled;

    public delegate void ArchipelagoReconnectCommandCalled();
    public static event ArchipelagoReconnectCommandCalled OnArchipelagoReconnectCommandCalled;

    public static void Connect(string url, int port, string slot, string password)
    {
        OnArchipelagoCommandCalled?.Invoke(url, port, slot, password);
    }

    public static void Disconnect()
    {
        OnArchipelagoDisconnectCommandCalled?.Invoke();
    }

    public static void Reconnect()
    {
        OnArchipelagoReconnectCommandCalled?.Invoke();
    }
}
