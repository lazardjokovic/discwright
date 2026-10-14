namespace DiscWright.Core.Staging;

/// <summary>
/// One thing that happens while a disc is being made.
/// </summary>
/// <remarks>
/// A step rather than a method on a builder, for one reason that pays for the
/// whole arrangement: <strong>the pipeline knows how many steps apply before it
/// starts any of them</strong>. So progress is "step 4 of 7, writing the menu",
/// with nobody estimating anything, which is the exact complaint about the
/// install window having a timer and no bar.
/// </remarks>
public interface IBuildStep
{
    /// <summary>
    /// What it says while it runs. "Writing the menu".
    /// </summary>
    /// <remarks>
    /// One string, used for the progress line and the log line both, so the two
    /// cannot drift apart into saying different things about the same moment.
    /// </remarks>
    string Says { get; }

    /// <summary>
    /// Whether this step has anything to do for this disc.
    /// </summary>
    /// <remarks>
    /// Asked once, up front, for every step, before any of them run. A step
    /// that does not apply is never counted and never shown, so the numbers a
    /// person sees are the work that is actually going to happen.
    /// </remarks>
    bool AppliesTo(BuildContext context);

    /// <summary>Does it.</summary>
    Task Run(BuildContext context, CancellationToken ct);
}
