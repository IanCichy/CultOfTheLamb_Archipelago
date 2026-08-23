namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Temporary instrumentation for the "weapon or curse arrives dealing zero damage" report.
///
/// **Delete this once the cause is known.** It exists because the bug has resisted every static
/// hypothesis: the level curve cannot reach zero damage
/// (`GetWeaponDamageMultiplier = 0.13 + 0.07 * level`, added to a base of 1),
/// `GetWeaponAttackRateMultiplier` has no callers at all, the durability table has no zero entry
/// (75/50/85/60/100), every family base is a real plain weapon, and `FamilyOf` handles `Sword = 0`
/// correctly as the last entry of its descending chain. Guessing further from source is not paying
/// off, so this records what actually happens instead.
///
/// Two questions it answers, and they need separating before any fix:
///
/// 1. **Is it us?** `RecordOffer` logs the type the game chose *and* the type we replaced it with.
///    A line where those match means the substitution was a no-op and the bad equipment came from
///    somewhere else - vanilla, or CheatMenu, which is installed on the test save and has already
///    been caught doing bulk unlocks.
/// 2. **Is it the level?** `RecordEquip` logs the level handed to SetWeapon/SetSpell alongside the
///    run counter it should have come from. `FoundItemPickUp.WeaponLevel` defaults to -1 and is
///    only ever assigned by Interaction_WeaponChoice, the legendary podium, or SetStartingWeapon -
///    so a pickup reaching SetWeapon by any other route passes -1.
/// </summary>
internal static class EquipmentDiagnostics
{
    /// <summary>
    /// What the game picked versus what we handed back. Logged on every offer, because the useful
    /// comparison is between the bad pickup and the good ones around it.
    /// </summary>
    internal static void RecordOffer(string noun, EquipmentType chosen, EquipmentType substituted)
    {
        var changed = chosen != substituted;
        Log.LogInfo($"[AP] DIAG {noun} offer: game chose {chosen}, "
            + $"we returned {substituted}{(changed ? " (SUBSTITUTED)" : " (unchanged)")}.");
    }

    /// <summary>
    /// What was actually equipped, and at what level. The run counters are read here rather than
    /// passed in because the question is whether they agree with the level the caller supplied -
    /// a mismatch is the symptom, so both sides have to be in the same line.
    /// </summary>
    internal static void RecordEquip(string noun, EquipmentType type, int level)
    {
        var data = DataManager.Instance;
        var runWeapon = data == null ? "n/a" : data.CurrentRunWeaponLevel.ToString();
        var runCurse = data == null ? "n/a" : data.CurrentRunCurseLevel.ToString();

        var line = $"[AP] DIAG {noun} equipped: {type} at level {level} "
            + $"(run counters: weapon {runWeapon}, curse {runCurse}).";

        // None at level 0 is the game clearing the slot - it fires several times a run and is not
        // a fault. Warning on it buried the signal in the first test, so only a *real* type at a
        // bad level is worth shouting about.
        var isRealType = type != EquipmentType.None && type != EquipmentType.Invalid;

        if (isRealType && level <= 0) Log.LogWarning(line + "  <-- LEVEL IS ZERO OR NEGATIVE");
        else Log.LogInfo(line);
    }
}
