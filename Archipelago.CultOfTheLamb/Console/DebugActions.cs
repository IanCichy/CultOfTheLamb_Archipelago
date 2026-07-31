using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using I2.Loc;
using Lamb.UI;
using Lamb.UI.Assets;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Console;

/// <summary>
/// The bodies behind DebugCommands' keybinds. Each one exercises exactly one candidate AP
/// feature against the real game API so a single debug build can prove or kill all of them in
/// one sitting (see docs/sprints/sprint-2-feature-slice.md). Every API called here was read
/// out of the decompiled source first - see DecompiledGamesViaDnSpy/Cotl/AI_INDEX.md §4b.
///
/// These are deliberately hardcoded single samples, not a general grant API. Once a feature
/// is proven, the real implementation belongs in a Service driven by received AP items.
/// </summary>
internal static class DebugActions
{
    // Chosen for visibility: an extra heart container is immediately obvious in the HUD.
    private const UpgradeSystem.Type SampleUpgrade = UpgradeSystem.Type.Combat_ExtraHeart1;
    private const TarotCards.Card SampleTarotCard = TarotCards.Card.Sun;
    private const PlayerFleeceManager.FleeceType SampleFleece = PlayerFleeceManager.FleeceType.Gold;

    /// <summary>F6 - resource filler items. Lowest-risk feature; API already used by two other mods.</summary>
    internal static void GiveResources()
    {
        // forceNormalInventory: true because Inventory.AddItem otherwise routes into the
        // *dungeon* inventory whenever BiomeGenerator.Instance exists (Inventory.cs:251),
        // which would make results depend on whether we're on a crusade.
        GiveItem(InventoryItem.ITEM_TYPE.LOG, 10);
        GiveItem(InventoryItem.ITEM_TYPE.STONE, 10);
        GiveItem(InventoryItem.ITEM_TYPE.BERRY, 5);
        GiveItem(InventoryItem.ITEM_TYPE.GOLD_NUGGET, 3);

        ApNotification.Show("Archipelago: received resources", NotificationBase.Flair.Positive);
    }

    private static void GiveItem(InventoryItem.ITEM_TYPE type, int quantity)
    {
        Inventory.AddItem(type, quantity, forceNormalInventory: true);
        Log.LogInfo($"[AP] Debug: gave {quantity}x {type}");
    }

    /// <summary>
    /// F2 - list the sermon upgrades you currently own, to the log.
    ///
    /// Randomizing sermons removes the game's own way of showing this tree: it's opened from
    /// exactly two places, SermonController.PlayerUpgrade() (which we suppress) and the Flock
    /// of the Faithful ritual. The Shrine shows the *building* tree, a different thing.
    ///
    /// This deliberately does NOT open UIUpgradePlayerTreeMenuController. That menu overrides
    /// OnCancelButtonInput() with an empty body - it cannot be dismissed - because it's only
    /// ever meant to be entered when the player is owed a pick. The single way out is
    /// DoUnlock(), which calls UpgradeSystem.UnlockAbility directly. So opening it to "just
    /// look" hands out a free upgrade the randomizer never granted, whatever DisciplePoints
    /// happens to be.
    ///
    /// TODO: a real in-game viewer needs to be our own UI, not the game's. Until then this is
    /// log-only, which is safe but not player-facing.
    /// </summary>
    internal static void ListOwnedSermonUpgrades()
    {
        // Filter against the game's own sermon tree rather than guessing by name prefix.
        // Prefix matching was wrong: "Relic_Pack_Default" starts with "Relic" but isn't a
        // sermon upgrade at all, and showed up in the list as an untranslated term.
        var tree = GameManager.GetInstance()?.UpgradePlayerConfiguration?.AllUpgrades;
        if (tree == null)
        {
            Log.LogWarning("[AP] Debug: sermon tree unavailable - can't list upgrades.");
            return;
        }

        var owned = new List<string>();
        var missing = new List<string>();
        foreach (var upgrade in tree)
        {
            var line = $"{Safe(() => UpgradeSystem.GetLocalizedName(upgrade))}  [{upgrade}]";
            if (UpgradeSystem.GetUnlocked(upgrade)) owned.Add(line);
            else missing.Add(line);
        }

        Log.LogInfo($"[AP] Sermon upgrades: {owned.Count} owned of {tree.Count}.");
        foreach (var line in owned) Log.LogInfo($"[AP]   have  {line}");
        foreach (var line in missing) Log.LogInfo($"[AP]   want  {line}");

        ApNotification.Show($"Archipelago: {owned.Count}/{tree.Count} sermon upgrades - see the log",
            NotificationBase.Flair.None);
    }

    /// <summary>
    /// F3 - fill the sermon bar so the very next sermon pays out immediately.
    ///
    /// Exists because testing sermon randomization otherwise means grinding real sermons, one
    /// per in-game day, each needing a flock's worth of accumulated XP. SermonController reads
    /// the stored XP when the sermon starts and pays out if it already meets the target
    /// (SermonController.cs:82), so pre-filling it here is enough - the reward still runs
    /// through the game's own code path rather than us faking the event.
    /// </summary>
    internal static void FillSermonBar()
    {
        if (DataManager.Instance == null)
        {
            Log.LogWarning("[AP] Debug: DataManager not ready - can't fill the sermon bar.");
            return;
        }

        var target = DoctrineUpgradeSystem.GetXPTargetBySermon(SermonCategory.PlayerUpgrade);
        DoctrineUpgradeSystem.SetXPBySermon(SermonCategory.PlayerUpgrade, target);

        var level = DataManager.Instance.Doctrine_PlayerUpgrade_Level;
        Log.LogInfo($"[AP] Debug: sermon XP set to target ({target}); currently at level {level}. "
            + "Give a sermon at the Temple to trigger the payout.");
        ApNotification.Show("Archipelago: sermon bar filled - give a sermon",
            NotificationBase.Flair.Positive);
    }

    /// <summary>F7 - sermon/ability upgrade. The biggest payoff feature (~35 items + ~35 locations).</summary>
    internal static void UnlockSampleSermon()
    {
        // Prefer the visible sample, but any real save is likely to have it already - and an
        // "already unlocked" result proves GetUnlocked works while proving nothing about the
        // grant path, which is the thing actually under test. So fall back to whatever is
        // still locked.
        var target = SampleUpgrade;
        if (UpgradeSystem.GetUnlocked(target))
        {
            Log.LogInfo($"[AP] Debug: {target} already unlocked; looking for a locked upgrade instead.");
            if (!TryFindLockedUpgrade(out target))
            {
                Log.LogInfo("[AP] Debug: every UpgradeSystem.Type is already unlocked on this save.");
                ApNotification.Show("Archipelago: every upgrade is already unlocked");
                return;
            }
        }

        Log.LogInfo($"[AP] Debug: unlocking upgrade {target}");

        // instant: true plays the game's own unlock-reveal sequence, which is what an AP item
        // grant should feel like. Returns false if it was already unlocked.
        var granted = UpgradeSystem.UnlockAbility(target, instant: true);
        Log.LogInfo($"[AP] Debug: UnlockAbility({target}) returned {granted}; "
            + $"GetUnlocked now {UpgradeSystem.GetUnlocked(target)}");

        ApNotification.Show(
            granted ? $"Archipelago: unlocked {target}" : $"Archipelago: {target} was already unlocked",
            granted ? NotificationBase.Flair.Positive : NotificationBase.Flair.None);
    }

    private static bool TryFindLockedUpgrade(out UpgradeSystem.Type locked)
    {
        foreach (UpgradeSystem.Type candidate in System.Enum.GetValues(typeof(UpgradeSystem.Type)))
        {
            if (UpgradeSystem.GetUnlocked(candidate)) continue;
            locked = candidate;
            return true;
        }

        locked = default;
        return false;
    }

    /// <summary>F8 - tarot card unlock.</summary>
    internal static void UnlockSampleTarot()
    {
        // Same already-unlocked problem as F7: fall back to a card the save doesn't have, so
        // the grant path is what actually gets exercised.
        var target = SampleTarotCard;
        var found = DataManager.Instance?.PlayerFoundTrinkets;
        if (found != null && found.Contains(target))
        {
            Log.LogInfo($"[AP] Debug: tarot card {target} already found; looking for an unfound one.");
            if (!TryFindUnfoundTarot(out target))
            {
                Log.LogInfo("[AP] Debug: every tarot card is already found on this save.");
                ApNotification.Show("Archipelago: every tarot card is already found");
                return;
            }
        }

        Log.LogInfo($"[AP] Debug: unlocking tarot card {target}");

        // UnlockTrinket queues the game's own card-unlocked alert as a side effect, so this
        // should produce visible feedback without us adding any.
        var granted = TarotCards.UnlockTrinket(target);
        Log.LogInfo($"[AP] Debug: UnlockTrinket({target}) returned {granted}; "
            + $"PlayerFoundTrinkets now has {DataManager.Instance?.PlayerFoundTrinkets?.Count ?? 0} card(s)");

        ApNotification.Show(
            granted ? $"Archipelago: unlocked tarot card {target}" : $"Archipelago: {target} was already found",
            granted ? NotificationBase.Flair.Positive : NotificationBase.Flair.None);
    }

    /// <summary>
    /// DataManager.AllTrinkets is the master card list; PlayerFoundTrinkets is what the save
    /// has. TarotCards.GetUnfoundTrinkets() computes this diff itself, but going through the
    /// two lists directly keeps the debug path independent of that helper's own filtering.
    /// </summary>
    private static bool TryFindUnfoundTarot(out TarotCards.Card card)
    {
        var found = DataManager.Instance?.PlayerFoundTrinkets;
        if (DataManager.AllTrinkets != null && found != null)
        {
            foreach (var candidate in DataManager.AllTrinkets)
            {
                if (found.Contains(candidate)) continue;
                card = candidate;
                return true;
            }
        }

        card = default;
        return false;
    }

    /// <summary>F10 - fleece unlock. There is no UnlockFleece API; the save state is a List&lt;int&gt;.</summary>
    internal static void UnlockSampleFleece()
    {
        var dataManager = DataManager.Instance;
        if (dataManager?.UnlockedFleeces == null)
        {
            Log.LogWarning("[AP] Debug: DataManager/UnlockedFleeces not ready.");
            return;
        }

        var fleeceId = (int)SampleFleece;
        if (dataManager.UnlockedFleeces.Contains(fleeceId))
        {
            Log.LogInfo($"[AP] Debug: fleece {SampleFleece} ({fleeceId}) already unlocked.");
            ApNotification.Show($"Archipelago: fleece {SampleFleece} was already unlocked");
            return;
        }

        dataManager.UnlockedFleeces.Add(fleeceId);
        Log.LogInfo($"[AP] Debug: unlocked fleece {SampleFleece} ({fleeceId}); "
            + $"{dataManager.UnlockedFleeces.Count} fleece(s) now unlocked. "
            + "Verify in the fleece selection menu at the temple.");
        ApNotification.Show($"Archipelago: unlocked fleece {SampleFleece}", NotificationBase.Flair.Positive);
    }

    /// <summary>F11 - notification pipeline, including the I2 term-registration fix.</summary>
    internal static void ShowSampleNotification()
    {
        Log.LogInfo("[AP] Debug: showing sample notifications (one per flair).");

        // Distinct text per flair matters: NotificationCentre dedupes by key within a frame,
        // and ApNotification derives the key from the text - identical strings would collapse
        // into a single popup and make this look broken.
        ApNotification.Show("Archipelago: neutral notification", NotificationBase.Flair.None);
        ApNotification.Show("Archipelago: positive notification", NotificationBase.Flair.Positive);
        ApNotification.Show("Archipelago: negative notification", NotificationBase.Flair.Negative);
    }

    /// <summary>
    /// F4 - write the internal-name -> display-name table for every unlockable system to a
    /// file next to the BepInEx log.
    ///
    /// These display names only exist at runtime: the decompile has the I2 *term keys*
    /// (e.g. "UpgradeSystem/PUpgrade_WeaponCritHit/Name") but the English text lives in a
    /// Unity asset. AP item/location names are effectively permanent once seeds exist, so we
    /// want the real names before generating the tables rather than renaming later.
    /// </summary>
    internal static void DumpUnlockableNames()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Cult of the Lamb - unlockable name table");
        sb.AppendLine("# internal name | display name");
        sb.AppendLine();

        sb.AppendLine("## UpgradeSystem.Type (Divine Inspiration / sermon tree)");
        foreach (UpgradeSystem.Type t in System.Enum.GetValues(typeof(UpgradeSystem.Type)))
        {
            sb.AppendLine($"{t}\t{Safe(() => UpgradeSystem.GetLocalizedName(t))}");
        }

        sb.AppendLine();
        sb.AppendLine("## TarotCards.Card");
        foreach (TarotCards.Card c in System.Enum.GetValues(typeof(TarotCards.Card)))
        {
            var inPool = DataManager.AllTrinkets != null && DataManager.AllTrinkets.Contains(c);
            sb.AppendLine($"{c}\t{Safe(() => LocalizationManager.GetTranslation($"TarotCards/{c}/Name"))}"
                + $"\t{(inPool ? "IN_ALLTRINKETS" : "-")}");
        }

        sb.AppendLine();
        sb.AppendLine("## PlayerFleeceManager.FleeceType");
        foreach (PlayerFleeceManager.FleeceType f in System.Enum.GetValues(typeof(PlayerFleeceManager.FleeceType)))
        {
            // Fleece terms are keyed by the enum's numeric value, not its name
            // (FleeceSelectionMenu.cs:46, SandboxCategory.cs:40).
            sb.AppendLine($"{f} ({(int)f})\t{Safe(() => LocalizationManager.GetTranslation($"TarotCards/Fleece{(int)f}/Name"))}");
        }

        sb.AppendLine();
        sb.AppendLine("## InventoryItem.ITEM_TYPE (resources, meals, currencies, quest items)");
        foreach (InventoryItem.ITEM_TYPE item in System.Enum.GetValues(typeof(InventoryItem.ITEM_TYPE)))
        {
            // Term is "Inventory/{TYPE}" with no /Name suffix, unlike the other systems
            // (FollowerCommandItems.cs:1354).
            sb.AppendLine($"{item} ({(int)item})\t{Safe(() => LocalizationManager.GetTranslation($"Inventory/{item}"))}");
        }

        sb.AppendLine();
        sb.AppendLine("## CrownAbilities.TYPE");
        foreach (CrownAbilities.TYPE c in System.Enum.GetValues(typeof(CrownAbilities.TYPE)))
        {
            sb.AppendLine($"{c}\t{Safe(() => CrownAbilities.LocalisedName(c))}");
        }

        sb.AppendLine();
        sb.AppendLine("## DoctrineUpgradeSystem.DoctrineType");
        foreach (DoctrineUpgradeSystem.DoctrineType d in System.Enum.GetValues(typeof(DoctrineUpgradeSystem.DoctrineType)))
        {
            sb.AppendLine($"{d}\t{Safe(() => DoctrineUpgradeSystem.GetLocalizedName(d))}");
        }

        DumpUpgradeTrees(sb);

        var path = Path.Combine(Paths.BepInExRootPath, "ap_unlockable_names.txt");
        File.WriteAllText(path, sb.ToString());
        Log.LogInfo($"[AP] Debug: wrote unlockable name table to {path}");
        ApNotification.Show("Archipelago: wrote name table", NotificationBase.Flair.Positive);
    }

    /// <summary>
    /// Dumps the three upgrade-tree definitions. This is the authoritative answer to "which
    /// upgrades are actually sermon upgrades" and "which are Woolhaven-only" - the trees are
    /// Unity ScriptableObjects, so neither question is answerable from the decompile, and the
    /// community wiki turned out to be out of date on both.
    ///
    ///  - UpgradeTreeConfiguration      : the Divine Inspiration building/ritual tree
    ///  - UpgradePlayerConfiguration    : the Temple sermon tree  <- the one we randomize
    ///  - DLCUpgradeTreeConfiguration   : the Woolhaven tree
    ///
    /// AllUpgradesRequiringUpgrade is the real prerequisite graph, which we don't need for
    /// granting (UnlockAbility ignores prerequisites) but do want for sanity-checking the
    /// progressive chains against how the game actually orders them.
    /// </summary>
    private static void DumpUpgradeTrees(StringBuilder sb)
    {
        var gameManager = GameManager.GetInstance();
        if (gameManager == null)
        {
            sb.AppendLine("\n## Upgrade trees: GameManager unavailable");
            return;
        }

        DumpTree(sb, "UpgradeTreeConfiguration (Divine Inspiration: buildings/rituals)",
            gameManager.UpgradeTreeConfiguration);
        DumpTree(sb, "UpgradePlayerConfiguration (TEMPLE SERMON TREE)",
            gameManager.UpgradePlayerConfiguration);

        DumpTree(sb, "DLCUpgradeTreeConfiguration (Woolhaven)",
            gameManager.DLCUpgradeTreeConfiguration);
    }

    private static void DumpTree(StringBuilder sb, string label, UpgradeTreeConfiguration tree)
    {
        sb.AppendLine();
        sb.AppendLine($"## {label}");
        if (tree == null)
        {
            sb.AppendLine("(null)");
            return;
        }

        var all = tree.AllUpgrades;
        sb.AppendLine($"# AllUpgrades: {all?.Count ?? 0}");
        if (all != null)
        {
            foreach (var upgrade in all)
            {
                sb.AppendLine($"{upgrade}\t{Safe(() => UpgradeSystem.GetLocalizedName(upgrade))}");
            }
        }

        var requires = tree.AllUpgradesRequiringUpgrade;
        sb.AppendLine($"# Prerequisites (upgrade -> children unlocked by it): {requires?.Count ?? 0}");
        if (requires != null)
        {
            foreach (var entry in requires)
            {
                var children = entry.Children == null
                    ? ""
                    : string.Join(", ", entry.Children.ConvertAll(c => c.ToString()).ToArray());
                sb.AppendLine($"{entry.Upgrade} -> {children}");
            }
        }
    }

    /// <summary>
    /// I2 returns null for unregistered terms and some lookups throw when the localization
    /// system isn't fully up - a half-written table is worse than a marked-up one.
    /// </summary>
    private static string Safe(System.Func<string> get)
    {
        try
        {
            var value = get();
            return string.IsNullOrEmpty(value) ? "(no translation)" : value.Replace("\n", " ").Replace("\t", " ");
        }
        catch (System.Exception e)
        {
            return $"(error: {e.GetType().Name})";
        }
    }

    /// <summary>
    /// Which Snail Shrines are lit, and the ShrineNumber of any shrine in the current scene.
    ///
    /// The second part is the point: locations.py currently puts all five shrines in "Cult"
    /// (always reachable) because which ShrineNumber sits in which hub is a serialized prefab
    /// field the decompile can't show. Four of the five are actually behind hub access, so
    /// Archipelago believes them reachable earlier than they are. Standing in a hub and
    /// pressing F9 records the mapping needed to region-scope them properly.
    /// </summary>
    private static void DumpSnailShrines()
    {
        var dataManager = DataManager.Instance;
        if (dataManager != null)
        {
            Log.LogInfo("[AP] Snail shrines lit: "
                + $"0={dataManager.ShellsGifted_0} 1={dataManager.ShellsGifted_1} "
                + $"2={dataManager.ShellsGifted_2} 3={dataManager.ShellsGifted_3} "
                + $"4={dataManager.ShellsGifted_4}");
        }

        var shrines = Resources.FindObjectsOfTypeAll<Snail_Interaction>();
        Log.LogInfo($"[AP] Snail_Interaction in scene ({shrines?.Length ?? 0}) - "
            + $"current scene: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");

        if (shrines == null) return;
        foreach (var shrine in shrines)
        {
            if (shrine == null) continue;
            Log.LogInfo($"[AP]   ShrineNumber={shrine.ShrineNumber}  (object: {shrine.name})");
        }
    }

    /// <summary>F9 - dump client + game boss state to the log.</summary>
    internal static void DumpState(ArchipelagoClient ap)
    {
        Log.LogInfo("[AP] ---- Archipelago debug state dump ----");
        // What was actually connected with, rather than what the config currently says - those
        // differ the moment someone edits the panel without connecting.
        Log.LogInfo($"[AP] Connected: {ap?.IsConnected ?? false}"
            + $" | slot: '{ap?.lastSlotName}'"
            + $" | server: {ap?.lastServerUrl}");
        Log.LogInfo($"[AP] Region locking active: {RegionLockState.Active}");

        foreach (var pair in RegionMapping.RegionToDungeonLocation)
        {
            Log.LogInfo($"[AP]   {pair.Key} ({pair.Value}): "
                + $"unlocked={RegionLockState.IsUnlocked(pair.Value)}");
        }

        DumpGameBossState();
        DumpMiniBossesInScene();
        DumpSnailShrines();
        Log.LogInfo("[AP] ---- end dump ----");
    }

    private static void DumpGameBossState()
    {
        var dataManager = DataManager.Instance;
        if (dataManager == null)
        {
            Log.LogInfo("[AP] DataManager not available (main menu?).");
            return;
        }

        var bossesCompleted = dataManager.BossesCompleted;
        Log.LogInfo($"[AP] BossesCompleted ({bossesCompleted?.Count ?? 0}) - Bishops/DLC bosses:");
        if (bossesCompleted != null)
        {
            foreach (var location in bossesCompleted)
            {
                Log.LogInfo($"[AP]   {location}");
            }
        }

        var killedBosses = dataManager.KilledBosses;
        Log.LogInfo($"[AP] KilledBosses ({killedBosses?.Count ?? 0}) - minibosses/Witnesses:");
        if (killedBosses != null)
        {
            foreach (var bossKey in killedBosses)
            {
                var mapped = BossKeyMapping.BossKeyToCheckId.TryGetValue(bossKey, out var checkId)
                    ? checkId.ToString()
                    : "(no AP location)";
                Log.LogInfo($"[AP]   \"{bossKey}\" -> {mapped}");
            }
        }
    }

    /// <summary>
    /// Settles the one open question left from the research pass: which internal boss name
    /// carries which display name (Amdusias vs Valefar vs Barbatos, etc). Press F9 inside a
    /// boss room and every encounter in it prints its name alongside its I2 DisplayName term
    /// and that term's translation.
    ///
    /// Resources.FindObjectsOfTypeAll (rather than FindObjectsOfType) on purpose:
    /// MiniBossManager deactivates every encounter except the selected one (MiniBossManager.cs:129),
    /// so the active-only search would return just one of the four.
    /// </summary>
    private static void DumpMiniBossesInScene()
    {
        var miniBosses = Resources.FindObjectsOfTypeAll<MiniBossController>();
        Log.LogInfo($"[AP] MiniBossControllers in scene ({miniBosses?.Length ?? 0}) - "
            + "internal name -> display name:");

        if (miniBosses == null) return;

        foreach (var miniBoss in miniBosses)
        {
            if (miniBoss == null) continue;

            var displayTerm = miniBoss.DisplayName;
            var translated = string.IsNullOrEmpty(displayTerm)
                ? "(no DisplayName term)"
                : LocalizationManager.GetTranslation(displayTerm) ?? "(term not translated)";

            var mapped = BossKeyMapping.BossKeyToCheckId.TryGetValue(miniBoss.name, out var checkId)
                ? checkId.ToString()
                : "(no AP location)";

            Log.LogInfo($"[AP]   \"{miniBoss.name}\" -> \"{translated}\" "
                + $"[term: {displayTerm}] -> {mapped}");
        }
    }

    private static string Describe(Sprite sprite) =>
        sprite == null ? "(none)" : $"\"{sprite.name}\" bounds={sprite.bounds.size}";

    /// <summary>
    /// F1 - dumps every shop in the current scene and the renderer hierarchy behind each slot.
    ///
    /// Exists because a shop slot's art has no single source: item and decoration stalls get
    /// theirs from InventoryItemDisplay.SetImage, but tarot slots never call it - InitTarotShop
    /// guards that call with itemToBuy != NONE, which is never true for a card - so their card
    /// art is authored on the prefab and only findable by walking the hierarchy. ShopIconService
    /// takes the slot's own SpriteRenderer first and the first child one otherwise; this is how
    /// you check that guess picked the card and not a shadow or a highlight decal.
    ///
    /// Press it standing in a hub shop (F1).
    /// </summary>
    internal static void DumpShopSlots()
    {
        var shops = Object.FindObjectsOfType<shopKeeperManager>();
        Log.LogInfo($"[AP] shopKeeperManagers in scene: {shops?.Length ?? 0}");

        if (shops == null) return;

        foreach (var shop in shops)
        {
            if (shop == null) continue;

            Log.LogInfo($"[AP]   shop \"{shop.name}\" location={shop.Location} "
                + $"tarot={shop.TarotCardShop} decorations={shop.DecorationsForSale} "
                + $"daily={shop.DailyShop} slots={shop.itemSlots?.Length ?? 0}");

            if (shop.itemSlots == null) continue;

            foreach (var slot in shop.itemSlots)
            {
                if (slot == null)
                {
                    Log.LogInfo("[AP]     slot: (null)");
                    continue;
                }

                var buyItem = slot.GetComponent<Interaction_BuyItem>();
                var entry = buyItem?.itemForSale;
                var sale = entry == null
                    ? "(no BuyEntry)"
                    : $"tarot={entry.TarotCard} card={entry.Card} decoration={entry.decorationToBuy} "
                        + $"item={entry.itemToBuy} bought={entry.Bought}";

                Log.LogInfo($"[AP]     slot \"{slot.name}\" active={slot.activeInHierarchy} {sale}");

                // A slot can draw through three different things and the prefab decides which,
                // so dump all of them rather than assuming. InventoryItemDisplay's own wiring
                // goes first: SetImage writes to whichever of its targets is non-null, so the
                // nulls are as informative as the values.
                var display = slot.GetComponent<InventoryItemDisplay>();
                if (display == null)
                {
                    Log.LogInfo("[AP]       no InventoryItemDisplay");
                }
                else
                {
                    Log.LogInfo($"[AP]       InventoryItemDisplay: "
                        + $"spriteRenderer={Describe(display.spriteRenderer?.sprite)} "
                        + $"image={Describe(display.image?.sprite)} "
                        + $"outline={Describe(display.outline?.sprite)}");
                }

                // includeInactive: the hidden slots are exactly the interesting ones when a
                // card turns out to be already unlocked.
                foreach (var renderer in slot.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    Log.LogInfo($"[AP]       SpriteRenderer on \"{renderer.gameObject.name}\" "
                        + $"(same object: {renderer.gameObject == slot}) "
                        + $"active={renderer.gameObject.activeInHierarchy} "
                        + $"enabled={renderer.enabled} sprite={Describe(renderer.sprite)}");
                }

                foreach (var image in slot.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                {
                    Log.LogInfo($"[AP]       UI.Image on \"{image.gameObject.name}\" "
                        + $"(same object: {image.gameObject == slot}) "
                        + $"active={image.gameObject.activeInHierarchy} "
                        + $"enabled={image.enabled} sprite={Describe(image.sprite)}");
                }

                // Catches the case where the art is neither: a Spine skeleton or a mesh.
                foreach (var renderer in slot.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer is SpriteRenderer) continue;
                    Log.LogInfo($"[AP]       {renderer.GetType().Name} on "
                        + $"\"{renderer.gameObject.name}\" enabled={renderer.enabled}");
                }
            }
        }
    }
}
