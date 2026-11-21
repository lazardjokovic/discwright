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

    // Always on a set, asked for or not. The set's own list of every file on
    // every disc is made from these, and without it no disc of the set knows
    // what the others hold: the menu says its list of files is missing and
    // nothing on any of them can be installed. A set built with the option
    // off was exactly that until 2026-11-21.
    public bool AppliesTo(BuildContext context) =>
        context.Project.Options.Checksums || context.Layout.Discs.Count > 1;

    public Task Run(BuildContext context, CancellationToken ct)
    {
        List<ChecksumEntry> entries = [];

        foreach (ListedFile file in files.List(context.StageFolder).Files)
        {
            ct.ThrowIfCancellationRequested();

            // The list itself is not in its own list. It cannot be: its hash
            // would have to be written into it.
            //
            // Nor is the set file, for a related reason: it describes every
            // file on every disc of the set, so it cannot be written until all
            // of them have been hashed, which is after this. Both are the
            // disc's own bookkeeping about the disc, and the set file carries
            // its own identity in the set id inside it.
            if (string.Equals(file.RelativePath, ChecksumList.FileName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(file.RelativePath, SetManifest.FileName, StringComparison.OrdinalIgnoreCase))
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
