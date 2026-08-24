using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Archipelago.CultOfTheLamb.UI;

/// <summary>
/// A plinth in the base showing one weapon or curse family, lit when Archipelago has granted it.
/// </summary>
/// <remarks>
/// Weapons and curses are the only randomized system with no native screen anywhere in the game
/// (see EquipmentDisplayService), so the display has to be built rather than unhidden.
///
/// The art is the game's own crusade podium, loaded through Addressables. It isn't in Resources -
/// only 525 GameObjects are, and none is a plinth - but it is one of ~33,600 addressable keys.
///
/// Purely visual: no StructureBrain, no StructureManager entry, nothing written to save data.
/// </remarks>
internal static class EquipmentPedestal
{
    /// <summary>
    /// The crusade weapon podium. Two keys point at it; the short one is tried first because a
    /// path key is the more likely of the two to be renamed by a game update.
    /// </summary>
    private static readonly string[] PodiumKeys =
    {
        "WeaponPodium",
        "Assets/Prefabs/Weapon Selection Podium.prefab",
    };

    /// <summary>The game's own "you don't have this" treatment - see Interaction_SelectWeapon.</summary>
    private static readonly Color NotReceivedTint = new(0f, 0f, 0f, 0.75f);

    /// <summary>
    /// The loaded podium. Its Addressables handle is deliberately not retained or released: one
    /// prefab held by a static for the process is cheaper to keep than to reload per base entry.
    /// </summary>
    private static GameObject prefab;

    /// <summary>Set once both keys have failed, so the load is attempted exactly once.</summary>
    private static bool prefabUnavailable;

    private static readonly List<GameObject> spawned = new();

    /// <summary>Which podium is showing which family, so a later grant can find and light it.</summary>
    private static readonly Dictionary<EquipmentType, GameObject> byFamily = new();

    /// <summary>
    /// Parent for freshly cloned podiums, kept inactive so their Awake never runs.
    ///
    /// This matters: the podium carries Interaction_WeaponSelectionPodium, which reaches into
    /// BiomeGenerator.Instance.CurrentRoom and destroys itself off the dungeon floor number. In
    /// the base that would either throw or delete the display. Instantiating under an inactive
    /// parent lets us strip those components before anything on them can run.
    /// </summary>
    private static GameObject nursery;

    internal static int Count => spawned.Count;

    /// <summary>
    /// Loads the podium prefab. Blocking rather than a coroutine because this runs from a debug
    /// key and from a scene-load poll, neither of which can hand back a yield.
    /// </summary>
    private static bool EnsurePrefab()
    {
        if (prefab != null) return true;

        // Failure is cached as well as success. Spawn calls this once per pedestal, and Tick
        // rebuilds the row on every base entry - so without this a renamed key would mean a dozen
        // blocking loads and two dozen warnings, every time, for the whole session.
        if (prefabUnavailable) return false;

        foreach (var key in PodiumKeys)
        {
            try
            {
                var handle = Addressables.LoadAssetAsync<GameObject>(key);
                var loaded = handle.WaitForCompletion();

                if (loaded != null)
                {
                    prefab = loaded;
                    Log.LogInfo($"[AP] Loaded podium art from '{key}'.");
                    return true;
                }
            }
            catch (System.Exception e)
            {
                Log.LogWarning($"[AP] Addressables key '{key}' failed: {e.GetType().Name}");
            }
        }

        prefabUnavailable = true;
        Log.LogWarning("[AP] No podium art available - tried " + string.Join(", ", PodiumKeys));
        return false;
    }

    internal static GameObject Spawn(EquipmentType family, Vector3 position, bool received)
    {
        if (!EnsurePrefab()) return null;

        if (nursery == null)
        {
            nursery = new GameObject("AP Pedestal Nursery");
            nursery.SetActive(false);
            Object.DontDestroyOnLoad(nursery);
        }

        // Into the inactive nursery first, so nothing on the prefab wakes up before it's safe.
        var plinth = Object.Instantiate(prefab, nursery.transform);
        ApplyLitState(plinth, received);
        Strip(plinth);

        plinth.name = $"AP Pedestal - {family}";
        plinth.transform.SetParent(null, worldPositionStays: false);
        plinth.transform.position = position;
        plinth.SetActive(true);

        spawned.Add(plinth);
        byFamily[family] = plinth;

        ConfigureIcon(plinth, family, received);

#if AP_DEBUG_KEYS
        // Scaffolding from the placement pass. Fifty-odd lines per base entry is far too much for
        // a normal build; DebugActions.DumpPodiumsInScene is the on-demand version.
        Describe(plinth, family);
#endif

        return plinth;
    }

    /// <summary>
    /// Re-lights an already-placed podium, for a family that arrives mid-session. Cheaper and less
    /// jarring than tearing the row down and rebuilding it.
    /// </summary>
    internal static void SetReceived(EquipmentType family, bool received)
    {
        if (!byFamily.TryGetValue(family, out var plinth) || plinth == null) return;

        ApplyLitState(plinth, received);
        ConfigureIcon(plinth, family, received);
    }

    /// <summary>
    /// Uses the podium's own lit and unlit art for received vs not.
    /// </summary>
    /// <remarks>
    /// The prefab ships both states as separate child groups, so a granted family gets the lit
    /// stone and an ungranted one the dead stone. Must run before Strip, since these references
    /// live on the component it destroys.
    ///
    /// Lighting deliberately stays off: it's a large radial glow sized for a dark dungeon room, and
    /// in the base it washes several metres of ground so the pedestals read as blobs rather than
    /// objects.
    /// </remarks>
    private static void ApplyLitState(GameObject plinth, bool received)
    {
        // By child name rather than through Interaction_WeaponSelectionPodium's fields: Strip
        // destroys that component, so this has to keep working after the podium is already placed
        // and a family arrives mid-session.
        foreach (var child in plinth.GetComponentsInChildren<Transform>(includeInactive: true))
        {
            switch (child.name)
            {
                case "PodiumOn":
                    child.gameObject.SetActive(received);
                    break;

                case "PodiumOff":
                    child.gameObject.SetActive(!received);
                    break;

                // A large radial glow sized for a dark dungeon room. In the daylit base it washes
                // several metres of ground and a row of them merges into one blob.
                case "Lighting":
                    child.gameObject.SetActive(false);
                    break;
            }
        }
    }

    /// <summary>
    /// Removes everything that would make the clone behave like a real podium: it must not offer a
    /// weapon, react to the player, or delete itself.
    /// </summary>
    private static void Strip(GameObject plinth)
    {
        foreach (var interaction in plinth.GetComponentsInChildren<Interaction>(includeInactive: true))
        {
            if (interaction != null) Object.Destroy(interaction);
        }

        foreach (var collider in plinth.GetComponentsInChildren<Collider2D>(includeInactive: true))
        {
            if (collider != null) Object.Destroy(collider);
        }
    }

    /// <summary>
    /// Points the podium's own floating icon at this family, and sets the lock to match.
    ///
    /// Driving the prefab's children rather than adding a sprite of our own: the podium already
    /// has an InventoryItemIcon (authored as Icon_Weapon_Sword) drawn above the plinth, so an
    /// extra renderer just hides behind it - which is why every pedestal read as a sword.
    /// </summary>
    private static void ConfigureIcon(GameObject plinth, EquipmentType family, bool received)
    {
        Sprite sprite = null;
        try
        {
            var data = EquipmentManager.GetEquipmentData(family);

            // WorldSprite is what the game itself puts here - Interaction_WeaponSelectionPodium's
            // GetIcon() returns exactly this. The prefab ships a UI-style placeholder
            // (Icon_Weapon_Sword) that gets overwritten at runtime, so matching the placeholder
            // rather than the runtime value gives art that doesn't match the weapon shop.
            sprite = data?.WorldSprite ?? data?.UISprite;
        }
        catch (System.Exception e)
        {
            Log.LogWarning($"[AP] No icon for '{family}': {e.GetType().Name}");
        }

        foreach (var renderer in plinth.GetComponentsInChildren<SpriteRenderer>(includeInactive: true))
        {
            switch (renderer.gameObject.name)
            {
                case "InventoryItemIcon":
                    if (sprite != null) renderer.sprite = sprite;
                    renderer.color = received ? Color.white : NotReceivedTint;
                    break;

                case "Lock Icon":
                    renderer.gameObject.SetActive(!received);
                    break;

                // The shop's "better/worse than what you're holding" arrow. There's nothing to
                // compare against on a display stand.
                case "WeaponCompare":
                    renderer.gameObject.SetActive(false);
                    break;
            }
        }
    }

#if AP_DEBUG_KEYS
    /// <summary>
    /// Size, sorting and child layout - none of it derivable from the decompile, and all of it
    /// needed to place these. Kept for the next time the podium prefab changes shape.
    /// </summary>
    private static void Describe(GameObject plinth, EquipmentType family)
    {
        var renderers = plinth.GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
        Log.LogInfo($"[AP] Pedestal '{family}' at {plinth.transform.position} - "
            + $"{renderers.Length} renderer(s).");

        foreach (var renderer in renderers)
        {
            Log.LogInfo($"[AP]   {renderer.gameObject.name}: active {renderer.gameObject.activeInHierarchy}, "
                + $"bounds {renderer.bounds.size}, layer '{SortingLayer.IDToName(renderer.sortingLayerID)}', "
                + $"order {renderer.sortingOrder}, sprite '{renderer.sprite?.name}'");
        }
    }
#endif

    /// <summary>Removes every pedestal. Nothing persists, so this is the whole teardown.</summary>
    internal static void Clear()
    {
        foreach (var plinth in spawned)
        {
            if (plinth != null) Object.Destroy(plinth);
        }

        spawned.Clear();
        byFamily.Clear();
    }
}
