namespace DiscWright.Core.Staging;

/// <summary>
/// Puts this disc's files into the folder that becomes it.
/// </summary>
/// <remarks>
/// The one step that moves real weight. Everything else on a disc is a few
/// kilobytes of text and pictures; this is the twenty gigabytes.
/// </remarks>
public sealed class CopyTheFilesStep : IBuildStep
{
    public string Says => "copying the files";

    /// <summary>
    /// A disc with no files of its own is not a mistake: in a set, the manual
    /// and the extras can need a disc to themselves, and that disc still gets a
    /// menu and an autorun.
    /// </summary>
    public bool AppliesTo(BuildContext context) => context.Disc.Files.Count > 0;

    public Task Run(BuildContext context, CancellationToken ct)
    {
        foreach (PlannedFile file in context.Disc.Files)
        {
            ct.ThrowIfCancellationRequested();

            string destination = context.On(file.RelativePath);

            // A GOG download has no shape to keep, but a folder of game files
            // does, so a file three folders deep needs those folders made.
            string? folder = System.IO.Path.GetDirectoryName(destination);

            if (!string.IsNullOrEmpty(folder))
            {
                context.Writer.MakeFolder(folder);
            }

            if (file.Part is null)
            {
                context.Writer.Copy(file.Source, destination);
            }
            else
            {
                // One piece of a file too big for any single disc. The source is
                // the person's own download and is never touched: the piece is
                // read out of it and written here.
                context.Writer.CopyPart(file.Source, file.Part.Offset, file.Bytes, destination);
            }
        }

        return Task.CompletedTask;
    }
}
