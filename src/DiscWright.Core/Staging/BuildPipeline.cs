namespace DiscWright.Core.Staging;

/// <summary>
/// Runs the steps that apply, in order, and says where it has got to.
/// </summary>
/// <remarks>
/// The order is data, handed in, and it matters: the checksum step hashes what
/// the copy step wrote, so a pipeline that ran them the other way round would
/// produce a list of hashes for an empty folder and still look like it worked.
/// </remarks>
public sealed class BuildPipeline(IReadOnlyList<IBuildStep> steps)
{
    public async Task Run(BuildContext context, IProgress<BuildProgress>? progress, CancellationToken ct)
    {
        // Worked out before anything runs, which is the whole point of steps.
        // A step with nothing to do is never counted and never shown, so the
        // numbers somebody watches are the work that is actually happening.
        List<IBuildStep> applying = [.. steps.Where(step => step.AppliesTo(context))];

        for (int at = 0; at < applying.Count; at++)
        {
            ct.ThrowIfCancellationRequested();

            IBuildStep step = applying[at];

            // Reported before it runs, not after. A step that takes a minute
            // would otherwise sit at the previous step's wording for the whole
            // minute and then jump two at once.
            progress?.Report(new BuildProgress(
                at + 1, applying.Count, step.Says, context.Disc.Number, context.Layout.Discs.Count));

            await step.Run(context, ct);
        }
    }

    /// <summary>
    /// What would happen, without doing any of it. For the line that says what
    /// a build is about to do before anybody presses the button.
    /// </summary>
    public IReadOnlyList<string> WouldDo(BuildContext context) =>
        [.. steps.Where(step => step.AppliesTo(context)).Select(step => step.Says)];
}

/// <summary>Where a build has got to.</summary>
/// <param name="Step">From one.</param>
/// <param name="OfSteps">How many are going to run, known before the first one does.</param>
/// <param name="Says">What is happening, in the step's own words.</param>
/// <param name="Disc">Which disc is being made, from one.</param>
/// <param name="OfDiscs">How many there are. One for a disc on its own.</param>
public sealed record BuildProgress(int Step, int OfSteps, string Says, int Disc = 1, int OfDiscs = 1)
{
    /// <summary>
    /// "step 4 of 7, writing the menu", and for a set, "disc 2 of 3, step 4 of
    /// 7, writing the menu".
    /// </summary>
    /// <remarks>
    /// The disc is only mentioned when there is more than one. Saying "disc 1
    /// of 1" to somebody making one disc is noise, and noise in a progress line
    /// is what makes people stop reading it.
    /// </remarks>
    public override string ToString() =>
        OfDiscs > 1
            ? $"disc {Disc} of {OfDiscs}, step {Step} of {OfSteps}, {Says}"
            : $"step {Step} of {OfSteps}, {Says}";
}
