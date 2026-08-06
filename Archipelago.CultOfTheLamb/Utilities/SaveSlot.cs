namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Which save is loaded, as a stable id.
///
/// Extracted from TarotService because it is a fact about the game's saving, not about tarot:
/// every managed collection has to key its debt by save, and all of them need the same answer.
/// </summary>
internal static class SaveSlot
{
    /// <summary>
    /// The loaded save, with the Woolhaven variant folded onto its base slot.
    ///
    /// SaveAndLoad.SAVE_SLOT is not stable within a session. The game keeps a DLC save at
    /// slot+10 beside a base-game backup at slot, and moves SAVE_SLOT between the two while
    /// writing: MakeBaseGameBackUpSave adds 10, saves, and puts it back (SaveAndLoad.cs:307),
    /// while Saving can subtract 10 for good (:183). Comparing the raw value would read those
    /// as the player loading a different save - during which a collection would hand its debt
    /// to the wrong key and clear Archipelago's grants with no item replay left to rebuild them.
    /// </summary>
    internal static int Current =>
        SaveAndLoad.SAVE_SLOT >= 10 ? SaveAndLoad.SAVE_SLOT - 10 : SaveAndLoad.SAVE_SLOT;
}
