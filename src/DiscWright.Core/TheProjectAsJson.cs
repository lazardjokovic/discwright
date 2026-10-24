using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DiscWright.Core;

/// <summary>
/// Writes <c>discproject.json</c>, the file that lets a disc be opened again.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AProjectFile"/> has read these since the rewrite began and
/// nothing has ever written one, so <strong>every disc 2.0 has built is one
/// nothing can reopen</strong>. The promise in that file, that somebody who
/// finds a disc in a drawer can open it, change one thing and build it again,
/// was half kept: the reading half.
/// </para>
/// <para>
/// Written in the shape version 14 of v1 wrote, because that is the shape the
/// reader understands and the one every existing disc carries. A field the
/// reader does not read is not written, and a field it reads is written even
/// when it carries a default, so that what comes back is what went in rather
/// than what a missing field happens to mean.
/// </para>
/// <para>
/// <c>SourceFolder</c> and <c>GameName</c> are written at the top level as
/// well, pointing at the first game. Nothing here reads them. Version 1 knew
/// about exactly one game and stored it there, and v1 went on writing them so
/// that a project saved by a later build still opened in an earlier one. Same
/// courtesy, same two lines.
/// </para>
/// </remarks>
public static class TheProjectAsJson
{
    /// <summary>The name every disc's project file has.</summary>
    public const string FileName = "discproject.json";

    /// <summary>
    /// The version of the format this writes, which is the newest the reader
    /// knows about.
    /// </summary>
    public const int Version = 14;

    /// <summary>Turns a project into the file that describes it.</summary>
    /// <param name="project">What was built.</param>
    /// <param name="madeBy">Which build of DiscWright wrote it.</param>
    /// <param name="mediaKey">
    /// The disc size that was aimed at, or empty for whatever fits.
    /// </param>
    /// <param name="outputFolder">Where the discs were built.</param>
    /// <param name="savedUtc">When, so that a file says when it was written.</param>
    public static string Of(
        DiscProject project,
        string madeBy,
        string mediaKey,
        string outputFolder,
        DateTimeOffset savedUtc)
    {
        ArgumentNullException.ThrowIfNull(project);

        MemoryStream into = new();

        // The relaxed encoder, because the strict one escapes anything that
        // would be unsafe inside HTML. A version like "2.0.0+abc123" comes out
        // as "2.0.0\u002Babc123" and a path with an accent in it becomes
        // unreadable, neither of which any file v1 wrote looks like. Nothing
        // puts this file inside a page, so there is nothing for that escaping
        // to protect.
        JsonWriterOptions how = new()
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        using (Utf8JsonWriter writing = new(into, how))
        {
            writing.WriteStartObject();

            writing.WriteNumber("Version", Version);
            writing.WriteString("AppVersion", madeBy);
            writing.WriteString("SavedUtc", savedUtc.UtcDateTime.ToString("s", CultureInfo.InvariantCulture));

            // Where version 1 kept the only game it knew about.
            GameFolder? first = project.Games.Count > 0 ? project.Games[0] : null;
            WriteOrNull(writing, "SourceFolder", first?.Path);
            WriteOrNull(writing, "GameName", first?.Name);

            writing.WriteStartArray("Games");

            for (int at = 0; at < project.Games.Count; at++)
            {
                GameFolder game = project.Games[at];

                writing.WriteStartObject();
                writing.WriteString("Folder", game.Path);
                writing.WriteString("GameName", game.Name);
                writing.WriteString("MatchName", game.MatchName);
                WriteOrNull(writing, "Setup", TheInstallerPathOf(game));
                writing.WriteString("Kind", "Game");
                // Anything that is not a GOG download is a folder of files,
                // whether or not something in it can be run. Only a folder
                // with something to run was written as Files, so a folder with
                // nothing to run came back as a download and v1 went looking
                // for a setup exe that was never there.
                writing.WriteString("Source", game.Kind == GameKind.GogInstaller ? "GOG" : "Files");
                writing.WriteNumber("Parent", -1);
                writing.WriteNull("Manual");
                writing.WriteNull("Extras");
                writing.WriteEndObject();
            }

            // An add-on is an entry of its own, pointing at the game it
            // belongs to by where that game sits in the list above. By name
            // would bind a patch to the wrong game whenever two folders report
            // the same one, which GOG folders do.
            foreach (AddOn addOn in project.AddOnsOrNone)
            {
                writing.WriteStartObject();
                writing.WriteString("Folder", FolderOf(addOn.Path));
                writing.WriteString("GameName", addOn.Name);
                writing.WriteString("MatchName", addOn.Name);
                writing.WriteString("Setup", addOn.Path);
                writing.WriteString("Kind", "AddOn");
                writing.WriteString("Source", "GOG");
                writing.WriteNumber("Parent", addOn.BelongsTo);
                writing.WriteNull("Manual");
                writing.WriteNull("Extras");
                writing.WriteEndObject();
            }

            writing.WriteEndArray();

            writing.WriteString("Label", project.Label);

            DiscArtwork? art = project.Artwork;
            WriteOrNull(writing, "IconPath", art?.IconPath);
            writing.WriteBoolean("IconIsIco", IsAlreadyAnIcon(art?.IconPath));
            writing.WriteBoolean("Menu", project.Options.Menu);
            WriteOrNull(writing, "BgPath", art?.BackgroundPath);
            writing.WriteBoolean("BgAsIs", art?.BackgroundAsIs ?? false);
            WriteOrNull(writing, "CoverPath", art?.CoverPath);
            WriteOrNull(writing, "DiscArtPath", art?.DiscFacePath);

            DiscExtras? extras = project.Extras;
            WriteOrNull(writing, "MusicFile", extras?.MusicPath);
            WriteOrNull(writing, "ManualPath", extras?.ManualPath);
            WriteOrNull(writing, "ExtrasPath", extras?.ExtrasFolderPath);

            writing.WriteStartArray("ExtraItems");
            foreach (string loose in extras?.Loose ?? [])
            {
                writing.WriteStringValue(loose);
            }

            writing.WriteEndArray();

            writing.WriteString("MediaKey", mediaKey);
            writing.WriteBoolean("LinuxInfo", false);
            writing.WriteBoolean("LegacyFs", project.Options.LegacyFileSystems);
            writing.WriteBoolean("Checksums", project.Options.Checksums);
            // Always true. The option is gone and the behaviour it named is
            // now what always happens, but v1 still reads this field and a
            // file saying false would open there as a disc without artwork.
            writing.WriteBoolean("ArtworkOnDisc", true);
            writing.WriteBoolean("DiscSet", false);
            writing.WriteBoolean("CutFiles", project.Options.CutFiles);
            writing.WriteString("OutDir", outputFolder);

            writing.WriteEndObject();
        }

        return Encoding.UTF8.GetString(into.ToArray());
    }

    private static void WriteOrNull(Utf8JsonWriter writing, string name, string? value)
    {
        if (value is null)
        {
            writing.WriteNull(name);
        }
        else
        {
            writing.WriteString(name, value);
        }
    }

    /// <summary>
    /// The installer as a full path, which is what the reader expects.
    /// </summary>
    /// <remarks>
    /// An entry's <c>Setup</c> is a path and not a folder, because an add-on
    /// shares a folder with the game it patches. Re-reading from the folder
    /// alone would find the game again and turn the patch into a second copy
    /// of it.
    /// </remarks>
    private static string? TheInstallerPathOf(GameFolder game) =>
        game.Installer is null ? null : System.IO.Path.Combine(game.Path, game.Installer.RelativePath);

    private static string FolderOf(string path) =>
        System.IO.Path.GetDirectoryName(path) ?? path;

    private static bool IsAlreadyAnIcon(string? path) =>
        path is not null && path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase);
}
