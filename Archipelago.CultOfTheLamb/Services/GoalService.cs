using Archipelago.CultOfTheLamb.Patches;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Packets;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Watches for the seed's win condition and reports it to the server.
///
/// Progress is counted from DataManager.Instance.BossesCompleted rather than from a
/// session-local tally: Interaction_MonsterHeart adds the kill to BossesCompleted *before*
/// raising OnHeartTaken (see AI_INDEX.md §3), so the game's own record is already correct
/// when our handler runs. Reading it means the count is also right after a reconnect, or if
/// the player beat Bishops before ever connecting.
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
    private bool warnedUnsupportedGoal;

    internal GoalService(ArchipelagoSession session, int goal, int requiredCount)
    {
        this.session = session;
        this.goal = goal;
        this.requiredCount = requiredCount;
    }

    public void Register()
    {
        InteractionMonsterHeartPatch.OnBossDefeated += HandleBossDefeated;
        // Re-check immediately: the player may already satisfy the goal from a previous
        // session before this connect.
        CheckGoal();
    }

    public void Unregister()
    {
        InteractionMonsterHeartPatch.OnBossDefeated -= HandleBossDefeated;
        goalSent = false;
        warnedUnsupportedGoal = false;
    }

    private void HandleBossDefeated(FollowerLocation location) => CheckGoal();

    private void CheckGoal()
    {
        if (goalSent) return;

        if (goal != GoalBishops)
        {
            // The Witness fights aren't detectable yet - FollowerLocation only identifies
            // the region, not which encounter within it (same limitation that blocks
            // miniboss/Witness location checks in LocationCheckService).
            if (!warnedUnsupportedGoal)
            {
                warnedUnsupportedGoal = true;
                Log.LogWarning(
                    "[AP] Goal is set to 'witnesses', which this client can't detect yet - "
                    + "victory will NOT be reported automatically. Use the Bishops goal for now.");
            }
            return;
        }

        var defeated = CountDefeatedBishops();
        Log.LogInfo($"[AP] Goal progress: {defeated}/{requiredCount} Bishops defeated.");

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
