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

    /// <summary>
    /// Lists what's actually reachable through Resources, written alongside the name table.
    ///
    /// The decompile has ~80 Resources.Load paths, but a path appearing in code doesn't mean the
    /// asset shipped there - "Prefabs/Structures/Statue - Sword" is referenced by DungeonDecorator
    /// and resolves to null at runtime. Enumerating is the only way to know what a pedestal can
    /// actually be built from.
    /// </summary>
    private static void DumpResourcePrefabs(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("## Resources prefabs actually present");

        foreach (var folder in new[] { "", "Prefabs", "Prefabs/Structures", "Prefabs/Resources" })
        {
            GameObject[] found;
            try
            {
                found = Resources.LoadAll<GameObject>(folder);
            }
            catch (System.Exception e)
            {
                sb.AppendLine($"### '{folder}' - threw {e.GetType().Name}");
                continue;
            }

            sb.AppendLine();
            sb.AppendLine($"### '{folder}': {found?.Length ?? 0} GameObject(s)");
            if (found == null) continue;

            foreach (var prefab in found)
            {
                if (prefab != null) sb.AppendLine($"\t{prefab.name}");
            }
        }

        DumpAddressableKeys(sb);

        // The specific candidates a pedestal might be built from, probed by exact path.
        sb.AppendLine();
        sb.AppendLine("### Direct path probes");
        foreach (var path in new[]
        {
            "Prefabs/Structures/Statue - Sword",
            "Prefabs/Resources/WeaponPickUp",
            "Prefabs/Structures/Buildings/Altar",
            "Prefabs/Resources/ResourceCustomTarget",
        })
        {
            var hit = Safe2(() => Resources.Load<GameObject>(path));
            sb.AppendLine($"\t{(hit != null ? "OK  " : "NULL")}  {path}");
        }
    }

    /// <summary>
    /// Every Addressables key the game has registered, filtered to things a weapon display could
    /// be built from.
    ///
    /// The crusade podiums are real scene objects (Interaction_WeaponSelectionPodium, with
    /// podiumOn/podiumOff/Lighting child art) authored into dungeon rooms, so the art exists - the
    /// only question is whether it can be addressed from the base. Nothing in Resources answers
    /// that; this does.
    /// </summary>
    private static void DumpAddressableKeys(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("### Addressables keys (filtered)");

        var matched = 0;
        var total = 0;

        try
        {
            foreach (var locator in UnityEngine.AddressableAssets.Addressables.ResourceLocators)
            {
                if (locator?.Keys == null) continue;

                foreach (var key in locator.Keys)
                {
                    var name = key?.ToString();
                    if (string.IsNullOrEmpty(name)) continue;

                    total++;
                    if (name.IndexOf("podium", System.StringComparison.OrdinalIgnoreCase) < 0
                        && name.IndexOf("plinth", System.StringComparison.OrdinalIgnoreCase) < 0
                        && name.IndexOf("weapon", System.StringComparison.OrdinalIgnoreCase) < 0
                        && name.IndexOf("statue", System.StringComparison.OrdinalIgnoreCase) < 0
                        && name.IndexOf("entrance", System.StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    matched++;
                    sb.AppendLine($"\t{name}");
                }
            }

            sb.AppendLine($"# {matched} matched of {total} key(s) total.");
        }
        catch (System.Exception e)
        {
            sb.AppendLine($"(Addressables enumeration failed: {e.GetType().Name} - {e.Message})");
        }
    }

    /// <summary>
    /// Ctrl+F7 - dumps every weapon podium in the loaded scene: hierarchy, renderers, scale and
    /// which of podiumOn/podiumOff/Lighting is active.
    ///
    /// Press it standing next to a real one mid-crusade and the output is the ground truth our
    /// base clone has to match - the prefab's authored structure isn't in the decompile.
    /// </summary>
    internal static void DumpPodiumsInScene()
    {
        var found = 0;

        // Both kinds: the entrance room's rune sigils and the weapon shop's basins are different
        // classes with different art, and either could be the right look for a base display.
        foreach (var podium in UnityEngine.Object.FindObjectsOfType<Interaction_WeaponSelectionPodium>(true))
        {
            if (podium == null) continue;
            found++;

            DescribeSceneObject(podium.gameObject, "WeaponSelectionPodium");
            Log.LogInfo($"[AP]     podiumOn={State(podium.podiumOn)} "
                + $"podiumOff={State(podium.podiumOff)} Lighting={State(podium.Lighting)}");
        }

        foreach (var item in UnityEngine.Object.FindObjectsOfType<Interaction_WeaponItem>(true))
        {
            if (item == null) continue;
            found++;

            DescribeSceneObject(item.gameObject, "WeaponItem (shop slot)");
        }

        Log.LogInfo($"[AP] Podium-like objects in scene: {found}");
        ApNotification.Show($"Archipelago: dumped {found} podium(s)", NotificationBase.Flair.Positive);
    }

    private static void DescribeSceneObject(GameObject go, string kind)
    {
        Log.LogInfo($"[AP] --- {kind} '{go.name}' at {go.transform.position}, "
            + $"scale {go.transform.lossyScale}, active {go.activeInHierarchy}");

        foreach (var renderer in go.GetComponentsInChildren<SpriteRenderer>(true))
        {
            Log.LogInfo($"[AP]     {HierarchyPath(renderer.transform, go.transform)}: "
                + $"active {renderer.gameObject.activeInHierarchy}, bounds {renderer.bounds.size}, "
                + $"layer '{SortingLayer.IDToName(renderer.sortingLayerID)}', "
                + $"order {renderer.sortingOrder}, sprite '{renderer.sprite?.name}'");
        }
    }

    private static string State(GameObject go) =>
        go == null ? "(null)" : go.activeInHierarchy ? "ON" : "off";

    /// <summary>
    /// Ctrl+F6 - drops a row of weapon pedestals in front of the player, alternating received and
    /// not-received so both tints can be compared side by side.
    ///
    /// A scratch harness for the placement pass, not the real feature: the statue is authored for
    /// dungeon decoration, so its size and sorting at base scale can only be found by looking at
    /// it. Press again to clear and respawn.
    /// </summary>
    internal static void SpawnEquipmentPedestals()
    {
        var player = PlayerFarming.Instance;
        if (player == null)
        {
            Log.LogWarning("[AP] Debug: no player, so nowhere to measure from.");
            return;
        }

        // A row starting where the player stands, so walking to a spot and pressing this shows
        // exactly what those config values would look like.
        var origin = player.transform.position;

        UI.EquipmentPedestal.Clear();

        var families = new[]
        {
            EquipmentType.Sword,
            EquipmentType.Axe,
            EquipmentType.Hammer,
            EquipmentType.Fireball,
        };

        var spacing = ArchipelagoPlugin.PedestalSpacing?.Value ?? 2f;
        for (var i = 0; i < families.Length; i++)
        {
            UI.EquipmentPedestal.Spawn(
                families[i], origin + new Vector3(i * spacing, 0f, 0f), received: i % 2 == 0);
        }

        Log.LogInfo($"[AP] Debug: preview row at X={origin.x:F2} Y={origin.y:F2} - "
            + "paste these into PedestalOriginX / PedestalOriginY to keep it.");
        ApNotification.Show($"Archipelago: X={origin.x:F2} Y={origin.y:F2} - see the log",
            NotificationBase.Flair.Positive);
    }

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
    /// F2 - list the sermon upgrades you currently own, to the log. Randomizing sermons removes
    /// the game's own way of showing this tree.
    ///
    /// Deliberately does NOT open UIUpgradePlayerTreeMenuController: that menu can't be
    /// dismissed (empty OnCancelButtonInput) and its only exit is DoUnlock(), so opening it to
    /// "just look" hands out a free upgrade the randomizer never granted.
    ///
    /// The player-facing viewer is now SermonTreeViewer ("Archipelago" on the Temple Altar menu),
    /// which opens the game's own tree read-only. This stays as the log-only diagnostic: it's the
    /// independent second opinion, since both read UpgradePlayerConfiguration.
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

        // The weapon and curse families this world pools. Added late, and their absence is why two
        // of them shipped with invented names: the display names in items.py were taken from
        // TarotCards/{X}/Name, a term family that only happens to carry some weapon names, and
        // silently returns the key itself for the rest. The real term is UpgradeSystem/{X}/Name
        // (EquipmentData.cs:15), which is what GetLocalisedTitle reads.
        sb.AppendLine();
        sb.AppendLine("## EquipmentType (weapon + curse families)");
        foreach (EquipmentType e in System.Enum.GetValues(typeof(EquipmentType)))
        {
            // The game's own accessor rather than the raw term, so a player's custom Legendary
            // names come out too.
            sb.AppendLine($"{e} ({(int)e})\t"
                + $"{Safe(() => EquipmentManager.GetEquipmentData(e)?.GetLocalisedTitle())}");
        }

        // Backs the 25 curated building locations, which have never been checked against the game
        // either.
        sb.AppendLine();
        sb.AppendLine("## StructureBrain.TYPES (buildings)");
        foreach (StructureBrain.TYPES s in System.Enum.GetValues(typeof(StructureBrain.TYPES)))
        {
            sb.AppendLine($"{s} ({(int)s})\t{Safe(() => StructuresData.GetLocalizedNameStatic(s))}");
        }

        DumpUpgradeTrees(sb);

        var path = Path.Combine(Paths.BepInExRootPath, "ap_unlockable_names.txt");
        File.WriteAllText(path, sb.ToString());
        Log.LogInfo($"[AP] Debug: wrote unlockable name table to {path}");
        ApNotification.Show("Archipelago: wrote name table", NotificationBase.Flair.Positive);
    }

    /// <summary>
    /// Dumps the three upgrade-tree definitions - the authoritative answer to which upgrades are
    /// sermon upgrades and which are Woolhaven-only. The trees are ScriptableObjects, so neither
    /// is answerable from the decompile, and the wiki was out of date on both.
    ///
    ///  - UpgradeTreeConfiguration      : the Divine Inspiration building/ritual tree
    ///  - UpgradePlayerConfiguration    : the Temple sermon tree  <- the one we randomize
    ///  - DLCUpgradeTreeConfiguration   : the Woolhaven tree
    ///
    /// AllUpgradesRequiringUpgrade is the real prerequisite graph - not needed for granting,
    /// since UnlockAbility ignores prerequisites, but useful for checking the progressive chains.
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

        DumpTreePrefabs(sb);
        DumpResourcePrefabs(sb);
        DumpStructureCoupling(sb);
    }

    /// <summary>
    /// The tree *shape* - node positions and prerequisite edges - which the ScriptableObjects above
    /// do not carry. UpgradeTreeConfiguration has AllUpgrades and tiers but no layout, and its
    /// AllUpgradesRequiringUpgrade is nearly empty (DivineInspirationShuffle.cs:36-38). The authored
    /// graph lives on the menu prefabs instead, so this reads them to size up an in-game viewer.
    ///
    /// Read-only, and deliberately never instantiates: Configure() writes node state and would
    /// damage the shared prefab, taking the game's own tree menu down with it.
    /// </summary>
    private static void DumpTreePrefabs(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("## Tree menu prefabs (node graph + layout)");

        var ui = MonoSingleton<UIManager>.Instance;
        if (ui == null)
        {
            sb.AppendLine("(UIManager unavailable - load a save first)");
            return;
        }

        DumpTreePrefab(sb, "UpgradeTreeMenuTemplate (Divine Inspiration)", ui.UpgradeTreeMenuTemplate);
        DumpTreePrefab(sb, "UpgradePlayerTreeMenuTemplate (SERMON)", ui.UpgradePlayerTreeMenuTemplate);
        DumpTreePrefab(sb, "DLCUpgradeTreeMenuTemplate (Woolhaven)", ui.DLCUpgradeTreeMenuTemplate);
    }

    private static void DumpTreePrefab(StringBuilder sb, string label, Component template)
    {
        sb.AppendLine();
        sb.AppendLine($"### {label}");
        if (template == null)
        {
            sb.AppendLine("(null - not loaded)");
            return;
        }

        var nodes = template.GetComponentsInChildren<UpgradeTreeNode>(true);
        sb.AppendLine($"# Nodes: {nodes?.Length ?? 0}");
        if (nodes == null || nodes.Length == 0) return;

        var root = template.transform.root;
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        var prereqEdges = 0;
        var connectionEdges = 0;

        sb.AppendLine("# upgrade\ttier\tconfig\tanchoredPos\tlocalPos\trequiresUpgrade"
            + "\trequiresStructure\tstate\tprerequisites\tconnections\tpath");

        foreach (var node in nodes)
        {
            if (node == null) continue;

            var rect = Safe2(() => node.RectTransform);

            // anchoredPosition is the only usable coordinate here. A prefab asset isn't in a scene,
            // so transform.position reads as zero for every node - measured, see the localPos
            // column. Every node is a direct child of one NodesContainer, so these are already in
            // a single comparable space.
            var anchored = rect == null ? Vector2.zero : rect.anchoredPosition;

            var local = rect == null
                ? Vector3.zero
                : root.InverseTransformPoint(rect.position);

            if (anchored.x < minX) minX = anchored.x;
            if (anchored.x > maxX) maxX = anchored.x;
            if (anchored.y < minY) minY = anchored.y;
            if (anchored.y > maxY) maxY = anchored.y;

            var prereqs = node.PrerequisiteNodes;
            var conns = node.NodeConnections;
            prereqEdges += prereqs?.Length ?? 0;
            connectionEdges += conns?.Count ?? 0;

            sb.AppendLine($"{Safe(() => node.Upgrade.ToString())}"
                + $"\t{Safe(() => node.NodeTier.ToString())}"
                + $"\t{Safe2(() => node.TreeConfig?.name) ?? "(null)"}"
                + $"\t{anchored.x:F1},{anchored.y:F1}"
                + $"\t{local.x:F1},{local.y:F1}"
                + $"\t{Safe(() => node.RequiresUpgrade.ToString())}"
                + $"\t{Safe(() => node.RequiresBuiltStructure.ToString())}"
                + $"\t{Safe(() => node.State.ToString())}"
                + $"\t{NodeNames(prereqs)}"
                + $"\t{NodeNames(conns)}"
                + $"\t{HierarchyPath(node.transform, root)}");
        }

        sb.AppendLine($"# Bounds (anchored): x {minX:F1}..{maxX:F1}  y {minY:F1}..{maxY:F1}"
            + $"  size {maxX - minX:F1} x {maxY - minY:F1}");
        sb.AppendLine($"# Edge candidates: PrerequisiteNodes={prereqEdges}, NodeConnections={connectionEdges}");

        // The third possible edge source: the menu's own line renderers. Read as components rather
        // than through UIUpgradeTreeMenuBase<T>.NodeConnections, which would need the concrete
        // generic argument per template. A line may span more than two nodes, which is why the
        // count alone isn't enough to reconstruct the graph.
        var lines = template.GetComponentsInChildren<NodeConnectionLine>(true);
        sb.AppendLine($"# NodeConnectionLines: {lines?.Length ?? 0}");
        if (lines == null) return;
        foreach (var line in lines)
        {
            if (line == null) continue;
            sb.AppendLine($"\t{NodeNames(Safe2(() => line.Nodes))}");
        }
    }

    private static string NodeNames(IEnumerable<UpgradeTreeNode> nodes)
    {
        if (nodes == null) return "";
        var names = new List<string>();
        foreach (var node in nodes)
        {
            if (node != null) names.Add(Safe(() => node.Upgrade.ToString()));
        }
        return string.Join(", ", names.ToArray());
    }

    /// <summary>Where the node sits under the prefab, to show whether tier containers group them.</summary>
    private static string HierarchyPath(Transform node, Transform root)
    {
        var parts = new List<string>();
        for (var t = node; t != null && t != root; t = t.parent) parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts.ToArray());
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
                // Description too: the sermon viewer folds it under each node's name, and whether
                // I2 actually has a term for these is the only way to tell why one comes out blank.
                sb.AppendLine($"{upgrade}\t{Safe(() => UpgradeSystem.GetLocalizedName(upgrade))}"
                    + $"\tDESC: {Safe(() => UpgradeSystem.GetLocalizedDescription(upgrade))}");
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

        DumpTiers(sb, tree);
    }

    /// <summary>
    /// The tier table - and specifically the cumulative NumRequiredToUnlock thresholds, which
    /// are the whole reason this dump exists for Sprint 0e.
    ///
    /// A node is available when NumUnlockedUpgrades() >= NumRequiredNodesForTier(its tier)
    /// AND its own prerequisites are met (UpgradeTreeNode.cs:296). The first half is a
    /// cumulative *count* of upgrades unlocked anywhere in the tree, not a set of specific
    /// ones - which is what lets Archipelago express tier access as a plain
    /// "N progression items received" rule with no graph traversal. Those N values live only
    /// in the ScriptableObject, so this is the only way to read them.
    /// </summary>
    private static void DumpTiers(StringBuilder sb, UpgradeTreeConfiguration tree)
    {
        var tiers = tree.TierConfigurations;
        sb.AppendLine($"# Tiers: {tiers?.Count ?? 0}");
        if (tiers == null) return;

        var cumulative = 0;
        foreach (var tier in tiers)
        {
            cumulative += tier.NumRequiredToUnlock;
            var members = tier.AllUpgradesInTier;
            sb.AppendLine($"{tier.Tier}\tcentral={tier.CentralNode}"
                + $"\trequiresCentral={tier.RequiresCentralTier}"
                + $"\tnumRequired={tier.NumRequiredToUnlock}"
                + $"\tCUMULATIVE_THRESHOLD={cumulative}"
                + $"\tmembers={members?.Count ?? 0}");

            if (members == null) continue;
            foreach (var upgrade in members) sb.AppendLine($"\t\t{upgrade}");
        }
    }

    /// <summary>
    /// The tech -> building -> tech coupling, and whether it ever forms a cycle. If an upgrade's
    /// required building is itself gated behind that same upgrade, Archipelago can't gate both
    /// systems in one seed without generating something unwinnable - Sprints 0e and 7 both
    /// depend on the answer, and it isn't readable from the decompile.
    ///
    /// Only direct and one-hop relationships are reported: a wrong "no cycles" from a half-right
    /// traversal is worse than an honest list someone can read.
    /// </summary>
    private static void DumpStructureCoupling(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("## Upgrade <-> structure coupling (tech -> building -> tech cycle check)");

        var gameManager = GameManager.GetInstance();
        var tree = gameManager?.UpgradeTreeConfiguration;
        if (tree?.AllUpgrades == null)
        {
            sb.AppendLine("(tree unavailable)");
            return;
        }

        // Which upgrade unlocks which structure, so a required building can be traced back.
        var structureToUpgrade = new Dictionary<StructureBrain.TYPES, UpgradeSystem.Type>();
        foreach (var upgrade in tree.AllUpgrades)
        {
            var built = Safe2(() => UpgradeSystem.GetStructureTypeFromUpgrade(upgrade));
            if (built != StructureBrain.TYPES.NONE) structureToUpgrade[built] = upgrade;
        }

        sb.AppendLine($"# Upgrades that unlock a structure: {structureToUpgrade.Count}");
        foreach (var pair in structureToUpgrade) sb.AppendLine($"{pair.Value}\tunlocks\t{pair.Key}");

        sb.AppendLine();
        sb.AppendLine("# Upgrades that REQUIRE a built structure (the cycle risk):");
        var found = 0;
        foreach (var upgrade in tree.AllUpgrades)
        {
            var required = Safe2(() => UpgradeSystem.GetRequiredBuilding(upgrade));
            if (required == null || required.Count == 0) continue;

            found++;
            foreach (var structure in required)
            {
                var gatedBy = structureToUpgrade.TryGetValue(structure, out var by)
                    ? by.ToString()
                    : "(not gated by any upgrade)";
                var cycle = gatedBy == upgrade.ToString() ? "  <<< DIRECT CYCLE" : "";
                sb.AppendLine($"{upgrade}\tneeds\t{structure}\twhich is unlocked by\t{gatedBy}{cycle}");
            }
        }

        if (found == 0)
        {
            sb.AppendLine("(none - no upgrade requires a built structure, so no cycle is possible)");
        }
    }

    /// <summary>Value-typed sibling of <see cref="Safe"/>, for the same reason.</summary>
    private static T Safe2<T>(System.Func<T> get)
    {
        try
        {
            return get();
        }
        catch (System.Exception e)
        {
            Log.LogWarning($"[AP] Debug: tree lookup failed: {e.GetType().Name}");
            return default;
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

    /// <summary>
    /// Everything about the objective guide, then a sweep-and-rebuild.
    ///
    /// The per-line I2 read-back is the reason this exists as its own key. A term that never
    /// registered and a broken UI look identical in game - a blank quest line - and nothing
    /// else distinguishes them. The same read-back is what diagnosed the blank-notification
    /// bug three attempts in.
    ///
    /// The save-list counts are the leak detector: after a disconnect every one must be zero,
    /// or a player is left with Archipelago lines in a vanilla quest log. The rebuild at the
    /// end lets one session exercise add -> sweep -> re-add without reconnecting.
    /// </summary>
    /// <summary>
    /// Ctrl+F2 - records all four Bishops as beaten and breaks every chain on the Gateway door,
    /// so the Narinder goal can be tested without a full playthrough.
    ///
    /// Save writes only. DoorRoomChainDoor.Start reads BossesCompleted and DoorRoomChainProgress
    /// and sets DoorActive at >= 5 chains (there are five breaks for four Bishops - the fourth
    /// triggers the fifth), so the door is open the next time the Door Room loads.
    ///
    /// Does **not** send the four Bishop location checks: those fire from Interaction_MonsterHeart
    /// when a heart is actually taken, and faking that is a different job. The goal re-check below
    /// is what this key is for.
    /// </summary>
    internal static void CompleteBishopsAndOpenGateway(ArchipelagoClient ap)
    {
        // Refuses on any other goal even though the key is compiled out of normal builds: on a
        // Bishops or Witnesses seed this would just be a way to win instantly, since Recheck()
        // counts straight from BossesCompleted and would report victory to the server.
        // Requires a *confirmed* narinder goal rather than merely failing to see another one.
        // Written the other way round, a disconnected session (GoalService null) slipped through
        // and wrote BossesCompleted anyway - then connecting afterwards on a Bishops seed made
        // GoalService.Register()'s catch-up check report instant victory. Same failure, deferred.
        if (ap?.GoalService == null || ap.GoalService.Goal != Services.GoalService.GoalNarinder)
        {
            Log.LogWarning("[AP] This key only works while connected to a narinder seed - on any "
                + "other goal it would report a false victory. Ignoring.");
            return;
        }

        var dataManager = DataManager.Instance;
        if (dataManager?.BossesCompleted == null)
        {
            Log.LogWarning("[AP] No save loaded - load a save before using this key.");
            return;
        }

        var added = 0;
        foreach (var bishop in RegionMapping.RegionToDungeonLocation.Values)
        {
            if (dataManager.BossesCompleted.Contains(bishop)) continue;
            dataManager.BossesCompleted.Add(bishop);
            added++;
        }

        dataManager.DoorRoomChainProgress = 5;

        Log.LogInfo($"[AP] Debug: {added} Bishop(s) recorded as beaten "
            + $"({dataManager.BossesCompleted.Count} total), Gateway chains all broken. "
            + "Return to the Door Room and the final door will be open.");

        // The real kills raise events the services listen to; a direct write doesn't, so nudge
        // the goal by hand.
        ap?.GoalService?.Recheck();
    }

    internal static void DumpQuestGuide(ArchipelagoClient ap)
    {
        Log.LogInfo("[AP] ---- objective guide ----");

        var guide = ap?.QuestGuideService;

        if (guide == null)
        {
            Log.LogInfo("[AP] No objective guide this session (not connected, or the "
                + "Archipelago Objective Guide option is off).");
        }
        else
        {
            Log.LogInfo($"[AP] {guide.DescribeState()}");
        }

        if (ap?.QuestTrimService != null)
        {
            Log.LogInfo($"[AP] {ap.QuestTrimService.DescribeState()}");
        }

        // ForceRebuild sweeps first, so this is one path either way. The bare sweep matters
        // too: it is how you check that a disconnected or never-connected save is clean.
        if (guide != null) guide.ForceRebuild();
        else Services.QuestGuideService.SweepAll();

        Log.LogInfo("[AP] ---- end objective guide ----");
    }

    /// <summary>
    /// What the weapon and curse pools look like from both sides. The game's own pool is
    /// printed in full because the claim being verified is that it's *never written to* -
    /// compare the line before and after a session.
    /// </summary>
    private static void DumpEquipmentPools(ArchipelagoClient ap)
    {
        foreach (var service in new[] { ap?.WeaponPoolService, ap?.CursePoolService })
        {
            if (service != null) Log.LogInfo($"[AP] {service.DescribeState()}");
        }

        if (ap?.WeaponPoolService != null || ap?.CursePoolService != null) return;

        // Still worth printing when the options are off, since the invariant above is about
        // the game's data rather than about our state.
        var dataManager = DataManager.Instance;
        if (dataManager == null) return;

        Log.LogInfo($"[AP] Equipment randomization off. WeaponPool "
            + $"({dataManager.WeaponPool.Count}): {string.Join(", ", dataManager.WeaponPool)}");
        Log.LogInfo($"[AP] CursePool ({dataManager.CursePool.Count}): "
            + string.Join(", ", dataManager.CursePool));
    }

    /// <summary>F9 - dump client + game boss state to the log.</summary>
    internal static void DumpState(ArchipelagoClient ap)
    {
        Log.LogInfo("[AP] ---- Archipelago debug state dump ----");
        // What was actually connected with, rather than what the config currently says - those
        // differ the moment someone edits the panel without connecting.
        Log.LogInfo($"[AP] Connected: {ap?.IsConnected ?? false}"
            + $" | slot: '{ap?.LastSlotName}'"
            + $" | server: {ap?.LastServerUrl}");
        Log.LogInfo($"[AP] Region locking active: {RegionLockState.Active}");

        foreach (var pair in RegionMapping.RegionToDungeonLocation)
        {
            Log.LogInfo($"[AP]   {pair.Key} ({pair.Value}): "
                + $"unlocked={RegionLockState.IsUnlocked(pair.Value)}");
        }

        DumpGameBossState();
        DumpTarotState();
        DumpEquipmentPools(ap);
        if (ap?.DivineInspirationService != null)
        {
            Log.LogInfo($"[AP] {ap.DivineInspirationService.DescribeState()}");
        }
        if (ap?.BuildingService != null) Log.LogInfo($"[AP] {ap.BuildingService.DescribeState()}");
        if (ap?.BroomService != null) Log.LogInfo($"[AP] {ap.BroomService.DescribeState()}");
        if (ap?.QuestGuideService != null) Log.LogInfo($"[AP] {ap.QuestGuideService.DescribeState()}");
        if (ap?.QuestTrimService != null) Log.LogInfo($"[AP] {ap.QuestTrimService.DescribeState()}");
        DumpMiniBossesInScene();
        DumpSnailShrines();
        Log.LogInfo("[AP] ---- end dump ----");
    }

    /// <summary>
    /// The one thing the log and the debt store can't tell you: whether the managed collection's
    /// invariant holds right now. While connected the game's own collection must contain zero
    /// managed cards - if ManagedCollection.Tick() stops sweeping they reappear as real unlocks
    /// and their checks are stranded, silently, since the sweep only logs what it hasn't already
    /// accounted for.
    /// </summary>
    private static void DumpTarotState()
    {
        var found = DataManager.Instance?.PlayerFoundTrinkets;
        if (found == null)
        {
            Log.LogInfo("[AP] PlayerFoundTrinkets unavailable (no save loaded).");
            return;
        }

        var granted = new List<TarotCards.Card>();
        var lent = Patches.TarotVisibility.GrantedCards?.Invoke();
        if (lent != null) granted.AddRange(lent);

        Log.LogInfo($"[AP] PlayerFoundTrinkets ({found.Count}) - the game's own collection:");
        foreach (var card in found) Log.LogInfo($"[AP]   {card}");

        Log.LogInfo($"[AP] Archipelago has granted {granted.Count} card(s):");
        foreach (var card in granted) Log.LogInfo($"[AP]   {card}");

        var leaked = new List<TarotCards.Card>();
        foreach (var card in found)
        {
            if (granted.Contains(card)) leaked.Add(card);
        }

        // Only meaningful while connected - disconnected, everything is correctly back in the
        // collection and an overlap here is the desired end state, not a leak.
        Log.LogInfo(leaked.Count == 0
            ? "[AP] Invariant OK: no Archipelago-granted card is in the game's collection."
            : $"[AP] INVARIANT BROKEN: {leaked.Count} granted card(s) are also real unlocks - "
                + "the sweep is not running. Their checks can no longer fire.");
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
    /// F1 (standing in a hub shop) - dumps every shop in the scene and the renderer hierarchy
    /// behind each slot. A slot's art has no single source: stalls get theirs from
    /// InventoryItemDisplay.SetImage, but tarot slots never call it, so their card art is
    /// authored on the prefab and only findable by walking the hierarchy. This is how you check
    /// ShopIconService's guess picked the card and not a shadow or a highlight decal.
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
