using System;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.CultOfTheLamb.Patches;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Shares deaths with the rest of the multiworld.
/// </summary>
/// <remarks>
/// A received death never touches a permadeath save: the game promotes it to Results.GameOver
/// (UIDeathScreenOverlayController.cs:51), which deletes the save file (:404). Those saves
/// still send.
/// </remarks>
internal class DeathLinkService : IService
{
    // Matches worlds/cult_of_the_lamb/options.py DeathLink.
    internal const int ModeOff = 0;
    internal const int ModeClassic = 1;

    // The game's own penalty for dying in a dungeon, which the configured loss scales against
    private const float VanillaDeathFaithLoss = 10f;

    private readonly ArchipelagoSession session;
    private readonly int mode;
    private readonly int cooldownSeconds;
    private readonly int faithLoss;
    private readonly string slotName;

    private Archipelago.MultiClient.Net.BounceFeatures.DeathLink.DeathLinkService link;

    // Health.Kill only queues the damage, so the death screen arrives long after Kill() returns.
    // Hence a window rather than a flag released on the way out.
    private float suppressSendUntil = float.NegativeInfinity;
    private const float SuppressSendWindow = 5f;

    // Unity time of the last accepted death, for the cooldown
    private float lastAcceptedAt = float.NegativeInfinity;

    // A death that arrived while the player could not be killed, waiting for a moment they can
    private bool deathPending;

    internal DeathLinkService(
        ArchipelagoSession session, int mode, int cooldownSeconds, int faithLoss, string slotName)
    {
        this.session = session;
        this.mode = mode;
        this.cooldownSeconds = cooldownSeconds;
        this.faithLoss = faithLoss;
        this.slotName = slotName;
    }

    public void Register()
    {
        if (mode == ModeOff)
        {
            return;
        }

        link = session.CreateDeathLinkService();
        link.EnableDeathLink();
        link.OnDeathLinkReceived += HandleReceived;
        DeathScreenPatch.OnPlayerKilled += HandleLocalDeath;

        Log.LogInfo($"[AP] DeathLink active: mode {ModeName}"
            + (mode == ModeClassic
                ? $", {cooldownSeconds}s cooldown, {faithLoss} Faith per death."
                : "."));
    }

    public void Unregister()
    {
        DeathScreenPatch.OnPlayerKilled -= HandleLocalDeath;

        if (link != null)
        {
            link.OnDeathLinkReceived -= HandleReceived;
        }

        deathPending = false;
    }

    private string ModeName => mode == ModeClassic ? "classic" : "off";

    private void HandleLocalDeath()
    {
        if (Time.time < suppressSendUntil)
        {
            suppressSendUntil = float.NegativeInfinity;
            Log.LogInfo("[AP] DeathLink: this death came from the multiworld, not sending it back.");
            return;
        }

        link?.SendDeathLink(new DeathLink(slotName, $"{slotName} died in a crusade."));
        Log.LogInfo("[AP] DeathLink: sent a death to the multiworld.");
    }

    // Websocket thread, so nothing here may touch Unity.
    private void HandleReceived(DeathLink death)
    {
        var who = string.IsNullOrEmpty(death.Source) ? "Someone" : death.Source;
        MainThreadQueue.Enqueue(() => Accept(who, death.Cause));
    }

    private void Accept(string who, string cause)
    {
        if (DataManager.Instance != null && DataManager.Instance.PermadeDeathActive)
        {
            Log.LogInfo($"[AP] DeathLink from {who} refused: this is a permadeath save, and the "
                + "game deletes those on death.");
            ApNotification.Show(
                $"Archipelago: {who} died. Permadeath saves are never killed by Death Link.",
                NotificationBase.Flair.Negative,
                ApColors.Red);
            return;
        }

        if (cooldownSeconds > 0 && Time.time - lastAcceptedAt < cooldownSeconds)
        {
            Log.LogInfo($"[AP] DeathLink from {who} dropped: still inside the "
                + $"{cooldownSeconds}s cooldown.");
            return;
        }

        lastAcceptedAt = Time.time;
        Log.LogInfo($"[AP] DeathLink received from {who}"
            + (string.IsNullOrEmpty(cause) ? "." : $": {cause}"));

        deathPending = true;
        TryApplyPendingDeath(who);
    }

#if AP_DEBUG_KEYS
    /// <summary>
    /// Feeds a death in as if the multiworld had sent one, through the real Accept path.
    /// </summary>
    internal void SimulateReceivedDeath()
    {
        Accept("a debug key", "testing Death Link");
    }
#endif

    /// <summary>
    /// Retries a death that arrived at a moment the player could not be killed.
    /// </summary>
    internal void Tick()
    {
        if (deathPending)
        {
            TryApplyPendingDeath(null);
        }
    }

    private void TryApplyPendingDeath(string who)
    {
        var player = PlayerFarming.Instance;
        if (player == null || player.health == null || !player.health.enabled)
        {
            return;
        }

        // Already dying: OnDie sets both, and a second kill stacks death screens
        if (player.health.invincible || player.health.untouchable)
        {
            deathPending = false;
            return;
        }

        deathPending = false;
        suppressSendUntil = Time.time + SuppressSendWindow;
        try
        {
            player.health.Kill();
            ApplyFaithLoss();

            if (who != null)
            {
                ApNotification.Show(
                    $"Archipelago: {who} died, and so did you.",
                    NotificationBase.Flair.Negative,
                    ApColors.Red);
            }
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] DeathLink could not apply a death: {e.Message}");
        }
        finally
        {
            // suppressSendUntil deliberately left running: the death screen has not fired yet
        }
    }

    // The game's own route, so the thought, notification and clamping behave as for a real death.
    // No-ops when faith is hidden or the cult is empty (CultFaithManager.cs:283).
    private void ApplyFaithLoss()
    {
        if (faithLoss <= 0)
        {
            return;
        }

        CultFaithManager.AddThought(
            Thought.Cult_DiedInDungeon, -1, faithLoss / VanillaDeathFaithLoss);
    }
}
