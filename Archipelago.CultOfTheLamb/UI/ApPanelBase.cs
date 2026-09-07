using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Archipelago.CultOfTheLamb.UI;

/// <summary>
/// Shared behaviour for the mod's IMGUI windows.
/// </summary>
/// <remarks>
/// That covers open/close, the design-height scale, freezing the player while a window is up, and
/// suspending the game's EventSystem so clicks don't fall through to the menu behind.
///
/// All of this was written for the connect panel and is unchanged here. It moved out only when a
/// second window, the upgrades panel, needed the same handling, since two copies of the
/// freeze/suspend pairing is exactly the kind of thing that drifts.
/// </remarks>
internal abstract class ApPanelBase
{
    /// <summary>
    /// The distinct constant that identifies this window to IMGUI.
    /// </summary>
    /// <remarks>
    /// IMGUI identifies windows by an int the caller picks, and two windows sharing one id fight
    /// over position and focus, so every subclass must return a value of its own.
    /// </remarks>
    protected abstract int WindowId { get; }

    /// <summary>Title bar text, and the drag handle.</summary>
    protected abstract string Title { get; }

    protected abstract void DrawContents(int id);

    internal bool IsOpen { get; private set; }

    // IMGUI's default font is unreadably small on anything modern. Everything is authored at this
    // nominal size and scaled to the actual screen, so layout maths stays in one space.
    protected const float DesignHeight = 1080f;

    protected Rect window = new(60f, 60f, 460f, 0f);

    // Whether *we* froze the player, so closing can't un-freeze something else that happened to
    // start while the panel was open, such as a cutscene or a shop purchase.
    private bool frozePlayer;

    // Set while the panel is open, so the game's UI can be handed back exactly what it had.
    private EventSystem suspendedEventSystem;

    private ApPanelHost host;

    internal void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    internal void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        if (host != null) host.enabled = true;
        FreezePlayer();
        SuspendGameUi();
        OnOpened();
    }

    internal void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        if (host != null) host.enabled = false;
        UnfreezePlayer();
        RestoreGameUi();
    }

    /// <summary>
    /// Hook for work that should happen once per opening rather than per frame, such as
    /// rebuilding a cached view of game state.
    /// </summary>
    /// <remarks>Does nothing by default.</remarks>
    protected virtual void OnOpened()
    {
    }

    /// <summary>
    /// Attaches this panel to the <see cref="ApPanelHost"/> whose OnGUI draws it.
    /// </summary>
    /// <param name="host">The component that will draw this panel.</param>
    /// <remarks>
    /// The host is disabled whenever the panel is closed, so Unity's IMGUI dispatch costs nothing
    /// for the ~99% of a session the panel isn't up.
    /// </remarks>
    internal void AttachTo(ApPanelHost host)
    {
        this.host = host;
        host.Panel = this;
        host.enabled = IsOpen;
    }

    /// <summary>Called from the host's OnGUI.</summary>
    internal void Draw()
    {
        if (!IsOpen) return;

        var scale = Screen.height / DesignHeight;
        var previousMatrix = GUI.matrix;

        // Scaling the whole matrix rather than each font size keeps hit-testing correct. Mouse
        // positions run through the same transform the drawing does.
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

        window = GUILayout.Window(WindowId, window, DrawContents, Title);

        GUI.matrix = previousMatrix;
    }

    /// <summary>
    /// Whether there's a loaded save underneath.
    /// </summary>
    /// <remarks>
    /// Connecting replays item history into save state and unlocks regions through DataManager;
    /// the upgrade trees likewise only exist once a save is up. At the main menu neither can do
    /// its job.
    /// </remarks>
    protected static bool HasLoadedSave() =>
        DataManager.Instance != null && PlayerFarming.Instance != null;

    /// <summary>
    /// Stops the lamb reacting to typing.
    /// </summary>
    /// <remarks>
    /// Without it, entering a slot name walks the player across the room and can trigger
    /// interactions.
    /// </remarks>
    private void FreezePlayer()
    {
        if (PlayerFarming.Instance == null) return;

        try
        {
            PlayerFarming.SetStateForAllPlayers(StateMachine.State.InActive, false, null);
            frozePlayer = true;
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Could not freeze the player for the panel: {e.Message}");
        }
    }

    private void UnfreezePlayer()
    {
        if (!frozePlayer) return;
        frozePlayer = false;

        if (PlayerFarming.Instance == null) return;

        try
        {
            PlayerFarming.SetStateForAllPlayers(StateMachine.State.Idle, false, null);
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Could not restore the player after the panel: {e.Message}");
        }
    }

    /// <summary>
    /// Switches off Unity's EventSystem while the panel is up.
    /// </summary>
    /// <remarks>
    /// IMGUI and the game's UI take input through completely separate paths, so a click inside
    /// this window also lands on whatever menu button sits behind it, and the panel is usually
    /// opened from the pause menu, which is exactly where that would happen. Turning the
    /// EventSystem off makes the menu behind inert until the panel closes, and stops arrow keys
    /// walking its selection while you type.
    /// </remarks>
    private void SuspendGameUi()
    {
        var eventSystem = EventSystem.current;
        if (eventSystem == null || !eventSystem.enabled) return;

        eventSystem.enabled = false;
        suspendedEventSystem = eventSystem;
    }

    private void RestoreGameUi()
    {
        if (suspendedEventSystem == null) return;

        suspendedEventSystem.enabled = true;
        suspendedEventSystem = null;
    }
}
