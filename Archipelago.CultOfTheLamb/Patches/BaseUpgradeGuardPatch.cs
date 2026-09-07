using System;
using System.Reflection;
using HarmonyLib;
using Lamb.UI;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Stops a Temple/Shrine upgrade from hard-locking the game when it runs at a moment the base
/// isn't live.
/// </summary>
/// <remarks>
/// BiomeBaseManager.UpgradeBaseRoutine takes control before it validates anything.
///
///     ForceBlockMenus = true;                          // :915
///     foreach (pf in players) pf.SetActive(false);      // :918  the player disappears
///     shrine = BuildingShrine.Shrines[0].gameObject;    // :920  throws when the list is empty
///     ... released only at :990 and :1074
///
/// There is no try/finally. An exception at :920 therefore leaves menus blocked and the player
/// deactivated for the rest of the process. No character, no input, and a hard exit as the only
/// way out. Observed three times in one session.
///
/// It happens because we call UpgradeSystem.UnlockAbility from ItemLogic.ProcessQueue on whatever
/// frame an item lands. The resulting unlock is flushed later, during an altar interaction, when
/// the base structures aren't enabled and BuildingShrine.Shrines is empty. Vanilla can't reach it,
/// because a base upgrade is bought at a shrine, which is by definition present.
///
/// Deferring rather than skipping is deliberate. The routine isn't a cutscene. It removes the
/// old Shrine and Temple and places the next tier. Dropping the call would keep the unlock on
/// record while the buildings never changed, trading a visible lock for silent permanent loss.
/// The upgrade is held and replayed once the base is live.
///
/// This guards the symptom only. The root cause is granting building upgrades outside the shrine
/// flow at all, which wants a dequeue gate in ProcessQueue and is tracked separately.
/// </remarks>
[HarmonyPatch]
internal static class BaseUpgradeGuardPatch
{
    /// <summary>
    /// The upgrade held back because the base wasn't ready, or null. Only ever one, because the
    /// three Temple tiers are sequential, so a second can't legitimately arrive while one is
    /// pending.
    /// </summary>
    private static UpgradeSystem.Type? deferred;

    /// <summary>Stops the waiting message repeating every tick.</summary>
    private static bool loggedWaiting;

    /// <summary>Stops RepairIfBehind re-firing a cutscene every tick if the rebuild won't take.</summary>
    private static bool repairAttempted;

    private static readonly MethodInfo UpgradeBaseMethod =
        AccessTools.Method(typeof(BiomeBaseManager), "UpgradeBase");

    /// <summary>The three tiers that actually start the coroutine. BiomeBaseManager.cs:904-910.</summary>
    private static bool StartsTheRoutine(UpgradeSystem.Type type) =>
        TierOf(type) > 0;

    /// <summary>Which base tier an upgrade builds, or 0 if it isn't one of the three.</summary>
    private static int TierOf(UpgradeSystem.Type type) => type switch
    {
        UpgradeSystem.Type.Building_Temple2 => 2,
        UpgradeSystem.Type.Temple_III => 3,
        UpgradeSystem.Type.Temple_IV => 4,
        _ => 0,
    };

    /// <summary>The tier the Temple structure is actually built at, or 0 if there isn't one.</summary>
    private static int CurrentTier()
    {
        var temples = StructureManager.GetAllStructuresOfType<Structures_Temple>();
        if (temples == null || temples.Count == 0) return 0;

        return temples[0]?.Data?.Type switch
        {
            StructureBrain.TYPES.TEMPLE_IV => 4,
            StructureBrain.TYPES.TEMPLE_III => 3,
            StructureBrain.TYPES.TEMPLE_II => 2,
            _ => 1,
        };
    }

    /// <summary>
    /// The highest base tier the save records as unlocked, or null if none of the three are.
    /// Nullable rather than a sentinel, because UpgradeSystem.Type has no None member.
    /// </summary>
    private static UpgradeSystem.Type? HighestOwnedTier()
    {
        if (UpgradeSystem.GetUnlocked(UpgradeSystem.Type.Temple_IV)) return UpgradeSystem.Type.Temple_IV;
        if (UpgradeSystem.GetUnlocked(UpgradeSystem.Type.Temple_III)) return UpgradeSystem.Type.Temple_III;
        if (UpgradeSystem.GetUnlocked(UpgradeSystem.Type.Building_Temple2)) return UpgradeSystem.Type.Building_Temple2;
        return null;
    }

    /// <summary>
    /// Whether the routine can complete.
    /// </summary>
    /// <remarks>
    /// **Both** unguarded index-0 accesses have to be satisfied, because the routine swaps the
    /// Shrine and the Temple as a pair.
    ///
    ///     BuildingShrine.Shrines[0]                              // :920
    ///     StructureManager.GetAllStructuresOfType&lt;Structures_Temple&gt;()[0]  // :994
    ///
    /// An earlier version of this guard checked only the first, which let a fresh cult through,
    /// since a new save has a Shrine but no Temple, and it threw at :994 instead. That is the
    /// worse throw site. It lands *after* :990-991 have reactivated the player and put it into
    /// CustomAnimation, so the player exists but cannot move.
    ///
    /// The menu flag is included because the routine drives a camera sequence that has no
    /// business starting underneath an open menu.
    /// </remarks>
    private static bool BaseIsReady()
    {
        if (BuildingShrine.Shrines == null || BuildingShrine.Shrines.Count == 0) return false;

        var temples = StructureManager.GetAllStructuresOfType<Structures_Temple>();
        if (temples == null || temples.Count == 0) return false;

        var ui = MonoSingleton<UIManager>.Instance;
        return ui == null || !ui.ForceBlockMenus;
    }

    [HarmonyPatch(typeof(BiomeBaseManager), "UpgradeBase")]
    [HarmonyPrefix]
    private static bool UpgradeBase_Prefix(UpgradeSystem.Type upgradeType)
    {
        // Anything else returns without starting a coroutine, so it can't lock and needn't wait.
        if (!StartsTheRoutine(upgradeType)) return true;

        // The routine doesn't check what you already have. It removes the current Shrine and
        // Temple and places whichever tier it was handed. Run it with a tier at or below the one
        // standing and it silently *downgrades* the base. Vanilla never does, because tiers are
        // bought in ascending order at the shrine. We can, because they arrive from the multiworld
        // and the game defers each reveal into UnlocksToReveal until the next altar visit, by
        // which point a later tier may already have been applied.
        //
        // Observed once. A queued Building_Temple2 flushed onto a tier-IV base and rebuilt it
        // as II.
        var requested = TierOf(upgradeType);
        var current = CurrentTier();

        if (current > 0 && requested <= current)
        {
            // Refusing outright would leave the save stranded whenever the structures have already
            // fallen behind the record, so re-run the highest tier actually owned instead. That is
            // a no-op when the base is already correct, and a repair when it isn't.
            var highest = HighestOwnedTier();

            if (highest != null && TierOf(highest.Value) > current && BaseIsReady())
            {
                Log.LogWarning($"[AP] {upgradeType} would rebuild the base at tier {requested}, "
                    + $"below the tier {current} already standing. Running {highest} instead.");
                deferred = highest;
                loggedWaiting = false;
                return false;
            }

            Log.LogInfo($"[AP] Skipped {upgradeType}: the base is already at tier {current}. "
                + "Running it would downgrade the Temple and Shrine.");
            return false;
        }

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
    /// tick, since the upgrade is a multi-second animation, so there's nothing to gain from
    /// checking more often.
    /// </summary>
    internal static void Tick()
    {
        if (deferred == null)
        {
            RepairIfBehind();
            return;
        }

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

        // Cleared before the call, not after, because the routine re-enters UnlockAbility for the
        // paired Shrine tier, so leaving it set risks queueing the same upgrade twice.
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
    /// Rebuilds the base when the structures have fallen behind what the save says is unlocked.
    /// </summary>
    /// <remarks>
    /// The prefix's own repair branch only fires when something calls UpgradeBase, and after a
    /// downgrade there is nothing left in UnlocksToReveal to call it, so that path is correct and
    /// unreachable, leaving the base stranded. This drives it instead.
    ///
    /// The state it exists for was observed directly. Temple and Shrine standing at tier II while
    /// UnlockedUpgrades holds Temple_IV, after a queued Building_Temple2 flushed onto an
    /// already-upgraded base.
    ///
    /// Runs at most once per session. A repair that doesn't take would otherwise re-fire a
    /// multi-second cutscene every tick, which is worse than the drift it fixes.
    /// </remarks>
    private static void RepairIfBehind()
    {
        if (repairAttempted || !BaseIsReady()) return;

        var highest = HighestOwnedTier();
        if (highest == null) return;

        var current = CurrentTier();
        if (current <= 0 || TierOf(highest.Value) <= current) return;

        repairAttempted = true;
        Log.LogWarning($"[AP] The base is built at tier {current} but the save records "
            + $"{highest} as unlocked. Rebuilding to match - this happens once per session.");
        deferred = highest;
        loggedWaiting = false;
    }

    /// <summary>
    /// Last resort. Hands back what the routine took if it throws for any reason the prefix
    /// didn't anticipate.
    ///
    /// Patched manually rather than by attribute because the target is a compiler-generated
    /// iterator. If AccessTools can't resolve it on some future game build, a failed attribute
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
    /// never gives back on a failure, menus and the player object, then swallows the exception,
    /// since it has already been reported and rethrowing only re-breaks the same coroutine.
    /// </summary>
    private static Exception RoutineFinalizer(Exception __exception)
    {
        if (__exception == null) return null;

        Log.LogError($"[AP] The base upgrade threw: {__exception.GetType().Name}: "
            + $"{__exception.Message}. Restoring control - the upgrade itself did not finish.");

        try
        {
            var ui = MonoSingleton<UIManager>.Instance;
            if (ui != null) ui.ForceBlockMenus = false;

            foreach (var player in PlayerFarming.players)
            {
                if (player?.gameObject == null) continue;

                player.gameObject.SetActive(true);

                // The one that actually strands you at the later throw site. :991 puts the player
                // into CustomAnimation immediately before the Temple lookup at :994, so by then
                // it is visible and reactivated but unable to move. Restoring activation alone,
                // which is all the first version of this did, fixes nothing.
                if (player.state != null) player.state.CURRENT_STATE = StateMachine.State.Idle;

                if (player.indicator != null) player.indicator.SetGameObjectActive(true);
            }

            // :938 pins the camera to the shrine and :939-940 put the game into conversation
            // framing. Neither is undone on the failure path, so without this the view stays on
            // the shrine even once the player can move again.
            var camera = GameManager.GetInstance()?.CamFollowTarget;
            if (camera != null)
            {
                foreach (var shrine in BuildingShrine.Shrines)
                {
                    if (shrine != null) camera.RemoveTarget(shrine.gameObject);
                }
            }
        }
        catch (Exception e)
        {
            Log.LogError($"[AP] Recovery itself failed: {e.Message}. A reload is the way out.");
        }

        return null;
    }
}
