namespace DiscWright.Core;

/// <summary>
/// Something that does long work for a build and can be told when to give it
/// up: the writer, in the middle of copying one file of several gigabytes.
/// </summary>
/// <remarks>
/// <para>
/// A build checked whether it had been stopped between one file and the next,
/// and never during one. That was enough while nothing could stop a build.
/// With a Stop button it is not: a game's installer comes in parts of four
/// gigabytes, and a press of Stop would be answered minutes later, when the
/// part in flight had finished, by a window that had looked for all that time
/// as if it had not heard.
/// </para>
/// <para>
/// Told rather than handed with every copy, so the fifty places that copy a
/// file are as they were, and so are the writers that only pretend to.
/// </para>
/// </remarks>
public interface IListenForAStop
{
    /// <summary>
    /// What to watch from now on. Given nothing to watch, it stops watching:
    /// a writer is kept between builds, and the one after a stopped build
    /// must not be stopped by the same press.
    /// </summary>
    void StopWhen(CancellationToken stopped);
}
