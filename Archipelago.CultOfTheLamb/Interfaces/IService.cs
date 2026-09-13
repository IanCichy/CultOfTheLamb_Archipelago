namespace Archipelago.CultOfTheLamb;

interface IService
{
    /// <summary>
    /// A game system that's active for the length of a session. Services can also catch up on
    /// anything that happened while disconnected.
    /// </summary>
    /// <remarks>
    /// Catch-up can read the game's save, but must not read anything Archipelago granted.
    ///
    /// Services register one after another on connect, before any items are applied.
    /// ArchipelagoItemLogicController only queues the server's item replay in Register. The items
    /// are actually granted later, in ProcessQueue.
    /// </remarks>
    public void Register();

    public void Unregister();
}
