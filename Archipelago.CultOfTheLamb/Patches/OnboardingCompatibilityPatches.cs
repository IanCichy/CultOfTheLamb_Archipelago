using HarmonyLib;
using System;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Patches;

/// Gets a save out of the ways the opening can stall and never recover. The tutorial assumes one fixed order which AP breaks
internal static class OnboardingCompatibilityPatches
{
    private const string BonesAndRitualGroup = "Objectives/GroupTitles/BonesAndRitual";
    private const string DiscipleGroup = "Objectives/GroupTitles/Disciple";
    private const string TempleGroup = "Objectives/GroupTitles/Temple";
    private const int Fragments = 3;
    private static bool fragmentsPlaced;
    private static bool loggedFailure;

    internal static void Tick()
    {
        var data = DataManager.Instance;
        if (!SaveSlot.IsLoaded || data == null || data.Objectives == null)
        {
            return;
        }

        try
        {
            EnableCursesIfOwed(data);
            ReenableTeleporterIfStranded(data);
            CreditTempleStepIfBuilt(data);
            CloseOutTutorialIfSkipped(data);
        }
        catch (Exception e)
        {
            if (!loggedFailure)
            {
                loggedFailure = true;
                Log.LogWarning($"[AP] An onboarding check failed: {e.Message}");
            }
        }
    }

    /// Turns casting on when the room that teaches it is out of reach
    private static void EnableCursesIfOwed(DataManager data)
    {
        if (data.EnabledSpells || !data.RatauToGiveCurseNextRun)
        {
            return;
        }

        data.EnabledSpells = true;
        Log.LogInfo("[AP] Curses were owed from a Darkwood room this seed has not opened, so "
            + "casting is now on.");
    }

    /// Turns the base teleporter back on when the tutorial ended without it.
    private static void ReenableTeleporterIfStranded(DataManager data)
    {
        if (data.UnlockBaseTeleporter || !data.ShowLoyaltyBars || !data.HasPerformedRitual)
        {
            return;
        }

        // Woolhaven seals the base until the furnace is lit (Onboarding.cs:973-975), which
        // assumes you can buy one. With AP that tech may never arrive,
        // so only lock while the furnace is actually buildable.
        if (data.WinterModeActive && !data.BuiltFurnace
            && UpgradeSystem.GetUnlocked(UpgradeSystem.Type.Building_Furnace))
        {
            return;
        }

        data.UnlockBaseTeleporter = true;

        // Woolhaven door unlock for furnace cases
        if ((data.WinterModeActive && !data.BuiltFurnace) && data.BaseGoopDoorLocked)
        {
            BaseGoopDoor.UnblockGoopDoor();
        }

        Log.LogInfo("[AP] Turned the base teleporter back on: the tutorial is over and it was "
            + "still off.");
    }

    /// Restarts day and night on a save whose tutorial ended with the clock still frozen.
    internal static void StartTheClockIfFrozen()
    {
        if (!TimeManager.PauseGameTime)
        {
            return;
        }

        TimeManager.PauseGameTime = false;
        Log.LogInfo($"[AP] The game clock was frozen, day and night are running again.");
    }

    /// Completes a Build Temple step that is asking for a Temple you already have.
    private static void CreditTempleStepIfBuilt(DataManager data)
    {
        if (data.OnboardingFinished || !TempleIsBuilt())
        {
            return;
        }

        var credited = 0;
        foreach (var objective in data.Objectives)
        {
            if (objective is Objectives_BuildStructure build
                && build.GroupId == TempleGroup
                && build.Count < build.Target)
            {
                build.Count = build.Target;
                credited++;
            }
        }

        if (credited == 0)
        {
            return;
        }

        ObjectiveManager.CheckObjectives(Objectives.TYPES.BUILD_STRUCTURE);
        BaseGoopDoor.UnblockGoopDoor();

        Log.LogInfo("[AP] Completed the onboarding Build Temple step, which asked for a Temple "
            + "that is already built, and reopened the base exit.");
    }

    /// Runs the tutorials closing step
    private static void CloseOutTutorialIfSkipped(DataManager data)
    {
        if (data.ShowLoyaltyBars
            || !data.HasPerformedRitual
            || GroupIsActive(data, BonesAndRitualGroup))
        {
            return;
        }

        data.UnlockBaseTeleporter = true;
        data.OnboardingFinished = true;
        data.ShowLoyaltyBars = true;
        data.GivenLoyaltyQuestDay = TimeManager.CurrentDay;

        // Restart the clock
        StartTheClockIfFrozen();

        Log.LogInfo("[AP] Closed out the tutorial");

        try
        {
            if (PlayerFarming.Location == FollowerLocation.Base)
            {
                data.DiscoverLocation(FollowerLocation.Hub1_RatauOutside);
            }

            if (!GroupIsActive(data, DiscipleGroup))
            {
                AddDiscipleQuest(Objectives.CustomQuestTypes.BlessAFollower);
                AddDiscipleQuest(Objectives.CustomQuestTypes.GiveGift);
                AddDiscipleQuest(Objectives.CustomQuestTypes.Disciple);
                AddDiscipleQuest(Objectives.CustomQuestTypes.LoyaltyCollectReward);
            }

            BaseGoopDoor.UnblockGoopDoor();
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] The tutorial is closed out, but its follow-up quests could not be handed over: {e.Message}");
        }
    }

    private static void AddDiscipleQuest(Objectives.CustomQuestTypes type) =>
        ObjectiveManager.Add(new Objectives_Custom(DiscipleGroup, type, -1, -1f), true, false);

    private static bool GroupIsActive(DataManager data, string groupId)
    {
        foreach (var objective in data.Objectives)
        {
            if (objective != null && objective.GroupId == groupId)
            {
                return true;
            }
        }

        return false;
    }

    // The step only ever asks for the first tier, so an upgraded Temple would never satisfy it.
    internal static bool TempleIsBuilt() =>
        StructureManager.AccumulateOnAllStructures(
            FollowerLocation.Base,
            s => s != null && s.Data != null && IsTemple(s.Data.Type) ? 1 : 0,
            0) > 0;

    // Teleporter and casting, which a player who has crusaded and built a Temple has in vanilla.
    // The Woolhaven furnace hold is respected like ReenableTeleporterIfStranded does.
    internal static void RepairPastFirstCrusade(DataManager data)
    {
        var furnaceHold = data.WinterModeActive && !data.BuiltFurnace
            && UpgradeSystem.GetUnlocked(UpgradeSystem.Type.Building_Furnace);
        if (!data.UnlockBaseTeleporter && !furnaceHold)
        {
            data.UnlockBaseTeleporter = true;
            Log.LogInfo("[AP] The base teleporter was still off from the tutorial, so it is on again.");
        }

        if (!data.EnabledSpells)
        {
            data.EnabledSpells = true;
            Log.LogInfo("[AP] Casting was still off from the tutorial, so curses are on again.");
        }
    }

    internal static bool TempleInSave(DataManager data)
    {
        if (data?.BaseStructures == null)
        {
            return false;
        }

        foreach (var structure in data.BaseStructures)
        {
            if (structure != null && IsTemple(structure.Type))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTemple(StructureBrain.TYPES type) =>
        type == StructureBrain.TYPES.TEMPLE
        || type == StructureBrain.TYPES.TEMPLE_II
        || type == StructureBrain.TYPES.TEMPLE_III
        || type == StructureBrain.TYPES.TEMPLE_IV;

    /// Puts the first Commandment Stone fragments in a boss chest when the Darkwood room that
    /// normally gives them is out of reach.
    internal static class BossChestFragments
    {
        internal static void Apply(Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(Interaction_Chest), "GiveBossReward");
                if (target == null)
                {
                    Log.LogWarning("[AP] Interaction_Chest.GiveBossReward not found, so a save "
                        + "that cannot reach Darkwood will not be given its first Commandment "
                        + "Stones.");
                    return;
                }

                harmony.Patch(target, prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(BossChestFragments), nameof(Prefix))));
            }
            catch (Exception e)
            {
                Log.LogWarning("[AP] Could not patch the boss chest for Commandment Stones: "
                    + $"{e.Message}");
            }
        }

        private static void Prefix(Interaction_Chest __instance)
        {
            try
            {
                if (!NeedsFragments())
                {
                    return;
                }

                fragmentsPlaced = true;

                for (var i = 0; i < Fragments; i++)
                {
                    SpawnFragment(__instance.transform.position);
                }

                Log.LogInfo("[AP] Still in the tutorial with no Commandment Stone fragments, so "
                    + $"the Darkwood room that normally gives them has not been reached. Put "
                    + $"{Fragments} in this boss chest instead.");
            }
            catch (Exception e)
            {
                Log.LogWarning("[AP] Could not hand over the Commandment Stone fragments: "
                    + $"{e.Message}");
            }
        }

        private static bool NeedsFragments()
        {
            var data = DataManager.Instance;

            return !fragmentsPlaced
                && data != null
                && TutorialState.InTutorial
                && data.ShowHaroDoctrineStoneRoom
                && !data.FirstDoctrineStone
                && data.DoctrineCurrentCount == 0
                && data.CompletedDoctrineStones == 0;
        }

        // What the boss chest does for its own fragment
        private static void SpawnFragment(Vector3 position)
        {
            var pickUp = InventoryItem.Spawn(
                InventoryItem.ITEM_TYPE.DOCTRINE_STONE, 1, position, 4f, null);

            if (pickUp == null)
            {
                return;
            }

            pickUp.SetInitialSpeedAndDiraction(5f, UnityEngine.Random.Range(200f, 300f));
            pickUp.GetComponent<Interaction_DoctrineStone>()?.MagnetToPlayer(null);
        }
    }
}

// Restarts the clock, and turns the teleporter and casting back on, for a save that has a
// Temple but never finished the tutorial. The game only does this after the first ritual
// (Onboarding.cs:168, :353), and an AP save can skip it when rituals arrive late.
[HarmonyPatch(typeof(Onboarding), "Start")]
internal static class OnboardingClockPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        try
        {
            if (ArchipelagoPlugin.Instance?.Client?.IsConnected != true && !AppliedItemStore.HasHistoryFor(SaveSlot.Current))
            {
                return;
            }

            var data = DataManager.Instance;
            if (data == null || !OnboardingCompatibilityPatches.TempleInSave(data))
            {
                return;
            }

            OnboardingCompatibilityPatches.StartTheClockIfFrozen();

            if (data.KilledBosses != null && data.KilledBosses.Count > 0)
            {
                OnboardingCompatibilityPatches.RepairPastFirstCrusade(data);
            }
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Couldn't check the save for a stalled tutorial: {e.Message}");
        }
    }
}
