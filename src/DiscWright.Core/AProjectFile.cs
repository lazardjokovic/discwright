using System.Text.Json;

namespace DiscWright.Core;

/// <summary>
/// Reads the file a disc carries that says how it was made.
/// </summary>
/// <remarks>
/// <para>
/// Every disc DiscWright has ever built carries <c>discproject.json</c>, and
/// somebody who finds one in a drawer should be able to open it, change one
/// thing and build it again. <strong>That is the promise a rewrite has to
/// keep</strong>, for every disc this version built.
/// </para>
/// <para>
/// <strong>A disc the PowerShell DiscWright built is recognised and refused by
/// name.</strong> Its file describes a menu look this version does not have,
/// a panel side, a button style, a title, a caption, a divider, a border and a
/// list of buttons, and until 2026-10-27 all of it was read and thrown away,
/// so the disc that came back was not the disc the file described. Saying so
/// is the honest answer, and the owner chose it over carrying the old look
/// into this menu.
/// </para>
/// <para>
/// The rule for everything else: <strong>a field that is not there is not an
/// error, it is an older file</strong>, and what it meant then is what it
/// means now. Each default below is the behaviour of the version that lacked
/// the field, not a value that seemed sensible.
/// </para>
/// <para>
/// It reads text, not a path. What comes out describes what somebody chose, and
/// not one file of it has been looked at: the folders have to be read again
/// before any of it is a disc, because a game that has moved is a thing to say
/// out loud rather than to discover halfway through a build.
/// </para>
/// </remarks>
public static class AProjectFile
{
    public static Outcome<OpenedProject> Read(string json)
    {
        JsonDocument document;

        try
        {
            // The mark at the front, if there is one. Windows PowerShell writes
            // it and v1 IS Windows PowerShell, so every project file every disc
            // carries begins ef bb bf. A reader that will not look past it can
            // open none of them.
            document = JsonDocument.Parse(json.TrimStart('﻿'));
        }
        catch (JsonException wrong)
        {
            return Refusals.ThatIsNotAProjectFile(wrong.Message);
        }

        using (document)
        {
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return Refusals.ThatIsNotAProjectFile("The file does not hold a project at all.");
            }

            // Before anything else is read, so that a file from the other
            // program is named as one however else it is shaped, with no
            // games in it included.
            if (WasWrittenByThePowerShellApp(root))
            {
                return Refusals.ThatProjectIsFromThePowerShellApp(Text(root, "AppVersion"));
            }

            List<ProjectEntry> entries = [.. EntriesIn(root)];

            if (entries.Count == 0)
            {
                return Refusals.ThatProjectHasNoGamesInIt();
            }

            return new OpenedProject(
                Version: Number(root, "Version") ?? 1,
                MadeBy: Text(root, "AppVersion"),
                Label: Text(root, "Label") ?? string.Empty,
                Entries: entries,
                Options: new DiscOptions(
                    Menu: Yes(root, "Menu"),
                    Checksums: Yes(root, "Checksums"),

                    // ArtworkOnDisc was read here until the option went. It
                    // is still written, because v1 reads it and a file this
                    // writes has to open there, but nothing here branches on
                    // it any more: the artwork goes on the disc either way.
                    LegacyFileSystems: Yes(root, "LegacyFs"),
                    CutFiles: Yes(root, "CutFiles"),

                    // Absent in every file written before the option, and on
                    // in all of them, so absent means on.
                    StartHere: Yes(root, "StartHere", whenMissing: true)),

                // Version 5. Absent is the automatic setting, which is what
                // those projects always described.
                MediaKey: Text(root, "MediaKey") ?? string.Empty,
                OutputFolder: Text(root, "OutDir"),
                ManualPath: Text(root, "ManualPath"),
                ExtrasFolderPath: Text(root, "ExtrasPath"),
                LooseExtras: [.. Strings(root, "ExtraItems")],
                MusicPath: Text(root, "MusicFile"),
                Artwork: new ProjectArtwork(
                    IconPath: Text(root, "IconPath"),
                    IconIsAlreadyAnIcon: Yes(root, "IconIsIco"),
                    BackgroundPath: Text(root, "BgPath"),
                    BackgroundAsItIs: Yes(root, "BgAsIs"),
                    CoverPath: Text(root, "CoverPath"),
                    DiscFacePath: Text(root, "DiscArtPath"),

                    // Tidied on the way in, so a file somebody edited by
                    // hand still only ever yields a colour or nothing.
                    BackgroundColour: SolidColour.Tidied(Text(root, "BgColour"))));
        }
    }

    /// <summary>
    /// The keys the PowerShell DiscWright writes into every project file and
    /// this version never does, because they describe a menu look its menu
    /// does not have.
    /// </summary>
    /// <remarks>
    /// v1 has written the panel side, the button style and the list of buttons
    /// since 0.1.0, unconditionally, so every file it ever wrote carries them.
    /// The version it stamps cannot tell the two programs apart, because this
    /// one carries on v1's numbering, and <c>Version</c> is the schema, which
    /// both write as 14. The look is the one thing only the other program
    /// writes.
    /// </remarks>
    private static readonly string[] TheLookOnlyThePowerShellAppWrites =
        ["PanelSide", "ButtonStyle", "Buttons", "Divider", "ShowTitle", "TitleText", "WindowBorder", "ShowCaption"];

    private static bool WasWrittenByThePowerShellApp(JsonElement root) =>
        TheLookOnlyThePowerShellAppWrites.Any(key => root.TryGetProperty(key, out _));

    /// <summary>
    /// The games, however the file happens to list them.
    /// </summary>
    /// <remarks>
    /// Version 1 held one game in <c>SourceFolder</c>; version 2 added the
    /// <c>Games</c> array. Both come back as the same list, so that nothing
    /// downstream has to know which it was reading.
    /// </remarks>
    private static IEnumerable<ProjectEntry> EntriesIn(JsonElement root)
    {
        if (root.TryGetProperty("Games", out JsonElement games) && games.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement game in games.EnumerateArray())
            {
                if (game.ValueKind != JsonValueKind.Object || Text(game, "Folder") is not string folder)
                {
                    // An entry with nowhere to read from describes nothing.
                    continue;
                }

                yield return new ProjectEntry(
                    Folder: folder,
                    Name: Text(game, "GameName"),

                    // Version 6. Older files have none, which leaves the name
                    // found by looking at the folder, and that is the
                    // registered one. So a project renamed under the old code
                    // repairs itself on opening, as long as its folder is
                    // still there.
                    MatchName: Text(game, "MatchName"),
                    Installer: Text(game, "Setup"),

                    // Version 3 gave an entry a kind and a parent. Without
                    // them every entry is a plain game, which is what those
                    // discs held.
                    IsAnAddOn: Text(game, "Kind") == "AddOn",
                    BelongsTo: Number(game, "Parent") ?? -1,

                    // Version 9. Anything older could only hold GOG downloads,
                    // so a file without it describes one.
                    WasAGogDownload: Text(game, "Source") != "Files",

                    // 2.0.4. Older files have neither, which is no manual
                    // and no extras of the game's own, exactly what they held.
                    Own:
                    [
                        .. Strings(game, "OwnManuals").Select(path => new GameExtra(path, GameExtraKind.Manual)),
                        .. Strings(game, "OwnExtras").Select(path => new GameExtra(path, GameExtraKind.Extra))
                    ]);
            }

            yield break;
        }

        if (Text(root, "SourceFolder") is string only)
        {
            yield return new ProjectEntry(only, Text(root, "GameName"), null, null, false, -1, true);
        }
    }

    private static string? Text(JsonElement holder, string name) =>
        holder.TryGetProperty(name, out JsonElement found) && found.ValueKind == JsonValueKind.String
            ? found.GetString()
            : null;

    private static int? Number(JsonElement holder, string name) =>
        holder.TryGetProperty(name, out JsonElement found) && found.TryGetInt32(out int number)
            ? number
            : null;

    private static bool Yes(JsonElement holder, string name, bool whenMissing = false) =>
        holder.TryGetProperty(name, out JsonElement found)
            ? found.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => whenMissing,
                _ => whenMissing
            }
            : whenMissing;

    private static IEnumerable<string> Strings(JsonElement holder, string name)
    {
        if (!holder.TryGetProperty(name, out JsonElement found) || found.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement each in found.EnumerateArray())
        {
            if (each.ValueKind == JsonValueKind.String && each.GetString() is string text && text.Length > 0)
            {
                yield return text;
            }
        }
    }
}
