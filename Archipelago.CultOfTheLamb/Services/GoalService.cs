using Archipelago.CultOfTheLamb.Patches;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Packets;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Watches for the seed's win condition and reports it to the server
/// </summary>
/// <remarks>
/// The counting lives in GoalProgress, shared with the quest log's win condition line. It reads
/// the game's save, which is already updated by the time our handlers run, so the count is right
/// after a reconnect or if you beat bosses before ever connecting.
/// </remarks>
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
        // Re-check immediately. The player may already satisfy the goal from a previous
        // session before this connect.
        CheckGoal();
    }

    public void Unregister()
    {
        InteractionMonsterHeartPatch.OnBossDefeated -= HandleBossDefeated;
        DataManagerKilledBossPatch.OnBossKillRecorded -= HandleBossKillRecorded;
        EnemyDeathCatBossPatch.OnNarinderDefeated -= HandleNarinderDefeated;
    }

    private void HandleBossDefeated(FollowerLocation location)
    {
        CheckGoal();
    }

    private void HandleBossKillRecorded(string bossKey)
    {
        CheckGoal();
    }

    // Sends on the event itself rather than reading DeathCatBeaten. That flag is set before our
    // postfix runs, yet in testing it still read false here, and the cause isn't known.
    private void HandleNarinderDefeated()
    {
        if (goal == GoalNarinder)
        {
            Log.LogInfo("[AP] Goal progress: Narinder defeated.");
            SendGoalCompleted();
            return;
        }

        CheckGoal();
    }

    // Once a second from ArchipelagoPlugin. A backstop for any goal whose event was missed, and
    // quiet, since the progress line would otherwise fill the log.
    internal void Tick()
    {
        CheckGoal(log: false);
    }

    // For debug keys that write save state directly, which raises none of the events above
    internal void Recheck()
    {
        CheckGoal();
    }

    // This seed's goal, so a debug key can refuse to run on the wrong one
    internal int Goal => goal;

    private void CheckGoal(bool log = true)
    {
        if (goalSent)
        {
            return;
        }

        var defeated = GoalProgress.CountForGoal(goal);

        // Narinder is a single encounter, so he has his own target and the seed's Required
        // Count doesn't apply.
        var fixedTarget = GoalProgress.FixedTargetForGoal(goal);
        var target = fixedTarget > 0 ? fixedTarget : requiredCount;

        if (log)
        {
            Log.LogInfo(goal == GoalNarinder
                ? $"[AP] Goal progress: Narinder {(defeated > 0 ? "defeated" : "not yet defeated")}."
                : $"[AP] Goal progress: {defeated}/{target} "
                    + $"{(goal == GoalWitnesses ? "Witnesses" : "Bishops")} defeated.");
        }

        if (defeated >= target)
        {
            SendGoalCompleted();
        }
    }

    private void SendGoalCompleted()
    {
        if (goalSent)
        {
            return;
        }

        goalSent = true;
        session.Socket.SendPacketAsync(new StatusUpdatePacket
        {
            Status = ArchipelagoClientState.ClientGoal,
        });
        Log.LogInfo("[AP] Goal complete - victory reported to the Archipelago server!");
    }
}
