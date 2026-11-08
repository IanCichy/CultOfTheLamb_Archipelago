using System;
using System.Reflection;
using HarmonyLib;
using Lamb.UI;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Stops a Temple or Shrine upgrade from locking up the game when it runs while the base is not
/// loaded.
///
/// UpgradeBaseRoutine blocks menus and deactivates every player (BiomeBaseManager.cs:915-918)
/// before it checks anything. If it then throws for want of a Shrine, nothing gives either back,
/// so the player is left with no character and no input and has to quit.
///
/// We cause it by granting the upgrade the moment the item arrives. The game holds it in
/// UnlocksToReveal and drains it at the next reveal, which can happen somewhere the base is not
/// loaded: RelicsUnlockRoom.cs:94 in a dungeon, or RatauHomeShop.cs:55. Vanilla is far less
/// exposed, since base upgrades are normally bought at the Shrine.
///
/// The upgrade is delayed rather than skipped, since skipping would record the unlock without
/// swapping in the new buildings. This guards the symptom only; not granting building upgrades
/// away from the Shrine is tracked separately.
/// </summary>
[HarmonyPatch]
internal static class BaseUpgradeGuardPatch
{
    // The upgrade held back because the base wasn't ready, or null. Nothing enforces one at a
    // time, since UnlockAbility does no prerequisite check, so a second would overwrite this.
    // The downgrade branch and RepairIfBehind cover that case.
    private static UpgradeSystem.Type? deferred;

    // Stops the waiting message repeating every tick
    private static bool loggedWaiting;

    // Stops RepairIfBehind re-firing a cutscene every tick if the rebuild won't take
    private static bool repairAttempted;

    private static readonly MethodInfo UpgradeBaseMethod =
        AccessTools.Method(typeof(BiomeBaseManager), "UpgradeBase");

    // The three tiers that start the coroutine. BiomeBaseManager.cs:904-910
    private static bool StartsTheRoutine(UpgradeSystem.Type type) =>
        TierOf(type) > 0;

    // Which base tier an upgrade builds, or 0 if it isn't one of the three
    private static int TierOf(UpgradeSystem.Type type) => type switch
    {
        UpgradeSystem.Type.Building_Temple2 => 2,
        UpgradeSystem.Type.Temple_III => 3,
        UpgradeSystem.Type.Temple_IV => 4,
        _ => 0,
    };

    // The tier the Temple structure is built at, or 0 if there isn't one
    private static int CurrentTier()
    {
        var temples = StructureManager.GetAllStructuresOfType<Structures_Temple>();
        if (temples == null || temples.Count == 0)
        {
            return 0;
        }

        return temples[0]?.Data?.Type switch
        {
            StructureBrain.TYPES.TEMPLE_IV => 4,
            StructureBrain.TYPES.TEMPLE_III => 3,
            StructureBrain.TYPES.TEMPLE_II => 2,
            _ => 1,
        };
    }

    // The highest base tier the save records as unlocked, or null if none of the three are.
    // Nullable rather than a sentinel, because UpgradeSystem.Type has no None member
    private static UpgradeSystem.Type? HighestOwnedTier()
    {
        if (UpgradeSystem.GetUnlocked(UpgradeSystem.Type.Temple_IV))
        {
            return UpgradeSystem.Type.Temple_IV;
        }

        if (UpgradeSystem.GetUnlocked(UpgradeSystem.Type.Temple_III))
        {
            return UpgradeSystem.Type.Temple_III;
        }

        if (UpgradeSystem.GetUnlocked(UpgradeSystem.Type.Building_Temple2))
        {
            return UpgradeSystem.Type.Building_Temple2;
        }

        return null;
    }

    // The routine swaps Shrine and Temple together and indexes both unguarded, at
    // BiomeBaseManager.cs:920 and :994. Checking only the Shrine is not enough: a new save has a
    // Shrine but no Temple, and the :994 throw is the worse one because the player is visible
    // again by then but cannot move. Menus are checked too, since the routine plays a camera
    // sequence that should not start under an open menu.
    private static bool BaseIsReady()
    {
        if (BuildingShrine.Shrines == null || BuildingShrine.Shrines.Count == 0)
        {
            return false;
        }

        var temples = StructureManager.GetAllStructuresOfType<Structures_Temple>();
        if (temples == null || temples.Count == 0)
        {
            return false;
        }

        var ui = MonoSingleton<UIManager>.Instance;
        return ui == null || !ui.ForceBlockMenus;
    }

    // Whether every player is walking around normally. Arriving back from a crusade the
    // teleporter holds the player in a locked CustomAnimation with a black silhouette
    // (Interaction_BaseTeleporter.cs:328-329), cleared by its warp-in-burst_end event at
    // :378-385. Starting the upgrade in that window swallows the event and leaves the player
    // black until the next scene change. The lock itself has a 2.5s fallback at :351-355; the
    // silhouette does not.
    private static bool PlayerIsFree()
    {
        if (PlayerFarming.players == null || PlayerFarming.players.Count == 0)
        {
            return false;
        }

        foreach (var player in PlayerFarming.players)
        {
            var state = player == null ? null : player.state;
            if (state == null || state.LockStateChanges)
            {
                return false;
            }

            if (state.CURRENT_STATE != StateMachine.State.Idle
                && state.CURRENT_STATE != StateMachine.State.Moving)
            {
                return false;
            }
        }

        return true;
    }

    [HarmonyPatch(typeof(BiomeBaseManager), "UpgradeBase")]
    [HarmonyPrefix]
    private static bool UpgradeBase_Prefix(UpgradeSystem.Type upgradeType)
    {
        // Anything else returns without starting a coroutine, so it can't lock and needn't wait.
        if (!StartsTheRoutine(upgradeType))
        {
            return true;
        }

        // The routine places whichever tier it was handed without checking what is standing, so
        // a tier at or below the current one silently downgrades the base. Vanilla buys tiers in
        // ascending order at the Shrine; ours arrive from the multiworld and sit in
        // UnlocksToReveal until the next altar visit, by which point a later tier may already be
        // applied. A queued Building_Temple2 landing on a tier IV base would rebuild it as II.
        var requested = TierOf(upgradeType);
        var current = CurrentTier();

        if (current > 0 && requested <= current)
        {
            // Refusing outright would leave the save stranded whenever the structures have already
            // fallen behind the record, so re-run the highest tier owned instead. That is
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

        if (BaseIsReady())
        {
            return true;
        }

        deferred = upgradeType;
        loggedWaiting = false;
        Log.LogWarning($"[AP] Held back the {upgradeType} base upgrade: the base isn't live "
            + $"({BuildingShrine.Shrines?.Count ?? 0} shrine(s) active). Running it now would "
            + "deactivate your character and block menus with no way back. It will run as soon as "
            + "the base is ready.");
        return false;
    }

    // Replays a held back upgrade once the base is live. The upgrade is a multi-second
    // animation, so the once-a-second tick is often enough
    internal static void Tick()
    {
        if (deferred == null)
        {
            RepairIfBehind();
            return;
        }

        // Player too, since this starts the cutscene on its own timing rather than from the game's
        // own flow, and can land in the middle of arriving back at the base
        if (!BaseIsReady() || !PlayerIsFree())
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

    // Rebuilds the base when the structures have fallen behind what the save records. The
    // prefix's repair branch needs something to call UpgradeBase, and after a downgrade nothing
    // is left in UnlocksToReveal to do so, which leaves the Temple and Shrine at tier II while
    // UnlockedUpgrades holds Temple_IV. Runs at most once per session, because a repair that does
    // not take would re-fire a multi-second cutscene every tick.
    private static void RepairIfBehind()
    {
        if (repairAttempted || !BaseIsReady())
        {
            return;
        }

        var highest = HighestOwnedTier();
        if (highest == null)
        {
            return;
        }

        var current = CurrentTier();
        if (current <= 0 || TierOf(highest.Value) <= current)
        {
            return;
        }

        repairAttempted = true;
        Log.LogWarning($"[AP] The base is built at tier {current} but the save records "
            + $"{highest} as unlocked. Rebuilding to match - this happens once per session.");
        deferred = highest;
        loggedWaiting = false;
    }

    // Hands back what the routine took if it throws for a reason the prefix didn't anticipate.
    // Patched by hand against the iterator's MoveNext. [HarmonyPatch] with MethodType.Enumerator
    // would also reach it, but would need its own nested class since this one already carries
    // method-level targets. A miss costs a warning.
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

    // Runs when the routine throws. Gives back the menus and the player it took at the start,
    // then swallows the exception, since it's already been reported and rethrowing just breaks
    // the same coroutine again
    private static Exception RoutineFinalizer(Exception __exception)
    {
        if (__exception == null)
        {
            return null;
        }

        Log.LogError($"[AP] The base upgrade threw: {__exception.GetType().Name}: "
            + $"{__exception.Message}. Restoring control - the upgrade itself did not finish.");

        try
        {
            var ui = MonoSingleton<UIManager>.Instance;
            if (ui != null)
            {
                ui.ForceBlockMenus = false;
            }

            foreach (var player in PlayerFarming.players)
            {
                if (player?.gameObject == null)
                {
                    continue;
                }

                player.gameObject.SetActive(true);

                // What strands you at the later throw site: :991 sets CustomAnimation
                // immediately before the Temple lookup at :994, so the player is reactivated but
                // unable to move. Restoring activation alone fixes nothing.
                if (player.state != null)
                {
                    player.state.CURRENT_STATE = StateMachine.State.Idle;
                }

                if (player.indicator != null)
                {
                    player.indicator.SetGameObjectActive(true);
                }
            }

            // :938 pins the camera to the shrine and :939-940 add conversation framing.
            // Neither is undone on the failure path, so the view would stay on the shrine even
            // once the player can move again.
            var camera = GameManager.GetInstance()?.CamFollowTarget;
            if (camera != null)
            {
                foreach (var shrine in BuildingShrine.Shrines)
                {
                    if (shrine != null)
                    {
                        camera.RemoveTarget(shrine.gameObject);
                    }
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
