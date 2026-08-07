using System;
using System.Collections.Generic;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Runs work on Unity's main thread that was handed over from the websocket thread. Teardown
/// reaches into save data, and collections like PlayerFoundTrinkets are plain Lists the main
/// thread iterates - writing to them from a socket callback can throw mid-enumeration.
///
/// Same idea as ArchipelagoItemLogicController's item queue, for the paths without their own.
/// </summary>
internal static class MainThreadQueue
{
    private static readonly Queue<Action> pending = new();

    /// <summary>Safe to call from any thread.</summary>
    internal static void Enqueue(Action work)
    {
        if (work == null) return;
        lock (pending) pending.Enqueue(work);
    }

    /// <summary>Called each frame from the plugin; does nothing once the queue drains.</summary>
    internal static void Drain()
    {
        while (true)
        {
            Action work;
            lock (pending)
            {
                if (pending.Count == 0) return;
                work = pending.Dequeue();
            }

            try
            {
                work();
            }
            catch (Exception e)
            {
                // Never let one failed item stop the rest of the queue - the work in here is
                // things like handing a player their save data back.
                Log.LogError($"[AP] Queued main-thread work failed: {e}");
            }
        }
    }
}
