namespace DiscWright.Core.Staging;

/// <summary>
/// Writes <c>discproject.json</c> beside the discs, so what was built can be
/// opened again.
/// </summary>
/// <remarks>
/// <para>
/// Beside the discs and not on them, which is where v1 put it and therefore
/// where "Open one you built" looks. A disc is a copy of a game; the project
/// is the recipe, and a recipe on the disc would be one more file somebody did
/// not ask for on every copy they hand out.
/// </para>
/// <para>
/// Once per build rather than once per disc. A set is one project, and writing
/// it again for disc 2 would say the same thing a second time, over a file
/// that is already right.
/// </para>
/// </remarks>
public sealed class WriteTheProjectFileStep(
    string madeBy, string mediaKey, string outputFolder, Func<DateTimeOffset> now)
    : IBuildStep
{
    /// <inheritdoc/>
    public string Says => "writing the project file";

    /// <inheritdoc/>
    /// <remarks>
    /// Only while making the first disc. Every disc of a set is built from the
    /// same project, so the others have nothing to add.
    /// </remarks>
    public bool AppliesTo(BuildContext context) =>
        context is not null && context.Disc.Number == 1 && outputFolder.Length > 0;

    /// <inheritdoc/>
    public Task Run(BuildContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Writer.WriteText(
            System.IO.Path.Combine(outputFolder, TheProjectAsJson.FileName),
            TheProjectAsJson.Of(context.Project, madeBy, mediaKey, outputFolder, now()));

        return Task.CompletedTask;
    }
}
