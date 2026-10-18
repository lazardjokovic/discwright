namespace DiscWright.Core;

/// <summary>
/// Which folders this program is allowed to delete the contents of.
/// </summary>
/// <remarks>
/// <para>
/// Emptying a folder is the only destructive thing this program does, and the
/// rule about which folders it may be done to is the guard on it. The rule
/// lives here, rather than in the one class that enforces it, because the
/// fake used in tests has to obey the same rule as the real writer.
/// </para>
/// <para>
/// <strong>It did not, and that is why this exists.</strong> A preview folder
/// was given a project's name, the real writer refused to empty anything not
/// called <c>disc</c>, and the preview wrote nothing at all. Eighteen tests
/// passed through it without complaint, because the fake emptied whatever it
/// was handed. The fake was kinder than the real thing, so the tests agreed
/// with a program that did not work.
/// </para>
/// </remarks>
public static class WhatMayBeEmptied
{
    /// <summary>
    /// Exactly what the builder calls a staging folder, and nothing else.
    /// </summary>
    /// <remarks>
    /// It used to be anything starting with "disc", which is looser than it
    /// reads. "DiscWright preview" starts with disc, and so would somebody
    /// else's "Discography": the rule said staging folder and meant any
    /// folder whose name happened to begin that way. DiscBuilder names them
    /// "disc" or "disc D1", so that is what this says.
    /// </remarks>
    private static readonly System.Text.RegularExpressions.Regex AStagingFolder =
        new("^disc( D[0-9]+)?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
                | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Whether this folder is one of ours to empty.
    /// </summary>
    /// <param name="path">The folder in question.</param>
    /// <param name="temporaryFolder">
    /// Where this machine puts temporary files, handed in rather than asked
    /// for, so the rule can be tested without depending on the machine
    /// running the test.
    /// </param>
    public static bool Allows(string path, string temporaryFolder)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        System.IO.DirectoryInfo here = new(Tidied(path));

        return AStagingFolder.IsMatch(here.Name) || IsAPreview(here, temporaryFolder);
    }

    /// <summary>
    /// A folder a preview is written into, known by where it sits rather than
    /// by what it is called.
    /// </summary>
    /// <remarks>
    /// By where it sits, because what it is called is the project's name and
    /// could be anything at all. It has to be directly inside our own folder,
    /// directly inside the temporary folder: a folder of the same name
    /// somebody keeps elsewhere is not ours to empty.
    /// </remarks>
    private static bool IsAPreview(System.IO.DirectoryInfo here, string temporaryFolder) =>
        here.Parent is System.IO.DirectoryInfo ours
        && string.Equals(
            ours.Name, WhereAPreviewGoes.TheFolderTheyAllGoIn, StringComparison.OrdinalIgnoreCase)
        && ours.Parent is System.IO.DirectoryInfo above
        && string.Equals(
            Tidied(above.FullName), Tidied(temporaryFolder), StringComparison.OrdinalIgnoreCase);

    private static string Tidied(string path) =>
        path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
}
