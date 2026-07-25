using Archipelago.CultOfTheLamb.Patches;
using Archipelago.MultiClient.Net;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Sends AP location checks in response to InteractionMonsterHeartPatch's OnBossDefeated
/// event (see that file, and DecompiledGamesViaDnSpy/Cotl/AI_INDEX.md §3). Only Bishop
/// kills are wired up for now - the 3 minibosses and the Witness fight per region aren't
/// disambiguated from FollowerLocation alone (it only identifies the region, not which
/// specific encounter within it), so those location entries in locations.py don't have a
/// send-check hook yet. TODO once a per-encounter identifier is found.
/// </summary>
internal class LocationCheckService : IService
{
    private readonly ArchipelagoSession session;

    internal LocationCheckService(ArchipelagoSession session)
    {
        this.session = session;
    }

    public void Register()
    {
        InteractionMonsterHeartPatch.OnBossDefeated += HandleBossDefeated;
    }

    public void Unregister()
    {
        InteractionMonsterHeartPatch.OnBossDefeated -= HandleBossDefeated;
    }

    private void HandleBossDefeated(FollowerLocation location)
    {
        if (!RegionMapping.BishopLocationToCheckId.TryGetValue(location, out var checkId))
        {
            // Not one of the 4 base Bishops (e.g. Woolhaven's Wolf/Yngya, or a location we
            // don't have a check for yet) - nothing to send.
            return;
        }

        Log.LogInfo($"[AP] Bishop defeated at {location}, sending check {checkId}");
        session.Locations.CompleteLocationChecks(checkId);
    }
}
