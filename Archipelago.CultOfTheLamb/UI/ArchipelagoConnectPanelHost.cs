using UnityEngine;

namespace Archipelago.CultOfTheLamb.UI;

/// <summary>
/// Owns the panel's OnGUI callback, and nothing else.
///
/// It exists to be *disabled*. Defining OnGUI anywhere switches Unity's IMGUI dispatch on for
/// that component permanently: Unity calls it at least twice a frame (Layout and Repaint) plus
/// once per queued input event, marshalling Event.current from native and setting up GUI skin,
/// matrix and depth state each time. An `if (!IsOpen) return;` guard is *reached*, not avoided,
/// so hosting this on the plugin - which is DontDestroyOnLoad - paid that cost from launch to
/// quit for a panel that's open maybe twenty seconds a session.
///
/// A disabled Behaviour receives no callbacks at all, so the closed cost is exactly zero.
/// </summary>
internal class ArchipelagoConnectPanelHost : MonoBehaviour
{
    internal ArchipelagoConnectPanel Panel;

    private void OnGUI() => Panel?.Draw();
}
