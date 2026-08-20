using UnityEngine;

namespace Archipelago.CultOfTheLamb.UI;

/// <summary>
/// Owns one panel's OnGUI callback, and nothing else. It exists to be *disabled*: defining
/// OnGUI switches Unity's IMGUI dispatch on permanently - twice a frame plus once per input
/// event, marshalling Event.current and setting up GUI state each time - and an early-return
/// guard is reached, not avoided. A disabled Behaviour gets no callbacks at all, so the closed
/// cost is zero.
///
/// One instance per panel rather than one shared host, so that per-panel disabling still works.
/// </summary>
internal class ApPanelHost : MonoBehaviour
{
    internal ApPanelBase Panel;

    private void OnGUI() => Panel?.Draw();
}
