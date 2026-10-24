using System.Collections.Concurrent;

namespace DiscWright.Tests;

/// <summary>
/// One thread that delivers everything posted to it, in the order it was
/// posted, the way the thread a window is drawn on does.
/// </summary>
/// <remarks>
/// <para>
/// <c>Progress&lt;T&gt;</c> posts each report to the thread that started the
/// work when that thread has a context, which in the app is the thread the
/// window is drawn on, so the last report of a check always lands before the
/// work's own ending does, and a report never lands on top of a line the
/// window has just said. A test thread has no context, so the reports went to
/// the pool, where the last one could land after the work had finished and be
/// dropped as a report about nothing, or land between a line being set and a
/// listener reading it. Both happened: once in six runs of the suite on
/// 2026-10-24 the watched burn never heard "checking the disc, 2 of 2", and
/// the watched erase never heard "D: is blank again".
/// </para>
/// <para>
/// Installed only around the work being watched. Everything the work awaits
/// comes back through here, and every report it makes is posted here, so
/// what a test hears is what a window would hear, in the order a window would
/// hear it. A fake that has to wait for its report to be heard waits through
/// <see cref="WaitUntil"/>, which delivers while it waits, because it is
/// waiting on the very thread that delivers.
/// </para>
/// </remarks>
internal sealed class OnTheWindowsThread : SynchronizationContext
{
    private readonly BlockingCollection<(SendOrPostCallback What, object? With)> _posted = [];

    private OnTheWindowsThread()
    {
    }

    public override void Post(SendOrPostCallback d, object? state) => _posted.Add((d, state));

    public override void Send(SendOrPostCallback d, object? state) => d(state);

    /// <summary>
    /// Starts the work with a window's kind of thread as its context, and
    /// delivers to it until the work is done.
    /// </summary>
    internal static Task Run(Func<Task> start)
    {
        OnTheWindowsThread thread = new();
        SynchronizationContext? was = Current;
        SetSynchronizationContext(thread);

        try
        {
            Task work = start();
            thread.PumpUntil(() => work.IsCompleted);

            return work;
        }
        finally
        {
            SetSynchronizationContext(was);
        }
    }

    /// <summary>
    /// Waits for a condition, delivering the window's reports meanwhile when
    /// this is the thread that delivers them, and merely waiting when it is
    /// not.
    /// </summary>
    /// <remarks>
    /// For a fake that reports and then has to see its report heard before it
    /// goes on, because the real thing takes minutes and the fake takes no
    /// time at all. Under <see cref="Run"/> the fake runs on the delivering
    /// thread, and waiting without delivering would wait for ever.
    /// </remarks>
    internal static bool WaitUntil(Func<bool> heard, TimeSpan? atMost = null)
    {
        DateTime until = DateTime.UtcNow + (atMost ?? TimeSpan.FromSeconds(10));

        if (Current is OnTheWindowsThread delivering)
        {
            delivering.PumpUntil(() => heard() || DateTime.UtcNow >= until);
        }
        else
        {
            while (!heard() && DateTime.UtcNow < until)
            {
                Thread.Sleep(1);
            }
        }

        return heard();
    }

    /// <summary>Delivers what has been posted until the condition holds, and then whatever is left.</summary>
    private void PumpUntil(Func<bool> done)
    {
        while (!done())
        {
            if (_posted.TryTake(out (SendOrPostCallback What, object? With) next, 10))
            {
                next.What(next.With);
            }
        }

        while (_posted.TryTake(out (SendOrPostCallback What, object? With) left))
        {
            left.What(left.With);
        }
    }
}
