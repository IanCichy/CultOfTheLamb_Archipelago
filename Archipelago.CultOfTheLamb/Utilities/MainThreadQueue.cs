using System;
using System.Collections.Generic;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Runs work on Unity's main thread that was handed over from somewhere else.
///
/// The Archipelago client raises its callbacks on the websocket thread. Most of them only
/// touch client state, but teardown reaches into the game's save data - and Unity collections
/// like DataManager.Instance.PlayerFoundTrinkets are plain Lists the main thread iterates,
/// so writing to them from a socket callback can throw mid-enumeration or, worse, quietly
/// undo work the main thread just did.
///
/// ArchipelagoItemLogicController already solves this for item receipt by queueing and
/// draining from Update. This is the same idea for the paths that don't have their own queue.
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
