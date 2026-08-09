using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Lamb.UI;
using Lamb.UI.Assets;

// UIUpgradeTreeMenuBase<T> is abstract and generic, so Harmony needs the constructed type. Both
// tree menus close it the same way (UIDLCUpgradeTreeMenuController derives from
// UIUpgradeTreeMenuController), so one patch covers both and the prefix filters by which
// configuration the menu holds rather than by type.
using TreeMenu = Lamb.UI.UIUpgradeTreeMenuBase<Lamb.UI.UIUpgradeUnlockOverlayController>;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Rearranges which tier each Divine Inspiration upgrade sits in.
///
/// Logic-neutral by construction. The tier gate is a *count* -
/// <c>NumUnlockedUpgrades() &gt;= NumRequiredNodesForTier(tier)</c> - so moving an upgrade
/// between tiers changes what the player sees and reaches, but not how many unlocks any tier
/// costs. Archipelago's rules count items and are untouched.
///
/// Two things have to be rewritten together or the drawn tree silently disagrees with the logic:
/// the configuration's per-tier membership lists, and each node component's own
/// <c>_upgrade</c> field, which is what the menu actually draws and compares against
/// (`TreeMenu.cs:356`). Rewriting only the first shows you one upgrade and sells
/// you another.
///
/// **Central nodes stay in their tier.** Each tier has a `RequiresCentralTier` node - the Temple
/// spine, plus the Refinery - that must be bought to open the next tier. Moving one to a
/// different tier makes that tier unopenable, which is a softlock rather than a shuffle. Pinning
/// them also keeps the Temple I-IV progression in order, which the structure prerequisites
/// expect anyway.
///
/// The intra-tree prerequisite graph is empty in this tree - all four `RequiresUpgrade` entries
/// have parents outside the 69 (PleasureSystem, TailorSystem, DiscipleSystem, System_PlayerTent)
/// - so there is no ordering constraint left to respect once centrals are pinned.
/// </summary>
internal static class DivineInspirationShuffle
{
    internal const int ModeDefault = 0;
    internal const int ModeRandomExceptFirst = 1;
    internal const int ModeTrueRandom = 2;

    /// <summary>Original upgrade -> the one that takes its place. Null when not shuffling.</summary>
    private static Dictionary<UpgradeSystem.Type, UpgradeSystem.Type> mapping;

    /// <summary>
    /// Each node's upgrade as the prefab authored it. Rewrites always go from here rather than
    /// from the node's current value, so re-opening the menu can't permute twice.
    /// </summary>
    private static readonly Dictionary<UpgradeTreeNode, UpgradeSystem.Type> originalNodeUpgrades =
        new();

    /// <summary>Tier membership as authored, for putting it back on disconnect.</summary>
    private static List<List<UpgradeSystem.Type>> originalTiers;

    /// <summary>
    /// Builds the permutation and rewrites the configuration. Node components are rewritten
    /// later, as each menu configures itself.
    /// </summary>
    internal static void Apply(int shuffleMode, int seed)
    {
        if (shuffleMode == ModeDefault) return;

        var tree = DivineInspirationPatch.Tree;
        if (tree == null)
        {
            Log.LogWarning("[AP] No Divine Inspiration tree yet - shuffle skipped.");
            return;
        }

        var tiers = tree.TierConfigurations;
        CaptureOriginalTiers(tiers);

        // Pinned: every tier's central node, and all of tier 1 in random_except_first.
        var pinned = new HashSet<UpgradeSystem.Type>(tiers.Select(t => t.CentralNode));
        if (shuffleMode == ModeRandomExceptFirst && originalTiers.Count > 0)
        {
            pinned.UnionWith(originalTiers[0]);
        }

        // Everything movable, in a stable order so the same seed always gives the same tree.
        var movable = originalTiers
            .SelectMany(t => t)
            .Where(u => !pinned.Contains(u))
            .ToList();

        Shuffle(movable, new Random(seed));

        mapping = new Dictionary<UpgradeSystem.Type, UpgradeSystem.Type>();
        var next = 0;

        for (var i = 0; i < tiers.Count; i++)
        {
            var rebuilt = new List<UpgradeSystem.Type>();

            foreach (var original in originalTiers[i])
            {
                // A pinned slot keeps its own upgrade; every other slot takes the next one off
                // the shuffled list. Tier sizes are therefore preserved exactly.
                var replacement = pinned.Contains(original) ? original : movable[next++];
                rebuilt.Add(replacement);
                mapping[original] = replacement;
            }

            // The getter hands back the live list, so this edits the asset in place.
            tiers[i].AllUpgradesInTier.Clear();
            tiers[i].AllUpgradesInTier.AddRange(rebuilt);
        }

        Log.LogInfo($"[AP] Divine Inspiration tree shuffled ({movable.Count} upgrades moved, "
            + $"{pinned.Count} pinned, seed {seed}).");
    }

    /// <summary>
    /// Puts the authored layout back. ScriptableObject edits last for the whole process, so
    /// without this a player who disconnects keeps a shuffled tree until they restart.
    /// </summary>
    internal static void Restore()
    {
        var tree = DivineInspirationPatch.Tree;

        if (tree != null && originalTiers != null)
        {
            var tiers = tree.TierConfigurations;
            for (var i = 0; i < tiers.Count && i < originalTiers.Count; i++)
            {
                tiers[i].AllUpgradesInTier.Clear();
                tiers[i].AllUpgradesInTier.AddRange(originalTiers[i]);
            }
        }

        foreach (var pair in originalNodeUpgrades)
        {
            if (pair.Key == null) continue;
            UpgradeField(pair.Key) = pair.Value;
            NodeRewrite.RefreshAuthoredVisuals(pair.Key);
        }

        mapping = null;
        originalTiers = null;
        originalNodeUpgrades.Clear();
    }

    private static void CaptureOriginalTiers(
        IReadOnlyList<UpgradeTreeConfiguration.TreeTierConfig> tiers)
    {
        // Only the first time: a reconnect must shuffle from the authored layout, not from
        // whatever the previous session left behind.
        originalTiers ??= tiers
            .Select(t => new List<UpgradeSystem.Type>(t.AllUpgradesInTier))
            .ToList();
    }

    /// <summary>Fisher-Yates on a seeded Random, so a reconnect rebuilds the identical tree.</summary>
    private static void Shuffle(IList<UpgradeSystem.Type> values, Random random)
    {
        for (var i = values.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (values[i], values[j]) = (values[j], values[i]);
        }
    }

    private static readonly AccessTools.FieldRef<UpgradeTreeNode, UpgradeSystem.Type> UpgradeField =
        AccessTools.FieldRefAccess<UpgradeTreeNode, UpgradeSystem.Type>("_upgrade");

    /// <summary>
    /// Points each drawn node at the upgrade the shuffle gave its slot.
    ///
    /// Configure() runs from the menu's Awake and again whenever it reopens, so this is written
    /// to be repeatable: it always maps from the node's authored value, never its current one.
    /// </summary>
    [HarmonyPatch(typeof(TreeMenu), nameof(TreeMenu.Configure))]
    internal static class NodeRewrite
    {
        [HarmonyPrefix]
        private static void Prefix(TreeMenu __instance)
        {
            if (mapping == null) return;

            var configuration = ConfigurationField(__instance);
            if (configuration == null || configuration != DivineInspirationPatch.Tree) return;

            foreach (var node in NodesField(__instance))
            {
                if (node == null) continue;

                if (!originalNodeUpgrades.TryGetValue(node, out var authored))
                {
                    authored = UpgradeField(node);
                    originalNodeUpgrades[node] = authored;
                }

                if (mapping.TryGetValue(authored, out var replacement))
                {
                    UpgradeField(node) = replacement;
                    RefreshVisuals(node);
                }
            }
        }

        /// <summary>
        /// Makes the node's art match the upgrade it now holds.
        ///
        /// Writing `_upgrade` alone moves the *name* - that's resolved from the field at
        /// runtime - but not the icon or category pip, which are baked onto the prefab. The
        /// result is a node captioned "Demonic Summoning Circle" wearing the Janitor Station's
        /// broom.
        ///
        /// UpgradeTreeNode.OnValidate() already does exactly this refresh (icon sprite,
        /// category text and colour, title, localize term) and is pure field assignment with
        /// nothing editor-only in it - but Unity only calls it in the editor, so a build never
        /// runs it. Calling the game's own routine beats reimplementing four lookups and
        /// getting one subtly wrong.
        /// </summary>
        /// <summary>Same refresh, used by Restore to put the authored art back.</summary>
        internal static void RefreshAuthoredVisuals(UpgradeTreeNode node) => RefreshVisuals(node);

        private static void RefreshVisuals(UpgradeTreeNode node)
        {
            if (OnValidate == null) return;

            try
            {
                OnValidate.Invoke(node, null);
            }
            catch (Exception e)
            {
                // Cosmetic only - a stale icon is much better than a throw inside menu setup.
                Log.LogWarning($"[AP] Couldn't refresh a tree node's art: {e.Message}");
            }
        }

        private static readonly System.Reflection.MethodInfo OnValidate =
            AccessTools.Method(typeof(UpgradeTreeNode), "OnValidate");

        private static readonly AccessTools.FieldRef<TreeMenu, UpgradeTreeConfiguration>
            ConfigurationField =
                AccessTools.FieldRefAccess<TreeMenu, UpgradeTreeConfiguration>(
                    "_configuration");

        private static readonly AccessTools.FieldRef<TreeMenu, List<UpgradeTreeNode>>
            NodesField =
                AccessTools.FieldRefAccess<TreeMenu, List<UpgradeTreeNode>>(
                    "_treeNodes");
    }
}
