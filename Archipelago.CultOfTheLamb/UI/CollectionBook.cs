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
/// **The game never spawns one of these, so there is no spawn path to copy.** Every lectern is
/// placed by hand in a room prefab and held as a serialized reference
/// (RelicRoomManager.relicBook), which is also why it has no Addressables key of its own. Copying
/// a real one is therefore the only way to get it correctly wired: Interaction carries ~25 fields
/// - OutlineTarget, LockPosition, ActivateDistance - and building one by hand would mean guessing
/// every one of them, plus assembling the art and sizing a collider.
///
/// Kept whole, unlike the crusade podium: Interaction_RelicBook is the feature rather than
/// scenery, and it carries none of the dungeon coupling that forces EquipmentPedestal.Strip - no
/// BiomeGenerator.CurrentRoom read, no self-destruct in Start, and it writes nothing to the save.
///
/// The tarot page shows Archipelago's granted cards with no extra work here, because
/// TarotVisibility already lends them to UITarotCardsMenuController.OnShowStarted - the menu this
/// opens.
/// </remarks>
internal static class CollectionBook
{
    /// <summary>
    /// The room we pull a lectern from when none is loaded. **Verified**; the only key here on
    /// purpose, since an unverified one reads as researched and isn't.
    /// </summary>
    private const string SourceRoomKey = "Assets/_Rooms/Marketplace Relics.prefab";

    /// <summary>Our inactive copy, taken once and kept for the process.</summary>
    private static GameObject template;

    /// <summary>Set once the room load has run, so that blocking load happens at most once.</summary>
    private static bool roomTried;

    /// <summary>How many ticks between memory scans while we still have no template.</summary>
    private const int ScanEveryTicks = 5;

    private static int sinceLastScan;

    private static readonly List<GameObject> spawned = new();

    /// <summary>
    /// Inactive parent the template and every clone are built under.
    /// </summary>
    /// <remarks>
    /// Instantiate wakes a clone immediately unless it lands inactive, and parenting into an
    /// inactive object is what makes it land that way - so each book is named and positioned
    /// before anything on it runs, rather than waking at the origin and teleporting.
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
    /// FindObjectsOfTypeAll rather than FindObjectOfType, because that one sees only *active*
    /// objects in loaded scenes and the base has no lectern in it. This also sees inactive ones and
    /// prefabs already loaded in memory, so it finds a book by type rather than by knowing where
    /// one was parked - which is what keeps this from being a list of hardcoded rooms.
    /// </remarks>
    internal static bool TryAdoptFromScene()
    {
        if (template != null) return true;

        // Throttled because the caller is a 1 Hz tick and this walks every loaded object. Once the
        // room harvest has failed there is nothing else to fall back on, so this can't just stop -
        // but scanning every second forever to catch a lectern that may never load is not worth
        // paying for either.
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
    /// Searches the **loaded asset**, never an instance of it. Addressables hands back the prefab's
    /// own hierarchy, so GetComponentInChildren walks it in memory and Instantiate clones only the
    /// lectern's subtree - the room is never built, so there is nothing to wake and nothing to tear
    /// down. The cost of detaching it is that scale or rotation applied by its parents doesn't come
    /// with it; position doesn't matter, since the caller sets that.
    ///
    /// **The handle is deliberately never released**, which pins the room and its dependencies for
    /// the process. Releasing would drop the refcount to zero and let the bundle unload - taking
    /// the sprites and materials our clone still points at with it, leaving a book that renders as
    /// nothing. Same call as EquipmentPedestal makes for the podium prefab, for the same reason;
    /// the memory is the price of the art staying valid.
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
