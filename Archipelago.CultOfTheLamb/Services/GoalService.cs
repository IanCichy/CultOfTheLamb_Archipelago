using Archipelago.CultOfTheLamb.Patches;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Packets;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Watches for the seed's win condition and reports it to the server.
///
/// Progress is counted from the game's own save state rather than a session-local tally -
/// BossesCompleted for Bishops, KilledBosses for Witnesses. Both are already updated by the
/// time our handlers run (Interaction_MonsterHeart adds to BossesCompleted before raising
/// OnHeartTaken; our AddKilledBoss patch is a postfix), so reading them means the count is
/// also right after a reconnect, or if the player beat bosses before ever connecting.
/// See AI_INDEX.md §3 and §3a.
/// </summary>
internal class GoalService : IService
{
    // Matches worlds/cult_of_the_lamb/options.py Goal: option_bishops = 0, option_witnesses = 1.
    internal const int GoalBishops = 0;
    internal const int GoalWitnesses = 1;

    private readonly ArchipelagoSession session;
    private readonly int goal;
    private readonly int requiredCount;
    private bool goalSent;

    internal GoalService(ArchipelagoSession session, int goal, int requiredCount)
    {
        this.session = session;
        this.goal = goal;
        this.requiredCount = requiredCount;
    }

    public void Register()
    {
        InteractionMonsterHeartPatch.OnBossDefeated += HandleBossDefeated;
        DataManagerKilledBossPatch.OnBossKillRecorded += HandleBossKillRecorded;
        // Re-check immediately: the player may already satisfy the goal from a previous
        // session before this connect.
        CheckGoal();
    }

    public void Unregister()
    {
        InteractionMonsterHeartPatch.OnBossDefeated -= HandleBossDefeated;
        DataManagerKilledBossPatch.OnBossKillRecorded -= HandleBossKillRecorded;
        goalSent = false;
    }

    private void HandleBossDefeated(FollowerLocation location) => CheckGoal();

    private void HandleBossKillRecorded(string bossKey) => CheckGoal();

    private void CheckGoal()
    {
        if (goalSent) return;

        var isWitnessGoal = goal == GoalWitnesses;
        var defeated = isWitnessGoal ? CountDefeatedWitnesses() : CountDefeatedBishops();
        var noun = isWitnessGoal ? "Witnesses" : "Bishops";

        Log.LogInfo($"[AP] Goal progress: {defeated}/{requiredCount} {noun} defeated.");

        if (defeated >= requiredCount)
        {
            SendGoalCompleted();
        }
    }

    private int CountDefeatedBishops()
    {
        var dataManager = DataManager.Instance;
        if (dataManager == null || dataManager.BossesCompleted == null) return 0;

        var count = 0;
        foreach (var bishopLocation in RegionMapping.BishopLocationToCheckId.Keys)
        {
            if (dataManager.BossesCompleted.Contains(bishopLocation)) count++;
        }
        return count;
    }

    /// <summary>
    /// Counts the base-game Witnesses only, deliberately ignoring the "_P2" post-game
    /// re-fights - a Purged-run kill shouldn't count toward a goal the player hasn't met in
    /// the base run. Reads KilledBosses directly rather than DataManager's
    /// BeatenWitnessDungeon1..4 booleans, which are only refreshed at specific points.
    /// </summary>
    private int CountDefeatedWitnesses()
    {
        var dataManager = DataManager.Instance;
        if (dataManager == null || dataManager.KilledBosses == null) return 0;

        var count = 0;
        foreach (var witnessKey in BossKeyMapping.WitnessKeys)
        {
            if (dataManager.KilledBosses.Contains(witnessKey)) count++;
        }
        return count;
    }

    private void SendGoalCompleted()
    {
        goalSent = true;
        session.Socket.SendPacketAsync(new StatusUpdatePacket
        {
            Status = ArchipelagoClientState.ClientGoal,
        });
        Log.LogInfo("[AP] Goal complete - victory reported to the Archipelago server!");
    }
}
