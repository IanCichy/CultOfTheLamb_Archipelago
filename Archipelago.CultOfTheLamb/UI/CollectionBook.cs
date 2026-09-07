using System;
using System.Collections.Generic;
using src;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;

namespace Archipelago.CultOfTheLamb.UI;

/// <summary>
/// The relic-and-tarot lectern, copied out of the game and stood in the base.
/// </summary>
/// <remarks>
/// The game never spawns one of these. Every lectern is placed by hand in a room prefab and held
/// as a serialized reference (RelicRoomManager.relicBook), which is why it has no Addressables key
/// of its own. There is no spawn path to copy, so we copy a real one instead.
///
/// That is also the only way to get it wired correctly. Interaction carries around 25 fields,
/// including OutlineTarget, LockPosition and ActivateDistance. Building one by hand would mean
/// guessing all of them, then assembling the art and sizing a collider.
///
/// The prefab is kept whole, unlike the crusade podium. Interaction_RelicBook is the feature
/// rather than scenery, and it has none of the dungeon coupling that forces
/// EquipmentPedestal.Strip. It never reads BiomeGenerator.CurrentRoom, never destroys itself in
/// Start, and writes nothing to the save.
///
/// The tarot page shows Archipelago's granted cards for free. TarotVisibility already lends them
/// to UITarotCardsMenuController.OnShowStarted, which is the menu this opens.
/// </remarks>
internal static class CollectionBook
{
    /// <summary>The room we pull a lectern from when none is already loaded.</summary>
    /// <remarks>
    /// Verified in play, and the only key here on purpose. An unverified key reads as researched
    /// when it isn't, which is how "WeaponPodium" ended up throwing on every launch.
    /// </remarks>
    private const string SourceRoomKey = "Assets/_Rooms/Marketplace Relics.prefab";

    /// <summary>Our inactive copy, taken once and kept for the process.</summary>
    private static GameObject template;

    /// <summary>Set once the room load has run, so that blocking load happens at most once.</summary>
    private static bool roomTried;

    /// <summary>How many ticks between memory scans while we still have no template.</summary>
    private const int ScanEveryTicks = 5;

    private static int sinceLastScan;

    private static readonly List<GameObject> spawned = new();

    /// <summary>Inactive parent that the template and every clone are built under.</summary>
    /// <remarks>
    /// Instantiate wakes a clone immediately unless it lands inactive, and parenting into an
    /// inactive object is what makes it land that way. Each book is therefore named and positioned
    /// before anything on it runs, instead of waking at the origin and then teleporting.
    /// </remarks>
    private static GameObject staging;

    internal static int Count => spawned.Count;

    internal static GameObject Spawn(Vector3 position)
    {
        if (!EnsureTemplate()) return null;

        var book = UnityEngine.Object.Instantiate(template, staging.transform);

        book.name = "AP Collection Book";
        book.transform.SetParent(null, worldPositionStays: false);

        // Unparenting from a DontDestroyOnLoad object leaves the clone in the DontDestroyOnLoad
        // scene, where it would outlive the base and stay registered as a live Interaction while
        // the player is somewhere else. Moving it to the active scene lets the scene unload take
        // it, rather than leaning on the tick to notice.
        SceneManager.MoveGameObjectToScene(book, SceneManager.GetActiveScene());

        book.transform.position = position;
        book.SetActive(true);

        spawned.Add(book);
        return book;
    }

    internal static void Clear()
    {
        foreach (var book in spawned)
        {
            if (book != null) UnityEngine.Object.Destroy(book);
        }

        spawned.Clear();
    }

    /// <summary>
    /// Takes a copy of any lectern currently in memory. Cheap, and a no-op once we have one.
    /// </summary>
    /// <remarks>
    /// FindObjectsOfTypeAll, not FindObjectOfType. The latter sees only active objects in loaded
    /// scenes, and the base has no lectern in it. This one also sees inactive objects and prefabs
    /// already loaded in memory, so it finds a book by type rather than by knowing where one was
    /// parked. That is what keeps this from being a list of hardcoded room keys.
    /// </remarks>
    internal static bool TryAdoptFromScene()
    {
        if (template != null) return true;

        // Throttled, because the caller ticks at 1 Hz and this walks every loaded object. It
        // can't stop altogether, because once the room harvest has failed there is nothing else
        // to fall back on. Scanning every second forever for a lectern that may never load isn't
        // worth it.
        if (++sinceLastScan < ScanEveryTicks) return false;
        sinceLastScan = 0;

        foreach (var live in Resources.FindObjectsOfTypeAll<Interaction_RelicBook>())
        {
            if (live != null) return Adopt(live.gameObject, "one already in memory");
        }

        return false;
    }

    private static bool EnsureTemplate()
    {
        if (TryAdoptFromScene()) return true;
        if (roomTried) return false;

        roomTried = true;
        return HarvestFromSourceRoom();
    }

    /// <summary>
    /// Copies the lectern out of the source room for a player who has loaded nothing containing one.
    /// </summary>
    /// <remarks>
    /// Searches the loaded asset, never an instance of it. Addressables hands back the prefab's own
    /// hierarchy, so GetComponentInChildren walks it in memory and Instantiate clones just the
    /// lectern's subtree. The room is never built, so there is nothing to wake and nothing to tear
    /// down. Detaching it does lose any scale or rotation its parents applied. Position doesn't
    /// matter, because the caller sets that.
    ///
    /// The Addressables handle is deliberately never released, which pins the room and its
    /// dependencies for the process. Releasing it would drop the refcount to zero and let the
    /// bundle unload, taking the sprites and materials our clone still points at. The book would
    /// render as nothing. EquipmentPedestal keeps its podium handle for the same reason. The memory
    /// is what keeps the art valid.
    /// </remarks>
    private static bool HarvestFromSourceRoom()
    {
        try
        {
            var asset = Addressables.LoadAssetAsync<GameObject>(SourceRoomKey).WaitForCompletion();
            var book = asset == null
                ? null
                : asset.GetComponentInChildren<Interaction_RelicBook>(includeInactive: true);

            if (book != null) return Adopt(book.gameObject, SourceRoomKey);
        }
        catch (Exception e)
        {
            Log.LogInfo($"[AP] Couldn't read a lectern from '{SourceRoomKey}': {e.GetType().Name}");
        }

        Log.LogWarning("[AP] No collection book yet - none in memory, and none in "
            + $"'{SourceRoomKey}'. Visiting a hub or tarot vendor will provide one.");
        return false;
    }

    private static bool Adopt(GameObject book, string source)
    {
        if (staging == null)
        {
            staging = new GameObject("AP Collection Book Staging");
            staging.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(staging);
        }

        template = UnityEngine.Object.Instantiate(book, staging.transform);
        template.name = "AP Collection Book Template";
        template.SetActive(false);

        Log.LogInfo($"[AP] Adopted a collection book from {source}.");
        return true;
    }
}
