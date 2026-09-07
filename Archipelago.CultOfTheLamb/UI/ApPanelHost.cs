using UnityEngine;

namespace Archipelago.CultOfTheLamb.UI;

/// <summary>
/// Owns one panel's OnGUI callback, and nothing else.
/// </summary>
/// <remarks>
/// It exists to be disabled. Defining OnGUI switches Unity's IMGUI dispatch on permanently:
/// twice a frame plus once per input event, marshalling Event.current and setting up GUI state
/// each time, with an early-return guard reached rather than avoided. A disabled Behaviour gets
/// no callbacks at all, so the closed cost is zero.
///
/// One instance per panel rather than a shared host, so per-panel disabling still works.
/// </remarks>
internal class ApPanelHost : MonoBehaviour
{
    internal ApPanelBase Panel;

    private void OnGUI()
    {
        Panel?.Draw();
    }
}
