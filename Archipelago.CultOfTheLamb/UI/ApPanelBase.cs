using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Archipelago.CultOfTheLamb.UI;

/// <summary>
/// Shared behaviour for the mod's IMGUI windows.
/// </summary>
/// <remarks>
/// Covers open/close, the design-height scale, freezing the player while a window is up, and
/// suspending the game's UI input so clicks don't fall through to the menu behind.
///
/// Written for the connect panel, and moved out when the upgrades panel needed the same
/// handling. Two copies of the freeze/suspend pairing would drift.
///
/// Both of those side effects wait for the first repaint rather than applying on Open. Applying
/// them up front froze the player with the only way out drawn inside a window that never
/// appeared, and the player had to kill the game (BUG-013).
/// </remarks>
internal abstract class ApPanelBase
{
    // IMGUI identifies windows by an int the caller picks, and two windows sharing one id fight
    // over position and focus, so every subclass must return a value of its own
    protected abstract int WindowId { get; }

    // Title bar text, and the drag handle
    protected abstract string Title { get; }

    protected abstract void DrawContents(int id);

    internal bool IsOpen { get; private set; }

    // IMGUI's default font is unreadably small on anything modern. Everything is authored at this
    // nominal size and scaled to the actual screen, so layout maths stays in one space.
    protected const float DesignHeight = 1080f;

    protected Rect window = new(60f, 60f, 460f, 0f);

    // Whether we froze the player, so closing can't un-freeze something else that happened to
    // start while the panel was open, such as a cutscene or a shop purchase.
    private bool frozePlayer;

    // Set while the panel is open, so the game's UI can be handed back exactly what it had.
    private readonly List<BaseInputModule> suspendedModules = new();

    private ApPanelHost host;

    // Set by the first repaint of the current opening. Nothing is frozen or suspended before that,
    // so a window that never reaches the screen costs the player nothing
    private bool hasRepainted;

    // Both halves matter: no draw calls means OnGUI never ran, while draw calls with no repaints
    // means IMGUI ran and Unity never painted us. The first BUG-013 report could not tell those
    // apart, and neither could we
    private int drawCalls;
    private int repaintCalls;

    private bool drawFailed;
    private bool loggedScaleFallback;

    // Every panel ever built, so the plugin's Update can tick them all without knowing which ones
    // exist. One entry per panel constructed in Awake, so it never grows during play
    private static readonly List<ApPanelBase> panels = new();

    private static bool loggedInputFailure;
    private static bool loggedTickFailure;

    protected ApPanelBase()
    {
        panels.Add(this);
    }

    internal void Toggle()
    {
        if (IsOpen)
        {
            Close("toggled off");
        }
        else
        {
            Open();
        }
    }

    internal void Open()
    {
        if (IsOpen)
        {
            Log.LogInfo($"[AP] The {Title} panel is already open.");
            return;
        }

        // Refusing beats opening blind. With no live host there is no OnGUI call at all, and this
        // path used to freeze the player and suspend the menus anyway, silently (BUG-013)
        if (host == null)
        {
            Log.LogWarning($"[AP] The {Title} panel has no OnGUI host, so it cannot be shown. "
                + "The mod's own GameObject is gone or was never set up.");
            return;
        }

        IsOpen = true;
        host.enabled = true;

        hasRepainted = false;
        drawCalls = 0;
        repaintCalls = 0;
        drawFailed = false;
        loggedScaleFallback = false;

        OnOpened();

        // The screen size is in here because a zero height scales the window to nothing while
        // everything else looks healthy, and the old code logged nothing on any path
        Log.LogInfo($"[AP] Opened the {Title} panel. Screen {Screen.width}x{Screen.height}, "
            + $"scale {CurrentScale():0.000}. Escape or the pause button closes it.");
    }

    internal void Close(string reason = "requested")
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        if (host != null)
        {
            host.enabled = false;
        }

        UnfreezePlayer();
        RestoreGameUi();

        Log.LogInfo($"[AP] Closed the {Title} panel ({reason}). Drew {drawCalls} times, "
            + $"repainted {repaintCalls} times.");
    }

    // Work that should happen once per opening rather than per frame, such as rebuilding a
    // cached view of game state. Does nothing by default
    protected virtual void OnOpened()
    {
    }

    // Attaches this panel to the ApPanelHost whose OnGUI draws it. The host is disabled whenever
    // the panel is closed, so IMGUI dispatch costs nothing while it isn't up
    internal void AttachTo(ApPanelHost host)
    {
        this.host = host;
        host.Panel = this;
        host.enabled = IsOpen;
    }

    // Driven from ArchipelagoPlugin.Update rather than from the host, because the host is the one
    // component that might be the reason nothing is drawing. A way out of a panel that cannot draw
    // must not run through the thing that cannot draw
    internal static void TickAll()
    {
        for (var i = 0; i < panels.Count; i++)
        {
            try
            {
                panels[i].Tick();
            }
            catch (Exception e)
            {
                // Shares the plugin's only Update with draining the client's queues, so a panel
                // must not be able to stop that. Logged once, or a persistent fault would bury
                // the rest of the log
                if (!loggedTickFailure)
                {
                    loggedTickFailure = true;
                    Log.LogError("[AP] A panel's per-frame check threw, so its escape key is "
                        + $"unreliable for this session: {e}");
                }
            }
        }
    }

    // The escape hatch. Everything in here has to work with the window invisible
    private void Tick()
    {
        if (!IsOpen)
        {
            return;
        }

        // A panel that threw before it ever painted is the BUG-013 shape, so it goes. One that has
        // painted is on screen with its own Close button, and pulling it out from under the player
        // over a single bad frame would be the worse trade
        if (drawFailed && !hasRepainted)
        {
            Close("it threw before it ever drew");
            return;
        }

        if (ClosePressed())
        {
            Close("Escape or the pause button");
        }
    }

    // Escape, plus the pause button so a controller player is not stuck behind a window they have
    // no pointer for. Once the game's UI input is suspended they cannot reach the drawn Close button
    // either, so that half is not a nicety.
    //
    // Read straight from Rewired rather than through InputManager.Gameplay, whose Instance getter
    // builds an InputManager and re-inits Steam input when the game has not
    // (Singleton.cs:9-18, InputManager.cs:47-54). A way out of a softlock must not create one
    private static bool ClosePressed()
    {
        try
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                return true;
            }

            if (!Rewired.ReInput.isReady)
            {
                return false;
            }

            // Straight to Rewired rather than through Unify's RewiredInputManager, which is only a
            // wrapper around this same call (RewiredInputManager.cs:30) and drags in an assembly
            // the plugin does not reference.
            //
            // Same sweep InputSource.GetButtonDown does for a null player (InputSource.cs:89-104)
            for (var i = 0; i < 4; i++)
            {
                var player = Rewired.ReInput.players.GetPlayer(i);
                if (player != null && player.GetButtonDown(RewiredConsts.Action.Pause))
                {
                    return true;
                }
            }
        }
        catch (Exception e)
        {
            if (!loggedInputFailure)
            {
                loggedInputFailure = true;
                Log.LogWarning("[AP] Could not read the close keys, so the panel's keyboard and "
                    + $"pause-button escapes are off for this session: {e.Message}");
            }
        }

        return false;
    }

    // Called from the host's OnGUI, which runs it more than once a frame: a layout pass, one pass
    // per input event, and a repaint
    internal void Draw()
    {
        if (!IsOpen)
        {
            return;
        }

        drawCalls++;

        var scale = CurrentScale();
        var previousMatrix = GUI.matrix;

        // Scaling the whole matrix rather than each font size keeps hit-testing correct. Mouse
        // positions run through the same transform the drawing does.
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

        // Only catches GUILayout.Window itself. IMGUI runs DrawContents after this returns, so a
        // throw in the contents is logged by Unity instead, and Escape or pause is the way out.
        try
        {
            window = GUILayout.Window(WindowId, window, DrawContents, Title);
        }
        catch (ExitGUIException)
        {
            // IMGUI's own control flow rather than a failure, and swallowing it corrupts the pass
            // it is unwinding
            GUI.matrix = previousMatrix;
            throw;
        }
        catch (Exception e)
        {
            // Tick does the closing, so a GUI pass that is already in trouble is not also tearing
            // state down underneath itself
            if (!drawFailed)
            {
                drawFailed = true;
                Log.LogError($"[AP] The {Title} panel threw while drawing: {e}");
            }

            GUI.matrix = previousMatrix;
            return;
        }

        GUI.matrix = previousMatrix;

        // Only a repaint proves the window reached the screen, and the freeze and the suspend are
        // exactly what makes a window that never reaches it unrecoverable (BUG-013)
        if (Event.current == null || Event.current.type != EventType.Repaint)
        {
            return;
        }

        repaintCalls++;

        if (hasRepainted)
        {
            return;
        }

        hasRepainted = true;

        // The line whose absence is the diagnosis: present means the panel really is on screen
        Log.LogInfo($"[AP] The {Title} panel painted its first frame, so the player is frozen and "
            + "the game's menus are inert until it closes.");

        FreezePlayer();
        SuspendGameUi();
    }

    // The window is authored at DesignHeight and scaled to whatever the screen is. A non-positive
    // height would scale it to nothing, which is visible side effects with an invisible window,
    // the exact shape BUG-013 was reported as, so that falls back to 1:1 and says so
    private float CurrentScale()
    {
        var height = Screen.height;
        if (height > 0)
        {
            return height / DesignHeight;
        }

        if (!loggedScaleFallback)
        {
            loggedScaleFallback = true;
            Log.LogWarning($"[AP] Screen.height is {height}, so the {Title} panel is drawing "
                + "unscaled. It will be the wrong size rather than invisible.");
        }

        return 1f;
    }

    // Connecting replays item history into save state and unlocks regions through DataManager,
    // and the upgrade trees only exist once a save is up. At the main menu neither works
    protected static bool HasLoadedSave() =>
        DataManager.Instance != null && PlayerFarming.Instance != null;

    // Without this, entering a slot name walks the player across the room and can trigger
    // interactions. Applied by the first repaint rather than by Open, because the state it writes
    // has no timeout of its own (PlayerFarming.cs:757-771) and nothing in the game clears it
    private void FreezePlayer()
    {
        if (PlayerFarming.Instance == null)
        {
            return;
        }

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
        if (!frozePlayer)
        {
            return;
        }

        frozePlayer = false;

        if (PlayerFarming.Instance == null)
        {
            return;
        }

        try
        {
            PlayerFarming.SetStateForAllPlayers(StateMachine.State.Idle, false, null);
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Could not restore the player after the panel: {e.Message}");
        }
    }

    // IMGUI and the game's UI take input through separate paths, so a click inside this window
    // also lands on whatever menu button sits behind it, and the panel is usually opened from
    // the pause menu. Switching its input modules off makes the menu behind inert and stops arrow
    // keys walking its selection while you type.
    //
    // The modules rather than the EventSystem itself, because a disabled EventSystem makes
    // EventSystem.current null and the game reads that unguarded every frame (ForceSelection.cs:13,
    // :24, :33). A disabled module drops out of the EventSystem's list, so input stops all the same.
    private void SuspendGameUi()
    {
        var eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            return;
        }

        foreach (var module in eventSystem.GetComponents<BaseInputModule>())
        {
            if (module.enabled)
            {
                module.enabled = false;
                suspendedModules.Add(module);
            }
        }
    }

    private void RestoreGameUi()
    {
        foreach (var module in suspendedModules)
        {
            // Gone if the scene changed underneath the panel.
            if (module != null)
            {
                module.enabled = true;
            }
        }

        suspendedModules.Clear();
    }
}
