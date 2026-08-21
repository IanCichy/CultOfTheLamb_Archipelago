namespace Archipelago.CultOfTheLamb;

interface IService
{
    /// <summary>
    /// Hooks the game up to this system for the duration of a session, and catches up on anything
    /// that happened while there wasn't one.
    ///
    /// **Register runs before a single received item has been applied.** ArchipelagoItemLogicController
    /// registers alongside everything else, and its own Register only *drains* the server's replayed
    /// backlog into a queue - the grants happen frames later in ProcessQueue. So catch-up here may
    /// read the game's own save state, which is complete and is what makes stateless re-derivation
    /// work, but it must never read multiworld-granted state: at this moment nothing has been
    /// granted yet, and the answer would be silently wrong rather than obviously empty.
    ///
    /// Anything that genuinely needs granted state has to run after the queue drains, not here.
    /// </summary>
    public void Register();

    public void Unregister();
}
