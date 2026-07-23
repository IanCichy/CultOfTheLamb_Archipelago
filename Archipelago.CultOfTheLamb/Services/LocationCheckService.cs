using Archipelago.MultiClient.Net;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Watches game events and reports Archipelago location checks when the player does the
/// in-game equivalent (converts a follower, defeats a bishop, completes a doctrine, etc.).
/// TODO: wire up the actual game hooks (Harmony patches or COTL_API events) once the
/// relevant Assembly-CSharp types are identified. Location ids should come from
/// worlds/cult_of_the_lamb/locations.py so the two sides can't drift apart.
/// </summary>
internal class LocationCheckService : IService
{
    private readonly ArchipelagoSession session;

    public LocationCheckService(ArchipelagoSession session)
    {
        this.session = session;
    }

    public void Register()
    {
        // TODO: subscribe to game events here.
    }

    public void Unregister()
    {
        // TODO: unsubscribe from game events here.
    }

    /// <summary>Reports a single location check by its AP location id.</summary>
    public void SendLocationCheck(long locationId)
    {
        session.Locations.CompleteLocationChecks(locationId);
    }
}
