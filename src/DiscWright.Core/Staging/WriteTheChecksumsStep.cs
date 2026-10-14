namespace DiscWright.Core.Staging;

/// <summary>
/// Hashes everything on the disc and writes the list it carries.
/// </summary>
/// <remarks>
/// <para>
/// It takes its own dependencies rather than finding them on the
/// <see cref="BuildContext"/>, which is what stops that type growing a field
/// for every tool any step might want. A step is an object; it can hold what it
/// needs.
/// </para>
/// <para>
/// <strong>It hashes the staged files, not the sources.</strong> The question
/// this list answers is "did that burn come out right", asked by somebody with
/// a disc in their hand, so it has to describe the bytes that are actually on
/// the disc. That is also why it must run after the copy step: run before, it
/// would hash an empty folder and still look like it worked.
/// </para>
/// </remarks>
public sealed class WriteTheChecksumsStep(IFileSystem files, IHasher hasher) : IBuildStep
{
    public string Says => "checking every file";

    public bool AppliesTo(BuildContext context) => context.Project.Options.Checksums;

    public Task Run(BuildContext context, CancellationToken ct)
    {
        List<ChecksumEntry> entries = [];

        foreach (ListedFile file in files.List(context.StageFolder).Files)
        {
            ct.ThrowIfCancellationRequested();

            // The list itself is not in its own list. It cannot be: its hash
            // would have to be written into it.
            if (string.Equals(file.RelativePath, ChecksumList.FileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entries.Add(new ChecksumEntry(
                file.RelativePath,
                hasher.Sha256(context.On(file.RelativePath), ct),
                file.Bytes));
        }

        context.Writer.WriteText(
            context.On(ChecksumList.FileName),
            ChecksumList.For(context.Project.Label, entries));

        return Task.CompletedTask;
    }
}
