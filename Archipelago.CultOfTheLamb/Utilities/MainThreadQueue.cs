using System;
using System.Collections.Generic;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Runs work on Unity's main thread that was handed over from the websocket thread.
/// </summary>
/// <remarks>
/// Teardown reaches into save data, and collections like PlayerFoundTrinkets are plain Lists that
/// the main thread iterates, so writing to them from a socket callback can throw mid-enumeration.
///
/// Same idea as ArchipelagoItemLogicController's item queue, for the paths without their own.
/// </remarks>
internal static class MainThreadQueue
{
    private static readonly Queue<Action> pending = new();

    /// <summary>Queues work for the next drain. Safe to call from any thread.</summary>
    /// <param name="work">The work to run on the main thread. Null is ignored.</param>
    internal static void Enqueue(Action work)
    {
        if (work == null) return;
        lock (pending) pending.Enqueue(work);
    }

    /// <summary>Runs everything queued. Called each frame from the plugin.</summary>
    /// <remarks>Does nothing once the queue is empty.</remarks>
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
                // Never let one failed item stop the rest of the queue. The work in here is
                // things like handing a player their save data back.
                Log.LogError($"[AP] Queued main-thread work failed: {e}");
            }
        }
    }
}
