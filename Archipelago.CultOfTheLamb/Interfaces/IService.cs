namespace Archipelago.CultOfTheLamb;

interface IService
{
    /// <summary>
    /// Default interface for all game systems. Attaches the service for the duration of the session, and catches up on anything
    /// that happened while disconnected if implemented in the service.
    /// </summary>
    /// <remarks>
    /// Catch up may read the game's own save, but must not read anything Archipelago has granted.
    ///
    /// Services register in sequence on connect, and nothing has been applied by the time they run.
    /// ArchipelagoItemLogicController registers alongside the others, but its Register only drains
    /// the server's login replay into a queue. The grants happen later in ProcessQueue.
    /// </remarks>
    public void Register();

    public void Unregister();
}
