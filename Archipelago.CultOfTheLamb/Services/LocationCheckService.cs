using System.Collections.Generic;
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
///
/// Both events only fire at the moment of the kill, so Register also re-derives from the save's
/// own kill records - see SendChecksForRecordedKills. That's what makes a boss killed while
/// disconnected still pay out on the next connect.
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

        SendChecksForRecordedKills();
    }

    /// <summary>
    /// Pays for every boss the save already records as dead.
    ///
    /// Without this a boss killed while disconnected is lost for good: the events above only fire
    /// at the moment of the kill, and these are the goal-critical checks. The game writes both
    /// kill records to save data, so the answer is sitting there at connect - GoalService already
    /// reads the same two fields to decide whether the goal is met, which meant the server could
    /// record a satisfied goal for checks that were never sent.
    ///
    /// Re-sending is free: CheckSender filters against AllLocationsChecked, which the Connected
    /// packet has already populated by the time any service registers.
    /// </summary>
    private void SendChecksForRecordedKills()
    {
        var data = DataManager.Instance;
        if (data == null) return;

        var pending = new List<long>();

        // Bishops, keyed by FollowerLocation - the same type the mapping uses.
        if (data.BossesCompleted != null)
        {
            foreach (var location in data.BossesCompleted)
            {
                if (RegionMapping.BishopLocationToCheckId.TryGetValue(location, out var checkId))
                {
                    pending.Add(checkId);
                }
            }
        }

        // Minibosses and Witnesses, keyed by the game's internal boss-name string.
        if (data.KilledBosses != null)
        {
            foreach (var bossKey in data.KilledBosses)
            {
                if (bossKey == null || BossKeyMapping.IsPostGameVariant(bossKey)) continue;

                if (BossKeyMapping.BossKeyToCheckId.TryGetValue(bossKey, out var checkId))
                {
                    pending.Add(checkId);
                }
            }
        }

        if (pending.Count == 0) return;

        Log.LogInfo($"[AP] {pending.Count} boss kill(s) already recorded in the save - "
            + "sending any whose check hasn't landed yet.");
        CheckSender.Send(session, pending);
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
        CheckSender.Send(session, checkId);
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
        CheckSender.Send(session, checkId);
    }
}
