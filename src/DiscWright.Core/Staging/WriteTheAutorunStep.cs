namespace DiscWright.Core.Staging;

/// <summary>
/// Writes the <c>autorun.inf</c> that gives the disc its name, its icon, and
/// what happens when it goes in.
/// </summary>
/// <remarks>
/// Always, even when there is no menu. Without it the disc is called something
/// like <c>DVD_ROM</c> in This PC, which is what somebody sees first and is the
/// part they will remember in ten years.
/// </remarks>
public sealed class WriteTheAutorunStep : IBuildStep
{
    /// <summary>
    /// The icon file at the disc's root. A fixed name, so autorun.inf can name
    /// it without knowing where the picture came from.
    /// </summary>
    public const string IconFileName = "disc.ico";

    public string Says => "naming the disc";

    /// <summary>
    /// Every disc gets one. There is no option for this and there should not
    /// be: a disc with no name is a disc nobody can identify in a drawer.
    /// </summary>
    public bool AppliesTo(BuildContext context) => true;

    public Task Run(BuildContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        string label = context.IsPartOfASet
            ? $"{context.Project.Label} {context.Disc.Number}"
            : context.Project.Label;

        context.Writer.WriteAutorunText(
            context.On("autorun.inf"),
            AutorunFile.For(label, IconFileName, context.Project.Options.Menu));

        return Task.CompletedTask;
    }
}
