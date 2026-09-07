using System.Collections.Generic;
using Archipelago.CultOfTheLamb.Patches;
using Archipelago.MultiClient.Net;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Sends AP location checks for every defeated boss.
/// </summary>
/// <remarks>
/// There are two independent sources, because the game tracks the two boss classes in two
/// unrelated ways. See DcplIdx 3 and 3a.
///
///  1. Bishops arrive on InteractionMonsterHeartPatch.OnBossDefeated, keyed by FollowerLocation.
///  2. Minibosses and Witnesses arrive on DataManagerKilledBossPatch.OnBossKillRecorded, keyed by
///     the game's internal boss-name string.
///
/// Both events fire only at the moment of the kill, so Register also re-derives from the save's
/// own kill records (SendChecksForRecordedKills), which is how a boss killed while disconnected
/// still pays out on the next connect.
/// </remarks>
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

    // Pays for every boss the save already records as dead. The events above fire only at the
    // moment of the kill, so without this a boss killed while disconnected is lost for good.
    //
    // GoalService reads the same two save fields to decide whether the goal is met, so skipping
    // this lets the server record a satisfied goal for checks that were never sent.
    private void SendChecksForRecordedKills()
    {
        var data = DataManager.Instance;
        if (data == null)
        {
            return;
        }

        var pending = new List<long>();

        // Bishops, keyed by FollowerLocation. That is the same type the mapping uses.
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
                if (bossKey == null || BossKeyMapping.IsPostGameVariant(bossKey))
                {
                    continue;
                }

                if (BossKeyMapping.BossKeyToCheckId.TryGetValue(bossKey, out var checkId))
                {
                    pending.Add(checkId);
                }
            }
        }

        if (pending.Count == 0)
        {
            return;
        }

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
            // Not one of the 4 base Bishops. That covers Woolhaven's Wolf and Yngya, and any
            // location we don't have a check for yet. Nothing to send.
            return;
        }

        Log.LogInfo($"[AP] Bishop defeated at {location}, sending check {checkId}");
        CheckSender.Send(session, checkId);
    }

    private void HandleBossKillRecorded(string bossKey)
    {
        if (BossKeyMapping.IsPostGameVariant(bossKey))
        {
            // Post game Purged fights re-record the same boss with a _P2 suffix. locations.py
            // has no entries for them yet, so log and skip.
            Log.LogInfo($"[AP] Post-game variant \"{bossKey}\" has no AP location yet - skipping.");
            return;
        }

        if (!BossKeyMapping.BossKeyToCheckId.TryGetValue(bossKey, out var checkId))
        {
            // Woolhaven minibosses in Dungeon5 and Dungeon6, Beholder 5 and 6, and the Warrior
            // Trio all flow through AddKilledBoss too, but none of them is in our location table.
            Log.LogInfo($"[AP] No AP location mapped for boss \"{bossKey}\" - skipping.");
            return;
        }

        Log.LogInfo($"[AP] Boss \"{bossKey}\" defeated, sending check {checkId}");
        CheckSender.Send(session, checkId);
    }
}
