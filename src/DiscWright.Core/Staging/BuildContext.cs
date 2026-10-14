namespace DiscWright.Core.Staging;

/// <summary>
/// Everything a step is given, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// This is the type the plan warned about, the one that decides whether the
/// step arrangement pays off or quietly becomes a god object handed to
/// everything. Five fields, each of which some step genuinely needs.
/// </para>
/// <para>
/// <strong>What is deliberately not here: progress and logging.</strong> They
/// belong to the pipeline, which is the only thing that knows how many steps
/// there are and which one is running. A step that could report its own
/// progress would have to know its own number, and then every step would need
/// telling where it sits in a list it is not part of.
/// </para>
/// <para>
/// It describes <em>one disc</em>. A set runs the pipeline once per disc with a
/// different <see cref="Disc"/> and <see cref="StageFolder"/> each time, which
/// is what keeps "copy the files" and "write the set manifest" from having to
/// be the same shape.
/// </para>
/// </remarks>
/// <param name="Project">What the person asked for.</param>
/// <param name="Layout">All the discs, so a step can say "2 of 3".</param>
/// <param name="Disc">The one being made now.</param>
/// <param name="StageFolder">The folder that becomes this disc.</param>
/// <param name="Writer">The only way anything here reaches a disk.</param>
public sealed record BuildContext(
    DiscProject Project,
    DiscLayout Layout,
    PlannedDisc Disc,
    string StageFolder,
    IStageWriter Writer)
{
    /// <summary>Whether this disc is one of several.</summary>
    public bool IsPartOfASet => Layout.Discs.Count > 1;

    /// <summary>Where a file on this disc goes, as a full path.</summary>
    public string On(string relativePath) =>
        System.IO.Path.Combine(StageFolder, relativePath);
}
