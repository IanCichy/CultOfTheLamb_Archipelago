using Archipelago.MultiClient.Net;

namespace Archipelago.CultOfTheLamb.Services;

// Sends Snail Shrine N once N shrines have a Shell, in any order. Logic can't see which hub holds
// which shrine, only how many are reachable, so the checks follow the count.
//
// The game records them as five save booleans, ShellsGifted_0 to _4, with no event, so this polls
// them. Being save state, that also catches offerings made while disconnected.
internal class SnailShrineService : IService
{
    private readonly ArchipelagoSession session;
    private readonly long locationBaseId;
    private readonly int locationCount;

    // Shrines already reported, so a steady state poll stays silent
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
        for (var i = 0; i < sent.Length; i++)
        {
            sent[i] = false;
        }
    }

    internal void Tick()
    {
        var dataManager = DataManager.Instance;
        if (dataManager == null)
        {
            return;
        }

        var lit = 0;
        for (var i = 0; i < 5; i++)
        {
            if (IsGifted(dataManager, i))
            {
                lit++;
            }
        }

        for (var i = 0; i < lit && i < locationCount && i < sent.Length; i++)
        {
            if (sent[i])
            {
                continue;
            }

            sent[i] = true;
            var checkId = locationBaseId + i;
            Log.LogInfo($"[AP] {lit} Snail Shrine(s) lit, sending Snail Shrine {i + 1} ({checkId})");
            CheckSender.Send(session, checkId);
        }
    }

    // Five separately-named booleans rather than an array, so this is an explicit switch
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
