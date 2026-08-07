using Archipelago.MultiClient.Net;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Sends a check for each Snail Shrine given a Shell offering.
///
/// The game records these as five plain save booleans, DataManager.ShellsGifted_0 through _4.
/// No event and no single write site worth patching, so this polls them - which is the better
/// shape anyway: they're save state, so polling also catches offerings made while disconnected
/// and re-derives correctly after a reload.
/// </summary>
internal class SnailShrineService : IService
{
    private readonly ArchipelagoSession session;
    private readonly long locationBaseId;
    private readonly int locationCount;

    /// <summary>Shrines already reported, so a steady state poll stays silent.</summary>
    private readonly bool[] sent;

    internal SnailShrineService(ArchipelagoSession session, long locationBaseId, int locationCount)
    {
        this.session = session;
        this.locationBaseId = locationBaseId;
        this.locationCount = locationCount;
        sent = new bool[locationCount];
    }

    public void Register()
    {
        Tick();
        Log.LogInfo($"[AP] Snail shrine checks active: {locationCount} location(s) "
            + $"from id {locationBaseId}.");
    }

    public void Unregister()
    {
        for (var i = 0; i < sent.Length; i++) sent[i] = false;
    }

    /// <summary>Called on a throttle from the plugin's Update.</summary>
    internal void Tick()
    {
        var dataManager = DataManager.Instance;
        if (dataManager == null) return;

        for (var i = 0; i < locationCount && i < sent.Length; i++)
        {
            if (sent[i] || !IsGifted(dataManager, i)) continue;

            sent[i] = true;
            var checkId = locationBaseId + i;
            Log.LogInfo($"[AP] Snail Shrine {i + 1} lit, sending check {checkId}");
            CheckSender.Send(session, checkId);
        }
    }

    /// <summary>
    /// Five separately-named booleans rather than an array, so this is an explicit switch.
    /// </summary>
    private static bool IsGifted(DataManager dataManager, int index)
    {
        switch (index)
        {
            case 0: return dataManager.ShellsGifted_0;
            case 1: return dataManager.ShellsGifted_1;
            case 2: return dataManager.ShellsGifted_2;
            case 3: return dataManager.ShellsGifted_3;
            case 4: return dataManager.ShellsGifted_4;
            default: return false;
        }
    }
}
