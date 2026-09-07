using System.Diagnostics;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Temporary instrumentation for the "weapon or curse arrives dealing zero damage" report.
/// </summary>
/// <remarks>
/// Delete this once the cause is known. Every static hypothesis is already ruled out:
/// `GetWeaponDamageMultiplier = 0.13 + 0.07 * level` on a base of 1 can't reach zero,
/// `GetWeaponAttackRateMultiplier` has no callers, the durability table has no zero entry
/// (75/50/85/60/100), every family base is a real plain weapon, and `FamilyOf` handles
/// `Sword = 0` as the last entry of its descending chain.
///
/// Two questions any fix depends on. Is the bad equipment ours: `RecordOffer` logs the type the
/// game chose against the one we returned, so a matching pair means the substitution was a
/// no-op. Is the level at fault: `RecordEquip` logs the level handed to SetWeapon/SetSpell
/// against the run counter. `FoundItemPickUp.WeaponLevel` defaults to -1 and is assigned only by
/// Interaction_WeaponChoice, the legendary podium, or SetStartingWeapon.
///
/// [Conditional] rather than #if AP_DEBUG_KEYS, because it strips the six call sites in
/// EquipmentPoolPatch without each of them needing its own guard.
/// </remarks>
internal static class EquipmentDiagnostics
{
    /// <summary>
    /// What the game picked versus what we handed back. Logged on every offer, because the useful
    /// comparison is between the bad pickup and the good ones around it.
    /// </summary>
    /// <param name="noun">"weapon" or "curse", so the two families read apart in the log.</param>
    /// <param name="chosen">The type the game's own selection returned.</param>
    /// <param name="substituted">The type we handed back in its place.</param>
    [Conditional("AP_DEBUG_KEYS")]
    internal static void RecordOffer(string noun, EquipmentType chosen, EquipmentType substituted)
    {
        var changed = chosen != substituted;
        Log.LogInfo($"[AP] DIAG {noun} offer: game chose {chosen}, "
            + $"we returned {substituted}{(changed ? " (SUBSTITUTED)" : " (unchanged)")}.");
    }

    /// <summary>
    /// What was actually equipped, and at what level. The run counters are read here rather than
    /// passed in because the question is whether they agree with the level the caller supplied:
    /// a mismatch is the symptom, so both sides have to be in the same line.
    /// </summary>
    /// <param name="noun">"weapon" or "curse", so the two families read apart in the log.</param>
    /// <param name="type">The equipment the game just put in the slot.</param>
    /// <param name="level">The level the caller handed to SetWeapon or SetSpell.</param>
    [Conditional("AP_DEBUG_KEYS")]
    internal static void RecordEquip(string noun, EquipmentType type, int level)
    {
        var data = DataManager.Instance;
        var runWeapon = data == null ? "n/a" : data.CurrentRunWeaponLevel.ToString();
        var runCurse = data == null ? "n/a" : data.CurrentRunCurseLevel.ToString();

        var line = $"[AP] DIAG {noun} equipped: {type} at level {level} "
            + $"(run counters: weapon {runWeapon}, curse {runCurse}).";

        // None at level 0 is the game clearing the slot. It fires several times a run and is not
        // a fault. Warning on it buried the signal in the first test, so only a *real* type at a
        // bad level is worth shouting about.
        var isRealType = type != EquipmentType.None && type != EquipmentType.Invalid;

        if (isRealType && level <= 0)
        {
            Log.LogWarning(line + "  <-- LEVEL IS ZERO OR NEGATIVE");
        }
        else
        {
            Log.LogInfo(line);
        }
    }
}
