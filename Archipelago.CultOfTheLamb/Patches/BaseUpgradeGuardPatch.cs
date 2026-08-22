using System;
using System.Reflection;
using HarmonyLib;
using Lamb.UI;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Stops a Temple/Shrine upgrade from hard-locking the game when it runs at a moment the base
/// isn't live.
///
/// BiomeBaseManager.UpgradeBaseRoutine takes control before it validates anything:
///
///     ForceBlockMenus = true;                          // :915
///     foreach (pf in players) pf.SetActive(false);      // :918  the player disappears
///     shrine = BuildingShrine.Shrines[0].gameObject;    // :920  throws when the list is empty
///     ... released only at :990 and :1074
///
/// There is no try/finally, so an exception at :920 leaves menus blocked and the player
/// deactivated for the rest of the process - no character, no input, hard exit the only way out.
/// Observed three times in one session.
///
/// It happens because we call UpgradeSystem.UnlockAbility from ItemLogic.ProcessQueue on whatever
/// frame an item lands, and because the resulting unlock is flushed later, during an altar
/// interaction, when the base structures aren't enabled and BuildingShrine.Shrines is empty.
/// Vanilla can't reach it: a base upgrade is bought at a shrine, which is by definition present.
///
/// **Deferring rather than skipping is deliberate.** The routine isn't a cutscene - it removes the
/// old Shrine and Temple and places the next tier, so dropping the call would keep the unlock on
/// record while the buildings never change. That trades a visible lock for silent permanent loss,
/// which is worse. So the upgrade is held and replayed once the base is live.
///
/// This guards the symptom. The root cause - granting building upgrades outside the shrine flow at
/// all - is a dequeue gate in ProcessQueue, tracked separately.
/// </summary>
[HarmonyPatch]
internal static class BaseUpgradeGuardPatch
{
    /// <summary>
    /// The upgrade held back because the base wasn't ready, or null. Only ever one: the three
    /// Temple tiers are sequential, so a second can't legitimately arrive while one is pending.
    /// </summary>
    private static UpgradeSystem.Type? deferred;

    /// <summary>Stops the waiting message repeating every tick.</summary>
    private static bool loggedWaiting;

    private static readonly MethodInfo UpgradeBaseMethod =
        AccessTools.Method(typeof(BiomeBaseManager), "UpgradeBase");

    /// <summary>The three tiers that actually start the coroutine - BiomeBaseManager.cs:904-910.</summary>
    private static bool StartsTheRoutine(UpgradeSystem.Type type) =>
        type == UpgradeSystem.Type.Building_Temple2
        || type == UpgradeSystem.Type.Temple_III
        || type == UpgradeSystem.Type.Temple_IV;

    /// <summary>
    /// Whether the routine can complete. Shrines is the one the crash proved matters; the menu
    /// flag is included because the routine drives a camera sequence that has no business starting
    /// underneath an open menu.
    /// </summary>
    private static bool BaseIsReady()
    {
        if (BuildingShrine.Shrines == null || BuildingShrine.Shrines.Count == 0) return false;

        var ui = MonoSingleton<UIManager>.Instance;
        return ui == null || !ui.ForceBlockMenus;
    }

    [HarmonyPatch(typeof(BiomeBaseManager), "UpgradeBase")]
    [HarmonyPrefix]
    private static bool UpgradeBase_Prefix(UpgradeSystem.Type upgradeType)
    {
        // Anything else returns without starting a coroutine, so it can't lock and needn't wait.
        if (!StartsTheRoutine(upgradeType)) return true;
        if (BaseIsReady()) return true;

        deferred = upgradeType;
        loggedWaiting = false;
        Log.LogWarning($"[AP] Held back the {upgradeType} base upgrade: the base isn't live "
            + $"({BuildingShrine.Shrines?.Count ?? 0} shrine(s) active). Running it now would "
            + "deactivate your character and block menus with no way back. It will run as soon as "
            + "the base is ready.");
        return false;
    }

    /// <summary>
    /// Replays a held-back upgrade once the base is live. Driven from the plugin's once-a-second
    /// tick - the upgrade is a multi-second animation, so there's nothing to gain from checking
    /// more often.
    /// </summary>
    internal static void Tick()
    {
        if (deferred == null) return;

        if (!BaseIsReady())
        {
            if (!loggedWaiting)
            {
                loggedWaiting = true;
                Log.LogDebug($"[AP] Still waiting to run the {deferred} base upgrade.");
            }
            return;
        }

        var upgrade = deferred.Value;

        // Cleared before the call, not after: the routine re-enters UnlockAbility for the paired
        // Shrine tier, so leaving it set risks queueing the same upgrade twice.
        deferred = null;
        loggedWaiting = false;

        var manager = BiomeBaseManager.Instance;
        if (manager == null || UpgradeBaseMethod == null)
        {
            Log.LogError($"[AP] Could not replay the {upgrade} base upgrade - no BiomeBaseManager. "
                + "The unlock is recorded, so the buildings catch up on the next load.");
            return;
        }

        Log.LogInfo($"[AP] Base is ready - running the held-back {upgrade} upgrade now.");
        UpgradeBaseMethod.Invoke(manager, new object[] { upgrade });
    }

    /// <summary>
    /// Last resort: hand back what the routine took if it throws for any reason the prefix didn't
    /// anticipate.
    ///
    /// Patched manually rather than by attribute because the target is a compiler-generated
    /// iterator - if AccessTools can't resolve it on some future game build, a failed attribute
    /// patch would take the whole plugin down with it. Here a miss costs a warning.
    /// </summary>
    internal static void ApplyRoutineFinalizer(Harmony harmony)
    {
        try
        {
            var routine = AccessTools.Method(typeof(BiomeBaseManager), "UpgradeBaseRoutine");
            var moveNext = routine == null ? null : AccessTools.EnumeratorMoveNext(routine);

            if (moveNext == null)
            {
                Log.LogWarning("[AP] Couldn't find UpgradeBaseRoutine's state machine, so a base "
                    + "upgrade that throws will still lock the game. The guard above still covers "
                    + "the known cause.");
                return;
            }

            harmony.Patch(moveNext, finalizer: new HarmonyMethod(
                typeof(BaseUpgradeGuardPatch), nameof(RoutineFinalizer)));
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Couldn't guard UpgradeBaseRoutine against exceptions: {e.Message}");
        }
    }

    /// <summary>
    /// Runs when the routine's MoveNext throws. Restores the two things it takes up front and
    /// never gives back on a failure - menus and the player object - then swallows the exception,
    /// since it has already been reported and rethrowing only re-breaks the same coroutine.
    /// </summary>
    private static Exception RoutineFinalizer(Exception __exception)
    {
        if (__exception == null) return null;

        Log.LogError($"[AP] The base upgrade threw: {__exception.GetType().Name}: "
            + $"{__exception.Message}. Restoring control - the upgrade itself did not finish.");

        try
        {
            foreach (var player in PlayerFarming.players)
            {
                if (player?.gameObject != null) player.gameObject.SetActive(true);
            }

            var ui = MonoSingleton<UIManager>.Instance;
            if (ui != null) ui.ForceBlockMenus = false;
        }
        catch (Exception e)
        {
            Log.LogError($"[AP] Recovery itself failed: {e.Message}. A reload is the way out.");
        }

        return null;
    }
}
