using UnityEngine;

namespace Archipelago.CultOfTheLamb.UI;

/// <summary>
/// Owns one panel's OnGUI callback, and nothing else
/// </summary>
/// <remarks>
/// It exists so it can be turned off. Having an OnGUI method at all makes Unity call it several
/// times a frame, even if it returns straight away. A disabled component gets no calls, so a
/// closed panel costs nothing.
///
/// One per panel rather than shared, so each panel can be turned off on its own.
/// </remarks>
internal class ApPanelHost : MonoBehaviour
{
    internal ApPanelBase Panel;

    private void OnGUI()
    {
        Panel?.Draw();
    }
}
