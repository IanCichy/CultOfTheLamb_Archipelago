namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Whether this save is still working through the game's opening tutorial.
/// </summary>
/// <remarks>
/// Not just OnboardingFinished - Woolhaven clears that flag for its furnace quest
/// (Onboarding.cs:973), long after the tutorial ended. ShowLoyaltyBars is the latch: both exits
/// set it (Onboarding.cs:356, :699), Quick Start sets it at creation (DataManager.cs:402), and
/// nothing clears it.
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

            return !data.OnboardingFinished && !data.ShowLoyaltyBars;
        }
    }
}
