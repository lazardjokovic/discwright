namespace DiscWright.Core;

/// <summary>
/// A build that somebody stopped, and what of its own it could not take away
/// again.
/// </summary>
/// <remarks>
/// <para>
/// A build stopped part way leaves a disc folder that is no disc: some of the
/// files, no menu, no list of what should be there. Left where it is, it looks
/// like one, and the next thing somebody does with it is burn it. So a stopped
/// build deletes what it had started, and says here what it could not.
/// </para>
/// <para>
/// Still a cancellation, so everything that already treats a stop as a stop
/// goes on doing so.
/// </para>
/// </remarks>
public sealed class ABuildThatWasStopped : OperationCanceledException
{
    public ABuildThatWasStopped()
        : this([])
    {
    }

    public ABuildThatWasStopped(string message)
        : base(message)
    {
        LeftBehind = [];
    }

    public ABuildThatWasStopped(string message, Exception inner)
        : base(message, inner)
    {
        LeftBehind = [];
    }

    public ABuildThatWasStopped(IReadOnlyList<string> leftBehind)
        : base("The build was stopped.")
    {
        LeftBehind = leftBehind;
    }

    /// <summary>
    /// The folders this build had started and could not delete, because
    /// something has them open. Empty when it left nothing.
    /// </summary>
    public IReadOnlyList<string> LeftBehind { get; }
}
