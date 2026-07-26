using Archipelago.CultOfTheLamb.Patches;
using Archipelago.MultiClient.Net;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Sends AP location checks for every defeated boss. Two independent sources, because the
/// game tracks the two boss classes in two unrelated ways (see
/// DecompiledGamesViaDnSpy/Cotl/AI_INDEX.md §3 and §3a):
///
///  - Bishops: InteractionMonsterHeartPatch.OnBossDefeated, keyed by FollowerLocation.
///  - Minibosses + Witnesses: DataManagerKilledBossPatch.OnBossKillRecorded, keyed by the
///    game's internal boss-name string.
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
        DataManagerKilledBossPatch.OnBossKillRecorded += HandleBossKillRecorded;
    }

    public void Unregister()
    {
        InteractionMonsterHeartPatch.OnBossDefeated -= HandleBossDefeated;
        DataManagerKilledBossPatch.OnBossKillRecorded -= HandleBossKillRecorded;
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

    private void HandleBossKillRecorded(string bossKey)
    {
        if (BossKeyMapping.IsPostGameVariant(bossKey))
        {
            // Post-game "Purged" re-fights re-record the same boss with a _P2 suffix. They're
            // real, distinct encounters and would roughly double the location count, but
            // locations.py has no entries for them yet - so log and skip rather than
            // silently dropping them.
            Log.LogInfo($"[AP] Post-game variant \"{bossKey}\" has no AP location yet - skipping.");
            return;
        }

        if (!BossKeyMapping.BossKeyToCheckId.TryGetValue(bossKey, out var checkId))
        {
            // Woolhaven (Dungeon5/6) minibosses, Beholder 5/6, and the Warrior Trio all flow
            // through AddKilledBoss too but aren't in our location table.
            Log.LogInfo($"[AP] No AP location mapped for boss \"{bossKey}\" - skipping.");
            return;
        }

        Log.LogInfo($"[AP] Boss \"{bossKey}\" defeated, sending check {checkId}");
        session.Locations.CompleteLocationChecks(checkId);
    }
}
