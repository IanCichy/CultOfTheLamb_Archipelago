#if AP_DEBUG_KEYS
using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Debug only. Opens the crusade and ritual doors without their Follower requirement.
/// </summary>
/// <remarks>
/// The real fix is a planned remove_follower_gates option. This is just so Narinder can be
/// reached without a full playthrough. Off until Ctrl+F2 turns it on, and then on until restart.
/// </remarks>
internal static class FollowerGateBypassPatch
{
    internal static bool Enabled;
}

/// <summary>The crusade region doors.</summary>
[HarmonyPatch(typeof(Interaction_BaseDungeonDoor), nameof(Interaction_BaseDungeonDoor.GetFollowerCount))]
internal static class DungeonDoorFollowerGatePatch
{
    private static void Postfix(ref bool __result)
    {
        if (FollowerGateBypassPatch.Enabled)
        {
            __result = true;
        }
    }
}

/// <summary>
/// The Narinder ritual pool, which is a different class with its own 20-Follower requirement.
/// </summary>
/// <remarks>
/// Zeroes TargetFollowerCount as well as setting EnoughFollowers, because GetLabel recomputes the
/// flag from the field and would otherwise still render "1 / 20" in red. Both are private.
/// </remarks>
[HarmonyPatch(typeof(Interaction_DeathCatRitual))]
internal static class DeathCatRitualFollowerGatePatch
{
    [HarmonyPatch("GetLabel")]
    [HarmonyPrefix]
    private static void BeforeLabel(Interaction_DeathCatRitual __instance) => Zero(__instance);

    [HarmonyPatch("OnInteract")]
    [HarmonyPrefix]
    private static void BeforeInteract(Interaction_DeathCatRitual __instance) => Zero(__instance);

    private static void Zero(Interaction_DeathCatRitual instance)
    {
        if (!FollowerGateBypassPatch.Enabled || instance == null)
        {
            return;
        }

        Traverse.Create(instance).Field("TargetFollowerCount").SetValue(0);
        Traverse.Create(instance).Field("EnoughFollowers").SetValue(true);
    }
}
#endif
