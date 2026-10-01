namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Whether this save is still working through the game's opening tutorial.
/// </summary>
/// <remarks>
/// Not just OnboardingFinished. Woolhaven clears that flag for its furnace quest
/// (Onboarding.cs:973), so a Quick Start save would read as mid-tutorial until the furnace is
/// built. QuickStartActive is saved with the game and rules that case out.
/// </remarks>
internal static class TutorialState
{
    internal static bool InTutorial
    {
        get
        {
            var data = DataManager.Instance;

            // At the main menu the instance is a blank one the getter built on demand, which
            // would read as mid-tutorial
            if (data == null || !SaveAndLoad.Loaded)
            {
                return false;
            }

            return !data.OnboardingFinished && !data.QuickStartActive;
        }
    }
}
