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
/// The code behind DebugCommands' keys. Each one tests one feature against the real game API, so a
/// single debug build can check all of them in one sitting. Every API here was read out of the
/// decompiled source first (DcplIdx 4b).
/// </summary>
/// <remarks>
/// These are hardcoded single samples, not a general grant API. Once a feature works, the real
/// version belongs in a Service driven by received AP items.
/// </remarks>
internal static class DebugActions
{
    // Chosen for visibility, since an extra heart container is immediately obvious in the HUD.
    private const UpgradeSystem.Type SampleUpgrade = UpgradeSystem.Type.Combat_ExtraHeart1;
    private const TarotCards.Card SampleTarotCard = TarotCards.Card.Sun;
    private const PlayerFleeceManager.FleeceType SampleFleece = PlayerFleeceManager.FleeceType.Gold;

    // Lists what can actually be loaded through Resources. A path showing up in the decompile
    // doesn't mean the asset shipped there ("Prefabs/Structures/Statue - Sword" loads as null), so
    // listing them is the only way to know what a pedestal can be built from
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
            if (found == null)
            {
                continue;
            }

            foreach (var prefab in found)
            {
                if (prefab != null)
                {
                    sb.AppendLine($"\t{prefab.name}");
                }
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

    // Every Addressables key, filtered to things a weapon display could be built from. The crusade
    // podium art exists in dungeon rooms, and the question is whether the base can load it.
    // Resources can't answer that, but this can
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
                if (locator?.Keys == null)
                {
                    continue;
                }

                foreach (var key in locator.Keys)
                {
                    var name = key?.ToString();
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

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

    // Ctrl+F7. Dumps every weapon podium in the scene: hierarchy, renderers, scale, and which of
    // podiumOn, podiumOff and Lighting is active. Run it next to a real podium mid-crusade to see
    // what our base copy has to match, since the prefab layout isn't in the decompile
    internal static void DumpPodiumsInScene()
    {
        var found = 0;

        // Both kinds. The entrance room's rune sigils and the weapon shop's basins are different
        // classes with different art, and either could be the right look for a base display.
        foreach (var podium in UnityEngine.Object.FindObjectsOfType<Interaction_WeaponSelectionPodium>(true))
        {
            if (podium == null)
            {
                continue;
            }

            found++;

            DescribeSceneObject(podium.gameObject, "WeaponSelectionPodium");
            Log.LogInfo($"[AP]     podiumOn={State(podium.podiumOn)} "
                + $"podiumOff={State(podium.podiumOff)} Lighting={State(podium.Lighting)}");
        }

        foreach (var item in UnityEngine.Object.FindObjectsOfType<Interaction_WeaponItem>(true))
        {
            if (item == null)
            {
                continue;
            }

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

    // Ctrl+F6. Drops a row of weapon pedestals in front of the player, alternating received and
    // not received so both looks sit side by side. A test harness for placement, not the real
    // feature. Press again to clear and respawn
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
            + "paste these into whichever anchor you're placing - WeaponOriginX/Y, CurseOriginX/Y "
            + "or BookOriginX/Y.");
        ApNotification.Show($"Archipelago: X={origin.x:F2} Y={origin.y:F2} - see the log",
            NotificationBase.Flair.Positive);
    }

    // F6. Grants resource filler items
    internal static void GiveResources()
    {
        // forceNormalInventory, or Inventory.AddItem puts it in the crusade inventory whenever
        // BiomeGenerator.Instance exists (Inventory.cs:251)
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

    // F2. Lists the sermon upgrades you own to the log.
    //
    // Doesn't open UIUpgradePlayerTreeMenuController on purpose. That menu can't be closed and its
    // only exit hands out an upgrade, so opening it just to look gives a free upgrade the
    // multiworld never sent. SermonTreeViewer is the player-facing view, and this is the log one
    internal static void ListOwnedSermonUpgrades()
    {
        // Filter against the game's own sermon tree, not by name prefix. "Relic_Pack_Default"
        // starts with "Relic" but isn't a sermon upgrade
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
            if (UpgradeSystem.GetUnlocked(upgrade))
            {
                owned.Add(line);
            }
            else
            {
                missing.Add(line);
            }
        }

        Log.LogInfo($"[AP] Sermon upgrades: {owned.Count} owned of {tree.Count}.");
        foreach (var line in owned)
        {
            Log.LogInfo($"[AP]   have  {line}");
        }

        foreach (var line in missing)
        {
            Log.LogInfo($"[AP]   want  {line}");
        }

        ApNotification.Show($"Archipelago: {owned.Count}/{tree.Count} sermon upgrades - see the log",
            NotificationBase.Flair.None);
    }

    // F3. Fills the sermon bar so the next sermon pays out straight away, instead of grinding one
    // sermon per in-game day. SermonController pays out if the stored XP already meets the target
    // (SermonController.cs:82), so the reward still goes through the game's own code
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

    // F7. Grants a sermon or ability upgrade
    internal static void UnlockSampleSermon()
    {
        // Prefer the visible sample, but a real save probably has it already, and "already
        // unlocked" doesn't test the grant path. So fall back to anything still locked
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

        // instant is true so the game's own unlock-reveal sequence plays, which is what an AP
        // item grant should feel like. Returns false if it was already unlocked.
        var granted = UpgradeSystem.UnlockAbility(target, instant: true);
        Log.LogInfo($"[AP] Debug: UnlockAbility({target}) returned {granted}; "
            + $"GetUnlocked now {UpgradeSystem.GetUnlocked(target)}");

        ApNotification.Show(
            granted ? $"Archipelago: unlocked {target}" : $"Archipelago: {target} was already unlocked",
            granted ? NotificationBase.Flair.Positive : NotificationBase.Flair.None);
    }

    // What tier the Temple and Shrine buildings really are, compared with what UnlockedUpgrades
    // says. A mismatch means an upgrade routine died before swapping the buildings, which is the
    // state the old base-upgrade hard lock left behind. Read only.
    //
    // Don't add a key that calls BiomeBaseManager.UpgradeBase directly. Running that outside the
    // game's own flow can hard-lock a save, and it replays the upgrade cutscene on the next altar
    // visit
    internal static void DumpBaseStructureTiers()
    {
        foreach (var brain in StructureManager.GetAllStructuresOfType<Structures_Temple>())
        {
            Log.LogInfo($"[AP] Debug: Temple structure is {brain?.Data?.Type.ToString() ?? "null"}");
        }

        foreach (var shrine in BuildingShrine.Shrines)
        {
            Log.LogInfo($"[AP] Debug: Shrine structure is "
                + $"{shrine?.StructureBrain?.Data?.Type.ToString() ?? "null"}");
        }

        foreach (var tier in new[] { UpgradeSystem.Type.Building_Temple2,
                                     UpgradeSystem.Type.Temple_III,
                                     UpgradeSystem.Type.Temple_IV })
        {
            Log.LogInfo($"[AP] Debug: UnlockedUpgrades has {tier}: {UpgradeSystem.GetUnlocked(tier)}");
        }
    }

    private static bool TryFindLockedUpgrade(out UpgradeSystem.Type locked)
    {
        foreach (UpgradeSystem.Type candidate in System.Enum.GetValues(typeof(UpgradeSystem.Type)))
        {
            if (UpgradeSystem.GetUnlocked(candidate))
            {
                continue;
            }

            locked = candidate;
            return true;
        }

        locked = default;
        return false;
    }

    // F8 unlocks a tarot card
    internal static void UnlockSampleTarot()
    {
        // Same already-unlocked problem as F7. Fall back to a card the save doesn't have, so
        // the grant path is what gets exercised.
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

    // A card the save hasn't found yet, from AllTrinkets minus PlayerFoundTrinkets. Done by hand
    // rather than with GetUnfoundTrinkets, so its extra filtering can't hide anything
    private static bool TryFindUnfoundTarot(out TarotCards.Card card)
    {
        var found = DataManager.Instance?.PlayerFoundTrinkets;
        if (DataManager.AllTrinkets != null && found != null)
        {
            foreach (var candidate in DataManager.AllTrinkets)
            {
                if (found.Contains(candidate))
                {
                    continue;
                }

                card = candidate;
                return true;
            }
        }

        card = default;
        return false;
    }

    // F10. Unlocks a fleece. There's no UnlockFleece API, the save just holds a List<int> of ids
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

    // F11. Runs the notification pipeline, including registering the I2 term
    internal static void ShowSampleNotification()
    {
        Log.LogInfo("[AP] Debug: showing sample notifications (one per flair).");

        // Different text per flair. Popups with the same text share a key and collapse into one
        // within a frame, which would look broken
        ApNotification.Show("Archipelago: neutral notification", NotificationBase.Flair.None);
        ApNotification.Show("Archipelago: positive notification", NotificationBase.Flair.Positive);
        ApNotification.Show("Archipelago: negative notification", NotificationBase.Flair.Negative);
    }

    // F4. Writes the internal name to display name table for every unlockable system to a file
    // next to the BepInEx log. The English names only exist at runtime, and AP item and location
    // names are permanent once seeds exist, so the real names matter before naming anything
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
        foreach (DoctrineUpgradeSystem.DoctrineType d
            in System.Enum.GetValues(typeof(DoctrineUpgradeSystem.DoctrineType)))
        {
            sb.AppendLine($"{d}\t{Safe(() => DoctrineUpgradeSystem.GetLocalizedName(d))}");
        }

        // The weapon and curse families. Their names come from UpgradeSystem/{X}/Name
        // (EquipmentData.cs:15). TarotCards/{X}/Name only covers some weapons and returns the key
        // for the rest, which is how two families first shipped with made up names
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

    // The three upgrade tree definitions, which answer which upgrades are sermon upgrades and which
    // are Woolhaven only. They're ScriptableObjects, so the decompile can't answer it, and the wiki
    // was out of date.
    //
    //   UpgradeTreeConfiguration     the Divine Inspiration tree
    //   UpgradePlayerConfiguration   the Temple sermon tree, the one we randomize
    //   DLCUpgradeTreeConfiguration  the Woolhaven tree
    //
    // AllUpgradesRequiringUpgrade is the real prerequisite graph. Granting doesn't need it, since
    // UnlockAbility ignores prerequisites, but it's handy for checking the progressive chains
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

    // The tree layout: node positions and prerequisite lines, which the ScriptableObjects don't
    // have. That lives on the menu prefabs, so this reads those.
    //
    // Read only, and never instantiates. Configure() writes node state and would damage the shared
    // prefab, breaking the game's own tree menu
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
        if (nodes == null || nodes.Length == 0)
        {
            return;
        }

        var root = template.transform.root;
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        var prereqEdges = 0;
        var connectionEdges = 0;

        sb.AppendLine("# upgrade\ttier\tconfig\tanchoredPos\tlocalPos\trequiresUpgrade"
            + "\trequiresStructure\tstate\tprerequisites\tconnections\tpath");

        foreach (var node in nodes)
        {
            if (node == null)
            {
                continue;
            }

            var rect = Safe2(() => node.RectTransform);

            // anchoredPosition, because a prefab isn't in a scene and transform.position reads zero
            // for every node. All nodes share one parent, so these are comparable
            var anchored = rect == null ? Vector2.zero : rect.anchoredPosition;

            var local = rect == null
                ? Vector3.zero
                : root.InverseTransformPoint(rect.position);

            if (anchored.x < minX)
            {
                minX = anchored.x;
            }

            if (anchored.x > maxX)
            {
                maxX = anchored.x;
            }

            if (anchored.y < minY)
            {
                minY = anchored.y;
            }

            if (anchored.y > maxY)
            {
                maxY = anchored.y;
            }

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

        // The menu's own line renderers are a third source of edges. A line can span more than two
        // nodes, so the count alone can't rebuild the graph
        var lines = template.GetComponentsInChildren<NodeConnectionLine>(true);
        sb.AppendLine($"# NodeConnectionLines: {lines?.Length ?? 0}");
        if (lines == null)
        {
            return;
        }

        foreach (var line in lines)
        {
            if (line == null)
            {
                continue;
            }

            sb.AppendLine($"\t{NodeNames(Safe2(() => line.Nodes))}");
        }
    }

    private static string NodeNames(IEnumerable<UpgradeTreeNode> nodes)
    {
        if (nodes == null)
        {
            return "";
        }

        var names = new List<string>();
        foreach (var node in nodes)
        {
            if (node != null)
            {
                names.Add(Safe(() => node.Upgrade.ToString()));
            }
        }
        return string.Join(", ", names.ToArray());
    }

    // Where the node sits under the prefab, to show whether tier containers group them
    private static string HierarchyPath(Transform node, Transform root)
    {
        var parts = new List<string>();
        for (var t = node; t != null && t != root; t = t.parent)
        {
            parts.Add(t.name);
        }

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
                // The description too, since the sermon viewer shows it under each name and this
                // is how to tell why one comes out blank
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

    // The tier table, mainly the NumRequiredToUnlock thresholds.
    //
    // A node opens once NumUnlockedUpgrades() reaches its tier's threshold and its own
    // prerequisites are met (UpgradeTreeNode.cs:296). The threshold is a count of unlocks anywhere
    // in the tree, which is why Archipelago can gate tiers with a simple "N items" rule. Those
    // numbers only live in the ScriptableObject, so this is the way to read them
    private static void DumpTiers(StringBuilder sb, UpgradeTreeConfiguration tree)
    {
        var tiers = tree.TierConfigurations;
        sb.AppendLine($"# Tiers: {tiers?.Count ?? 0}");
        if (tiers == null)
        {
            return;
        }

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

            if (members == null)
            {
                continue;
            }

            foreach (var upgrade in members)
            {
                sb.AppendLine($"\t\t{upgrade}");
            }
        }
    }

    // The upgrade to building to upgrade chain, and whether it ever loops. If an upgrade's required
    // building sits behind that same upgrade, a seed gating both can't be won, and the decompile
    // can't answer it.
    //
    // Only direct and one-step links are listed. A wrong "no loops" from a half-right search is
    // worse than a plain list someone can read
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
            if (built != StructureBrain.TYPES.NONE)
            {
                structureToUpgrade[built] = upgrade;
            }
        }

        sb.AppendLine($"# Upgrades that unlock a structure: {structureToUpgrade.Count}");
        foreach (var pair in structureToUpgrade)
        {
            sb.AppendLine($"{pair.Value}\tunlocks\t{pair.Key}");
        }

        sb.AppendLine();
        sb.AppendLine("# Upgrades that REQUIRE a built structure (the cycle risk):");
        var found = 0;
        foreach (var upgrade in tree.AllUpgrades)
        {
            var required = Safe2(() => UpgradeSystem.GetRequiredBuilding(upgrade));
            if (required == null || required.Count == 0)
            {
                continue;
            }

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

    // Value-typed sibling of Safe, for the same reason
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

    // I2 returns null for missing terms and some lookups throw before localization is ready, so
    // this marks those instead of leaving the table half written
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

    // Which Snail Shrines are lit, and the ShrineNumber of any shrine in this scene.
    //
    // The second part is the useful one. locations.py puts all five shrines in "Cult" because
    // which ShrineNumber is in which hub is a prefab field the decompile can't show, and four of
    // them are behind hub access. Pressing F9 in each hub records the mapping needed to fix
    // that
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

        if (shrines == null)
        {
            return;
        }

        foreach (var shrine in shrines)
        {
            if (shrine == null)
            {
                continue;
            }

            Log.LogInfo($"[AP]   ShrineNumber={shrine.ShrineNumber}  (object: {shrine.name})");
        }
    }

    // Ctrl+F2. Marks all four Bishops beaten and breaks every chain on the Gateway door, so the
    // Narinder goal can be tested without a full playthrough.
    //
    // Save writes only. DoorRoomChainDoor.Start opens the door at 5 chains (four Bishops, and the
    // fourth breaks the fifth), so it's open the next time the Door Room loads.
    //
    // Doesn't send the Bishop checks, since those fire when a heart is taken. The goal re-check
    // below is the point
    /// <summary>
    /// Applies a Death Link as if another player had died.
    /// </summary>
    internal static void SimulateDeathLink(ArchipelagoClient ap)
    {
        if (ap?.DeathLinkService == null)
        {
            Log.LogWarning("[AP] Death Link is off on this seed, so there is nothing to simulate.");
            ApNotification.Show("Archipelago: Death Link is off on this seed");
            return;
        }

        Log.LogInfo("[AP] Debug: applying a simulated Death Link.");

        // The simulate hook only exists in a debug-keys build, same as the Follower gate bypass
#if AP_DEBUG_KEYS
        ap.DeathLinkService.SimulateReceivedDeath();
#endif
    }

    internal static void CompleteBishopsAndOpenGateway(ArchipelagoClient ap)
    {
        // Only on a confirmed Narinder goal, even in a debug build. On a Bishops or Witnesses seed
        // this would win instantly, since Recheck() counts BossesCompleted. Checking for "not some
        // other goal" instead let a disconnected session through, and the win got reported on the
        // next connect
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
            if (dataManager.BossesCompleted.Contains(bishop))
            {
                continue;
            }

            dataManager.BossesCompleted.Add(bishop);
            added++;
        }

        dataManager.DoorRoomChainProgress = 5;

        // The Narinder doors also want Followers. The patch only exists in a debug-keys build.
#if AP_DEBUG_KEYS
        Patches.FollowerGateBypassPatch.Enabled = true;
#endif

        Log.LogInfo($"[AP] Debug: {added} Bishop(s) recorded as beaten "
            + $"({dataManager.BossesCompleted.Count} total), Gateway chains all broken, "
            + "Follower door requirements bypassed for this session. "
            + "Return to the Door Room and the final door will be open.");

        // The real kills raise events the services listen to, and a direct write doesn't, so
        // nudge the goal by hand.
        ap?.GoalService?.Recheck();
    }

    // Ctrl+F9. Everything about the objective guide, then a sweep and rebuild.
    //
    // It reads each line's text back from I2, because a missing term and a broken UI both show a
    // blank line. The save list counts catch leaks: after a disconnect they must all be zero, or a
    // vanilla quest log keeps Archipelago lines
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
        // too, because it is how you check that a disconnected or never-connected save is clean.
        if (guide != null)
        {
            guide.ForceRebuild();
        }
        else
        {
            Services.QuestGuideService.SweepAll();
        }

        Log.LogInfo("[AP] ---- end objective guide ----");
    }

    // The weapon and curse pools from both sides. The game's pool is printed in full so you can
    // compare it before and after a session
    private static void DumpEquipmentPools(ArchipelagoClient ap)
    {
        foreach (var service in new[] { ap?.WeaponPoolService, ap?.CursePoolService })
        {
            if (service != null)
            {
                Log.LogInfo($"[AP] {service.DescribeState()}");
            }
        }

        if (ap?.WeaponPoolService != null || ap?.CursePoolService != null)
        {
            return;
        }

        // Still worth printing when the options are off, since the invariant above is about
        // the game's data rather than about our state.
        var dataManager = DataManager.Instance;
        if (dataManager == null)
        {
            return;
        }

        Log.LogInfo($"[AP] Equipment randomization off. WeaponPool "
            + $"({dataManager.WeaponPool.Count}): {string.Join(", ", dataManager.WeaponPool)}");
        Log.LogInfo($"[AP] CursePool ({dataManager.CursePool.Count}): "
            + string.Join(", ", dataManager.CursePool));
    }

    // Part of F9. Logs where max HP came from, since tarot effect values live in prefab data the
    // decompile doesn't carry. RunTrinkets is the set HasTrinket reads, so it's what actually applies.
    private static void DumpPlayerHealthAndRunTrinkets()
    {
        try
        {
            var player = PlayerFarming.Instance;
            if (player == null)
            {
                Log.LogInfo("[AP] Health: no player right now (menu or loading).");
                return;
            }

            var health = player.health;
            if (health != null)
            {
                // Special hearts hang off Health, not DataManager
                Log.LogInfo($"[AP] Health: HP {health.HP}/{health.totalHP}"
                    + $" | black {health.BlackHearts}"
                    + $" | spirit {health.TotalSpiritHearts}"
                    + $" | blue {health.BlueHearts}"
                    + $" | fire {health.FireHearts}"
                    + $" | ice {health.IceHearts}");
            }

            // The inputs to PLAYER_TOTAL_HEALTH (HealthPlayer.cs:150), so a wrong total points
            // at a specific term
            var data = DataManager.Instance;
            if (data != null)
            {
                Log.LogInfo($"[AP] Max HP inputs: health modified {data.PLAYER_HEALTH_MODIFIED}"
                    + $" | hearts level {data.PLAYER_HEARTS_LEVEL}"
                    + $" | removed {data.PLAYER_REMOVED_HEARTS}"
                    + $" | fleece {data.PlayerFleece}");
            }

            var run = player.RunTrinkets;
            Log.LogInfo($"[AP] Run trinkets ({run?.Count ?? 0}) - these are the ones actually "
                + "applying effects this run:");
            if (run != null)
            {
                foreach (var trinket in run)
                {
                    Log.LogInfo($"[AP]   {trinket.CardType} (upgrade {trinket.UpgradeIndex})");
                }
            }

            var found = data?.PlayerFoundTrinkets;
            Log.LogInfo($"[AP] Collection holds {found?.Count ?? 0} card(s), which only widens "
                + "the offer pool.");
        }
        catch (System.Exception e)
        {
            Log.LogWarning($"[AP] Could not read health or run trinkets: {e.Message}");
        }
    }

    // Ctrl+F4. Most of the flags SetTutorialVariables (DataManager.cs:391) sets from
    // QuickStartActive, set one by one because that method also resets health, game time,
    // HasBuiltShrine1 and the save id.
    internal static void FinishTutorial()
    {
        var d = DataManager.Instance;
        if (d == null)
        {
            Log.LogWarning("[AP] No save loaded - load a save before using this key.");
            return;
        }

        d.QuickStartActive = true;

        // First, and not optional: both tutorial exit paths are guarded on !ShowLoyaltyBars, so
        // forcing that without this strands the save with the door re-locking on every load.
        d.OnboardingFinished = true;

        d.AllowSaving = true;
        d.EnabledHealing = true;
        d.EnabledSpells = true;
        d.BuildShrineEnabled = true;
        d.CookedFirstFood = true;
        d.XPEnabled = true;
        d.InTutorial = true;
        d.Tutorial_Second_Enter_Base = true;
        d.AllowBuilding = true;
        d.ShowLoyaltyBars = true;
        d.ShowCultFaith = true;
        d.ShowCultHunger = true;
        d.ShowCultIllness = true;
        d.UnlockBaseTeleporter = true;
        d.BonesEnabled = true;
        d.PauseGameTime = false;
        d.ShownDodgeTutorial = true;
        d.ShownInventoryTutorial = true;
        d.HasEncounteredTarot = true;
        d.OnboardedHomeless = true;
        d.ForceDoctrineStones = true;
        d.HadInitialDeathCatConversation = true;
        d.PlayerHasBeenGivenHearts = true;
        d.BaseGoopDoorLocked = false;
        d.CanBuildShrine = true;
        d.FirstDoctrineStone = true;

        // BaseGoopDoor.Start has already run if we're in the base, so the field alone won't
        // lower a wall that is already up.
        BaseGoopDoor.UnblockGoopDoor();

        Log.LogInfo("[AP] Debug: tutorial flags set to their Quick Start values. Building, faith "
            + "and the base door should all be live. Health, game time and the save id were left "
            + "alone. Re-enter the base if the door is still up.");
    }

    // F9 dumps client and game boss state to the log
    internal static void DumpState(ArchipelagoClient ap)
    {
        Log.LogInfo("[AP] ---- Archipelago debug state dump ----");
        // What was connected with, rather than what the config currently says. Those
        // differ the moment someone edits the panel without connecting.
        Log.LogInfo($"[AP] Connected: {ap?.IsConnected ?? false}"
            + $" | slot: '{ap?.LastSlotName}'"
            + $" | server: {ap?.LastServerUrl}");
        Log.LogInfo($"[AP] Region locking active: {RegionLockState.Active}");

        DumpPlayerHealthAndRunTrinkets();

        foreach (var pair in RegionMapping.RegionToDungeonLocation)
        {
            Log.LogInfo($"[AP]   {pair.Key} ({pair.Value}): "
                + $"unlocked={RegionLockState.IsUnlocked(pair.Value)}");
        }

        DumpGameBossState();
        DumpBaseStructureTiers();
        DumpTarotState();
        DumpEquipmentPools(ap);
        DumpEquippedAndWeaponData();

        if (ap?.DivineInspirationService != null)
        {
            Log.LogInfo($"[AP] {ap.DivineInspirationService.DescribeState()}");
        }

        if (ap?.BuildingService != null)
        {
            Log.LogInfo($"[AP] {ap.BuildingService.DescribeState()}");
        }

        if (ap?.BroomService != null)
        {
            Log.LogInfo($"[AP] {ap.BroomService.DescribeState()}");
        }

        if (ap?.QuestGuideService != null)
        {
            Log.LogInfo($"[AP] {ap.QuestGuideService.DescribeState()}");
        }

        if (ap?.QuestTrimService != null)
        {
            Log.LogInfo($"[AP] {ap.QuestTrimService.DescribeState()}");
        }

        DumpMiniBossesInScene();
        DumpSnailShrines();
        Log.LogInfo("[AP] ---- end dump ----");

        // Post message on screen as feedback for players
        ApNotification.Show("Archipelago: diagnostics saved to the log for your bug report",
            NotificationBase.Flair.Positive, ApColors.Blue);
    }

    // Whether the tarot collection is in the state it should be. While connected, the game's
    // collection must hold zero managed cards. If the sweep stops working they come back as real
    // unlocks and strand their checks, without any log line
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
        if (lent != null)
        {
            granted.AddRange(lent);
        }

        Log.LogInfo($"[AP] PlayerFoundTrinkets ({found.Count}) - the game's own collection:");
        foreach (var card in found)
        {
            Log.LogInfo($"[AP]   {card}");
        }

        Log.LogInfo($"[AP] Archipelago has granted {granted.Count} card(s):");
        foreach (var card in granted)
        {
            Log.LogInfo($"[AP]   {card}");
        }

        var leaked = new List<TarotCards.Card>();
        foreach (var card in found)
        {
            if (granted.Contains(card))
            {
                leaked.Add(card);
            }
        }

        // Only meaningful while connected. Disconnected, everything is correctly back in the
        // collection and an overlap here is the desired end state rather than a leak.
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

    // Which internal boss name goes with which display name (Amdusias, Valefar, Barbatos and so
    // on). Press F9 in a boss room and each encounter prints its name, its I2 term and the text.
    //
    // Resources.FindObjectsOfTypeAll, because MiniBossManager turns off every encounter except the
    // chosen one (MiniBossManager.cs:129), so an active-only search finds just one of four
    private static void DumpMiniBossesInScene()
    {
        var miniBosses = Resources.FindObjectsOfTypeAll<MiniBossController>();
        Log.LogInfo($"[AP] MiniBossControllers in scene ({miniBosses?.Length ?? 0}) - "
            + "internal name -> display name:");

        if (miniBosses == null)
        {
            return;
        }

        foreach (var miniBoss in miniBosses)
        {
            if (miniBoss == null)
            {
                continue;
            }

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

    // F1, in a hub shop. Dumps every shop in the scene and the renderers behind each slot. Tarot
    // slot art is set on the prefab rather than through SetImage, so walking the hierarchy is the
    // only way to check ShopIconService picked the card and not a shadow or highlight
    internal static void DumpShopSlots()
    {
        var shops = Object.FindObjectsOfType<shopKeeperManager>();
        Log.LogInfo($"[AP] shopKeeperManagers in scene: {shops?.Length ?? 0}");

        if (shops == null)
        {
            return;
        }

        foreach (var shop in shops)
        {
            if (shop == null)
            {
                continue;
            }

            Log.LogInfo($"[AP]   shop \"{shop.name}\" location={shop.Location} "
                + $"tarot={shop.TarotCardShop} decorations={shop.DecorationsForSale} "
                + $"daily={shop.DailyShop} slots={shop.itemSlots?.Length ?? 0}");

            if (shop.itemSlots == null)
            {
                continue;
            }

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

                // A slot can draw through three different things, so dump them all.
                // InventoryItemDisplay goes first, since SetImage writes to whichever of its
                // targets isn't null, so the nulls matter too
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

                // includeInactive, because the hidden slots are exactly the interesting ones
                // when a card turns out to be already unlocked.
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

                // Catches the case where the art is neither, such as a Spine skeleton or a mesh.
                foreach (var renderer in slot.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer is SpriteRenderer)
                    {
                        continue;
                    }

                    Log.LogInfo($"[AP]       {renderer.GetType().Name} on "
                        + $"\"{renderer.gameObject.name}\" enabled={renderer.enabled}");
                }
            }
        }
    }

    // Equipped weapon and curse, fleece, and run levels, for equipment bug reports
    internal static void DumpEquippedAndWeaponData()
    {
        var dataManager = DataManager.Instance;
        if (dataManager == null)
        {
            Log.LogInfo("[AP] No save loaded, so no equipment state to dump.");
            return;
        }

        Log.LogInfo($"[AP] Fleece {dataManager.PlayerFleece}"
            + $" | swaps weapon for curse: {Safe(() => PlayerFleeceManager.FleeceSwapsWeaponForCurse().ToString())}");

        var player = PlayerFarming.Instance;
        if (player != null)
        {
            Log.LogInfo($"[AP] Equipped weapon {player.currentWeapon} lvl {player.currentWeaponLevel}"
                + $" | curse {player.currentCurse} lvl {player.currentCurseLevel}");
        }

        Log.LogInfo($"[AP] StartingEquipmentLevel {DataManager.StartingEquipmentLevel}"
            + $" | CurrentRunWeaponLevel {dataManager.CurrentRunWeaponLevel}"
            + $" | CurrentRunCurseLevel {dataManager.CurrentRunCurseLevel}");
    }
}
