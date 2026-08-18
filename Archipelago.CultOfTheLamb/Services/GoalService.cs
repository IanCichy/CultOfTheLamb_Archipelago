using Archipelago.CultOfTheLamb.Patches;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Packets;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Watches for the seed's win condition and reports it to the server.
///
/// Counting lives in GoalProgress, shared with the objective guide's win-condition line. Both
/// tracks are read from the game's own save state rather than a session-local tally, and both
/// are already written by the time our handlers run (Interaction_MonsterHeart adds to
/// BossesCompleted before raising OnHeartTaken; our AddKilledBoss patch is a postfix) - so the
/// count is right after a reconnect, or if the player beat bosses before ever connecting.
/// </summary>
internal class GoalService : IService
{
    // Matches worlds/cult_of_the_lamb/options.py Goal.
    internal const int GoalBishops = 0;
    internal const int GoalWitnesses = 1;
    internal const int GoalNarinder = 2;

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
        EnemyDeathCatBossPatch.OnNarinderDefeated += HandleNarinderDefeated;
        // Re-check immediately: the player may already satisfy the goal from a previous
        // session before this connect.
        CheckGoal();
    }

    public void Unregister()
    {
        InteractionMonsterHeartPatch.OnBossDefeated -= HandleBossDefeated;
        DataManagerKilledBossPatch.OnBossKillRecorded -= HandleBossKillRecorded;
        EnemyDeathCatBossPatch.OnNarinderDefeated -= HandleNarinderDefeated;
    }

    private void HandleBossDefeated(FollowerLocation location) => CheckGoal();

    private void HandleBossKillRecorded(string bossKey) => CheckGoal();

    private void HandleNarinderDefeated() => CheckGoal();

    /// <summary>Re-runs the goal check. For debug keys that write save state directly, which
    /// raises none of the events above.</summary>
    internal void Recheck() => CheckGoal();

    /// <summary>This seed's goal, so a debug key can refuse to run on the wrong one.</summary>
    internal int Goal => goal;

    private void CheckGoal()
    {
        if (goalSent) return;

        var defeated = GoalProgress.CountForGoal(goal);

        // Narinder is a single encounter, so he has his own target and the seed's Required
        // Count doesn't apply.
        var fixedTarget = GoalProgress.FixedTargetForGoal(goal);
        var target = fixedTarget > 0 ? fixedTarget : requiredCount;

        Log.LogInfo(goal == GoalNarinder
            ? $"[AP] Goal progress: Narinder {(defeated > 0 ? "defeated" : "not yet defeated")}."
            : $"[AP] Goal progress: {defeated}/{target} "
                + $"{(goal == GoalWitnesses ? "Witnesses" : "Bishops")} defeated.");

        if (defeated >= target)
        {
            SendGoalCompleted();
        }
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
