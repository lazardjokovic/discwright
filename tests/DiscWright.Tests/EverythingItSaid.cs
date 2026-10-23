namespace DiscWright.Tests;

/// <summary>
/// A listener that hears every report, on the thread the report was made on.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Not <c>Progress&lt;T&gt;</c>, and that is the whole point.</strong>
/// <c>Progress&lt;T&gt;</c> hands each report to the thread pool, which is
/// exactly right for the window, because its reports have to arrive on the
/// thread the window is drawn on. In a test it means the last report can still
/// be in flight when the work itself has finished, so a test that counts what
/// was said is a test that sometimes counts one too few.
/// </para>
/// <para>
/// It went off here on 2026-10-23: the whole suite running at once,
/// <c>Says_which_file_it_is_on</c> heard two of three lines and failed, and
/// the same test passed on its own and twice more afterwards. One of the three
/// tests that count reports had already grown a wait loop with the cause
/// written beside it, which is a workaround for something better removed.
/// </para>
/// <para>
/// Nothing about the strength of those tests changes. Three reports are still
/// three reports, and the last one still has to say the work is finished. They
/// are simply no longer a race.
/// </para>
/// </remarks>
internal sealed class EverythingItSaid<T> : IProgress<T>
{
    /// <summary>What was said, in the order it was said.</summary>
    internal List<T> Heard { get; } = [];

    public void Report(T value) => Heard.Add(value);
}
