namespace Archipelago.CultOfTheLamb;

/// <summary>Which save is loaded, as a stable id. Every managed collection keys its debt by it.</summary>
internal static class SaveSlot
{
    /// <summary>
    /// The loaded save, with the Woolhaven variant folded onto its base slot.
    ///
    /// SaveAndLoad.SAVE_SLOT isn't stable within a session - the game keeps a DLC save at
    /// slot+10 and moves SAVE_SLOT between the two while writing (SaveAndLoad.cs:307, :183).
    /// Comparing the raw value would read that as the player loading a different save.
    /// </summary>
    internal static int Current =>
        SaveAndLoad.SAVE_SLOT >= 10 ? SaveAndLoad.SAVE_SLOT - 10 : SaveAndLoad.SAVE_SLOT;
}
