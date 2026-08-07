using System;
using System.Collections.Generic;
using System.Linq;
using Archipelago.CultOfTheLamb.Patches;
using Archipelago.MultiClient.Net;
using Newtonsoft.Json.Linq;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Makes the weapon and curse families an Archipelago system: the game may only offer a family
/// the multiworld has granted, and equipping one for the first time sends a check.
///
/// Vanilla already treats these as progression, just invisibly - see EquipmentPoolPatch for the
/// ladder that hands out the Axe, then the Dagger, then the rest on a fixed schedule.
///
/// One instance handles weapons or curses, not both, since the two are independent YAML options
/// and either can be on alone. Nothing here touches save data.
/// </summary>
internal class EquipmentPoolService : IService
{
    /// <summary>
    /// The base value of each family, descending so the first match wins. Every variant sits
    /// contiguously above its own base (Axe is 100, Axe_Legendary 107), so a value's family is
    /// the largest base not above it. The game instead reads PrimaryEquipmentType off a
    /// ScriptableObject (DataManager.cs:2856), which needs assets loaded and can return null.
    /// </summary>
    private static readonly EquipmentType[] FamilyBases =
    {
        EquipmentType.Barrier,        // 1100
        EquipmentType.Teleport,       // 1000
        EquipmentType.MegaSlash,      // 900
        EquipmentType.Fireball,       // 800
        EquipmentType.ProjectileAOE,  // 700
        EquipmentType.EnemyBlast,     // 600
        EquipmentType.Tentacles,      // 500
        EquipmentType.Chain,          // 470
        // Shield (460) is omitted - Conviction's Guard was cut before release.
        EquipmentType.Blunderbuss,    // 450
        EquipmentType.Gauntlet,       // 400
        EquipmentType.Dagger,         // 300
        EquipmentType.Hammer,         // 200
        EquipmentType.Axe,            // 100
        EquipmentType.Sword,          // 0
    };

    /// <summary>
    /// Family -> its Legendary. Woolhaven content, normally earned through the Blacksmith's
    /// Broken Hammer questline. Shield_Legendary is omitted with the rest of Shield.
    /// </summary>
    private static readonly Dictionary<EquipmentType, EquipmentType> Legendaries = new()
    {
        { EquipmentType.Sword, EquipmentType.Sword_Legendary },
        { EquipmentType.Axe, EquipmentType.Axe_Legendary },
        { EquipmentType.Hammer, EquipmentType.Hammer_Legendary },
        { EquipmentType.Dagger, EquipmentType.Dagger_Legendary },
        { EquipmentType.Gauntlet, EquipmentType.Gauntlet_Legendary },
        { EquipmentType.Blunderbuss, EquipmentType.Blunderbuss_Legendary },
        { EquipmentType.Chain, EquipmentType.Chain_Legendary },
    };

    private readonly ArchipelagoSession session;

    /// <summary>Whether this instance is doing weapons or curses.</summary>
    private readonly bool weapons;

    /// <summary>
    /// Chance a weapon offer is upgraded to its family's Legendary. 0 disables it, and the
    /// server already resolves it to 0 without Woolhaven.
    /// </summary>
    private readonly float legendaryChance;

    /// <summary>
    /// False when the seed isn't randomizing this pool, which makes every family count as
    /// available - the Legendary option works on its own, without randomize_weapons.
    /// </summary>
    private readonly bool randomizing;

    /// <summary>AP item name -> family, for every family this seed manages.</summary>
    private readonly Dictionary<string, EquipmentType> itemNameToFamily;

    /// <summary>Family -> the check that first equipping it sends.</summary>
    private readonly Dictionary<EquipmentType, long> familyToCheckId;

    /// <summary>Every family this seed manages, for deciding what to substitute away.</summary>
    private readonly HashSet<EquipmentType> managed;

    /// <summary>Families the player may currently be offered.</summary>
    private readonly HashSet<EquipmentType> granted = new();

    /// <summary>
    /// Checks sent this session. CheckSender already filters what the server has from previous
    /// sessions; this only stops the resend every time the player re-equips in a run.
    /// </summary>
    private readonly HashSet<EquipmentType> sent = new();

    /// <summary>
    /// The fallback when a substitution has nothing to work with. Unreachable in practice
    /// (starting counts have range_start = 1), but "no valid answer" downstream is an
    /// IndexOutOfRangeException in GetRandomWeaponInPool rather than a graceful failure.
    /// </summary>
    private readonly EquipmentType fallback;

    internal EquipmentPoolService(
        ArchipelagoSession session,
        bool weapons,
        Dictionary<string, EquipmentType> itemNameToFamily,
        Dictionary<EquipmentType, long> familyToCheckId,
        HashSet<EquipmentType> startingFamilies,
        bool randomizing = true,
        float legendaryChance = 0f)
    {
        this.session = session;
        this.weapons = weapons;
        this.randomizing = randomizing;
        this.legendaryChance = legendaryChance;
        this.itemNameToFamily = itemNameToFamily ?? new Dictionary<string, EquipmentType>();
        this.familyToCheckId = familyToCheckId ?? new Dictionary<EquipmentType, long>();

        managed = new HashSet<EquipmentType>(this.itemNameToFamily.Values);
        fallback = weapons ? EquipmentType.Sword : EquipmentType.Fireball;

        if (startingFamilies != null)
        {
            foreach (var family in startingFamilies) granted.Add(family);
        }
    }

    private string Noun => weapons ? "weapon" : "curse";

    public void Register()
    {
        if (weapons)
        {
            EquipmentPoolPatch.SubstituteWeapon = Substitute;
            EquipmentPoolPatch.WeaponEquipped = Equipped;
        }
        else
        {
            EquipmentPoolPatch.SubstituteCurse = Substitute;
            EquipmentPoolPatch.CurseEquipped = Equipped;
        }

        Log.LogInfo($"[AP] {Noun} families active: {managed.Count} managed, "
            + $"{granted.Count} granted at start."
            + (legendaryChance > 0f ? $" Legendaries at {legendaryChance:P0}." : string.Empty));
    }

    public void Unregister()
    {
        // Reference writes, safe from the websocket thread. This is the whole teardown, because
        // nothing here ever wrote to save data.
        if (weapons)
        {
            EquipmentPoolPatch.SubstituteWeapon = null;
            EquipmentPoolPatch.WeaponEquipped = null;
        }
        else
        {
            EquipmentPoolPatch.SubstituteCurse = null;
            EquipmentPoolPatch.CurseEquipped = null;
        }
    }

    /// <summary>
    /// Grants a family if the item is one. Returns false so the caller can keep looking.
    /// Idempotent, because the granted set is rebuilt from the item history on every connect.
    /// </summary>
    internal bool TryApplyItem(string itemName)
    {
        if (itemName == null || !itemNameToFamily.TryGetValue(itemName, out var family))
        {
            return false;
        }

        if (granted.Add(family))
        {
            Log.LogInfo($"[AP] {Noun} '{itemName}' ({family}) unlocked.");
        }

        return true;
    }

    /// <summary>
    /// The family the game is allowed to hand over, given the one it chose. Runs inside the
    /// game's own selection, so it must never throw and must always answer.
    /// </summary>
    private EquipmentType Substitute(EquipmentType chosen)
    {
        var family = FamilyOf(chosen);

        // Before everything else, so it applies to picks that would otherwise pass straight
        // through. Only for a family the player can already use, so this improves the weapons
        // you have rather than handing you one you can't otherwise get.
        if (legendaryChance > 0f
            && (!randomizing || granted.Contains(family))
            && Legendaries.TryGetValue(family, out var legendary)
            && UnityEngine.Random.value < legendaryChance)
        {
            return legendary;
        }

        // Granted, so keep the pick exactly - including its variant. A Bane Axe rides along
        // with the Axe, since the affixes are the sermon system's to gate, not ours.
        if (granted.Contains(family)) return chosen;

        // Not ours at all: Sword_Ratau, the Teleport and Barrier curses, or any family when
        // this pool isn't being randomized. Left alone.
        if (!managed.Contains(family)) return chosen;

        // Prefer a granted family the player doesn't own yet. This preserves the game's own
        // unlock ceremony - the ladder was already about to hand over a new weapon here, so
        // redirecting it means the player gets the full first-pickup animation.
        var unowned = FirstGrantedNotInPool();
        if (unowned != EquipmentType.None) return unowned;

        // Ordinary mid-run variety. Pool entries carry the variants (a Bane Axe rides along
        // with the Axe), and the granted-but-uncollected families are added as their base type
        // because they are not in the pool to be drawn from - see GrantedNotInPool.
        var candidates = GrantedInPool();
        candidates.AddRange(GrantedNotInPool());
        if (candidates.Count > 0) return candidates[UnityEngine.Random.Range(0, candidates.Count)];

        return fallback;
    }

    /// <summary>A granted family the player's pool doesn't contain yet, or None.</summary>
    private EquipmentType FirstGrantedNotInPool()
    {
        var pool = Pool();
        if (pool == null) return EquipmentType.None;

        foreach (var family in granted)
        {
            if (!pool.Contains(family)) return family;
        }

        return EquipmentType.None;
    }

    /// <summary>Everything in the player's real pool whose family has been granted.</summary>
    private List<EquipmentType> GrantedInPool()
    {
        var pool = Pool();
        if (pool == null) return new List<EquipmentType>();

        return pool.Where(entry => granted.Contains(FamilyOf(entry))).ToList();
    }

    /// <summary>
    /// Granted families the player's pool doesn't contain, as their base type.
    ///
    /// In practice this is the Flail and nothing else: vanilla's ladder seeds every other family
    /// into WeaponPool within the first few runs, but it only ever offers the Chain inside
    /// Woolhaven (GetRandomWeaponInPool checks PlayerFarming.Location == Dungeon1_5), and a
    /// Bishops-goal seed never goes there.
    ///
    /// Without this, a granted Flail is reachable only through the FirstGrantedNotInPool branch
    /// above, which needs the game's own pick to be a family the player hasn't been granted.
    /// Once every family has been granted that branch is unreachable, and a Flail the player
    /// never happened to collect can never be offered again - stranding its check, and any
    /// progression the fill put there.
    /// </summary>
    private List<EquipmentType> GrantedNotInPool()
    {
        var pool = Pool();
        if (pool == null) return new List<EquipmentType>();

        return granted.Where(family => !pool.Contains(family)).ToList();
    }

    /// <summary>
    /// The game's real pool, read fresh every call - DataManager.Instance changes when a
    /// different save is loaded.
    /// </summary>
    private List<EquipmentType> Pool()
    {
        var data = DataManager.Instance;
        if (data == null) return null;
        return weapons ? data.WeaponPool : data.CursePool;
    }

    /// <summary>First equip of a family this session. Sends its check.</summary>
    private void Equipped(EquipmentType equipped)
    {
        var family = FamilyOf(equipped);

        // Check id first: a starting family has no location by design, and neither does None
        // (a cleared spell). Adding those to `sent` would be harmless for de-duplication but
        // makes DescribeState's "checks sent" count families that never sent anything, which is
        // exactly the noise that would hide a broken equip path during testing.
        if (!familyToCheckId.TryGetValue(family, out var checkId)) return;
        if (!sent.Add(family)) return;

        Log.LogInfo($"[AP] Equipped {equipped} ({family}) - sending check {checkId}.");
        CheckSender.Send(session, checkId);
    }

    /// <summary>The family a weapon or curse belongs to. See FamilyBases.</summary>
    internal static EquipmentType FamilyOf(EquipmentType type)
    {
        // None (9999) and Invalid (99999) sit above every band and belong to no family.
        if (type == EquipmentType.None || type == EquipmentType.Invalid
            || type == EquipmentType.TENTACLE_TAROT_REF)
        {
            return EquipmentType.None;
        }

        foreach (var family in FamilyBases)
        {
            if (type >= family) return family;
        }

        return EquipmentType.None;
    }

    /// <summary>
    /// AP item name -> family, from "weaponItems"/"curseItems". Names this build of the game
    /// doesn't recognise are dropped with a warning - losing one weapon beats losing the
    /// connection.
    /// </summary>
    internal static Dictionary<string, EquipmentType> ParseFamilies(
        IReadOnlyDictionary<string, object> slotData, string key)
    {
        var result = new Dictionary<string, EquipmentType>();

        if (!slotData.TryGetValue(key, out var raw) || raw is not JObject mapping) return result;

        foreach (var entry in mapping)
        {
            if (TryParse(entry.Value?.ToString(), entry.Key, out var family))
            {
                result[entry.Key] = family;
            }
        }

        return result;
    }

    /// <summary>Family -> location id, from "weaponLocations"/"curseLocations".</summary>
    internal static Dictionary<EquipmentType, long> ParseLocations(
        IReadOnlyDictionary<string, object> slotData, string key)
    {
        var result = new Dictionary<EquipmentType, long>();

        if (!slotData.TryGetValue(key, out var raw) || raw is not JObject mapping) return result;

        foreach (var entry in mapping)
        {
            if (!TryParse(entry.Key, entry.Key, out var family)) continue;

            // Skipped rather than thrown: this runs during connect, so an exception costs the
            // whole session instead of the one check we couldn't read.
            try
            {
                result[family] = entry.Value.ToObject<long>();
            }
            catch (Exception e)
            {
                Log.LogWarning("[AP] Slot data has a non-numeric location id for "
                    + $"'{entry.Key}': {e.Message} - skipping it.");
            }
        }

        return result;
    }

    /// <summary>Families granted at seed start, from "startingWeapons"/"startingCurses".</summary>
    internal static HashSet<EquipmentType> ParseStarting(
        IReadOnlyDictionary<string, object> slotData, string key)
    {
        var result = new HashSet<EquipmentType>();

        if (!slotData.TryGetValue(key, out var raw) || raw is not JArray names) return result;

        foreach (var name in names)
        {
            if (TryParse(name?.ToString(), name?.ToString(), out var family)) result.Add(family);
        }

        return result;
    }

    private static bool TryParse(string internalName, string context, out EquipmentType family)
    {
        family = default;
        if (string.IsNullOrEmpty(internalName)) return false;

        if (!Enum.IsDefined(typeof(EquipmentType), internalName))
        {
            Log.LogWarning("[AP] Slot data names an equipment type this game doesn't have: "
                + $"'{internalName}' - skipping '{context}'.");
            return false;
        }

        family = (EquipmentType)Enum.Parse(typeof(EquipmentType), internalName);
        return true;
    }

    /// <summary>What F9 prints for this pool. See DebugActions.</summary>
    internal string DescribeState()
    {
        var pool = Pool();
        var poolText = pool == null ? "<no save loaded>" : string.Join(", ", pool);

        return $"{Noun}s: {managed.Count} managed, {granted.Count} granted "
            + $"({string.Join(", ", granted)}), {sent.Count} check(s) sent this session, "
            + $"legendary chance {legendaryChance:P0}."
            + $"\n  Game's own pool ({pool?.Count ?? 0}, never written to): {poolText}";
    }
}
