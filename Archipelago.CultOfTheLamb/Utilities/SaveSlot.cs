namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Which save is loaded, as a stable id.
/// </summary>
/// <remarks>Every managed collection keys its debt by it.</remarks>
internal static class SaveSlot
{
    // The loaded save, with the Woolhaven variant folded onto its base slot.
    //
    // SaveAndLoad.SAVE_SLOT isn't stable within a session. The game keeps a DLC save at slot+10
    // and moves SAVE_SLOT between the two while writing (SaveAndLoad.cs:307, :183). Comparing the
    // raw value would read that as the player loading a different save
    internal static int Current =>
        SaveAndLoad.SAVE_SLOT >= 10 ? SaveAndLoad.SAVE_SLOT - 10 : SaveAndLoad.SAVE_SLOT;

    // Whether there is a real save to write into. DataManager.Instance is no help, since its
    // getter builds a blank one on demand and so is never null. SaveAndLoad.Loaded is no help on
    // its own either, because nothing ever sets it back to false.
    internal static bool IsLoaded => SaveAndLoad.Loaded && PlayerFarming.Instance != null;
}
