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
/// keep</strong>, and it is why this reads every version back to the first
/// rather than only the one being written now.
/// </para>
/// <para>
/// The rule throughout: <strong>a field that is not there is not an error, it
/// is an older file</strong>, and what it meant then is what it means now. Each
/// default below is the behaviour of the version that lacked the field, not a
/// value that seemed sensible.
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
                    CutFiles: Yes(root, "CutFiles")),

                // Version 5. Absent is the automatic setting, which is what
                // those projects always described.
                MediaKey: Text(root, "MediaKey") ?? string.Empty,
                OutputFolder: Text(root, "OutDir"),
                ManualPath: Text(root, "ManualPath"),
                ExtrasFolderPath: Text(root, "ExtrasPath"),
                LooseExtras: [.. Strings(root, "ExtraItems")],
                WantsTheSetFile: Yes(root, "DiscSet"),
                WantsLinuxInfo: Yes(root, "LinuxInfo"),
                Artwork: new ProjectArtwork(
                    IconPath: Text(root, "IconPath"),
                    IconIsAlreadyAnIcon: Yes(root, "IconIsIco"),
                    BackgroundPath: Text(root, "BgPath"),
                    BackgroundAsItIs: Yes(root, "BgAsIs"),
                    CoverPath: Text(root, "CoverPath"),
                    DiscFacePath: Text(root, "DiscArtPath")),
                Look: new ProjectLook(
                    // Right and Bordered are what the versions without these
                    // fields drew.
                    PanelSide: Text(root, "PanelSide") ?? "Right",
                    ButtonStyle: Text(root, "ButtonStyle") ?? "Bordered",
                    Divider: Yes(root, "Divider"),
                    ShowTitle: Yes(root, "ShowTitle"),
                    TitleText: Text(root, "TitleText") ?? string.Empty,
                    WindowBorder: Yes(root, "WindowBorder"),

                    // The one default that is NOT false when the field is
                    // missing. Taking it as false would turn the caption off on
                    // every disc made before the option existed, every one of
                    // which showed one.
                    ShowCaption: Yes(root, "ShowCaption", whenMissing: true),
                    MusicFile: Text(root, "MusicFile"),
                    Buttons: [.. Strings(root, "Buttons")]));
        }
    }

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

                    // Version 4. Absent reads back as an entry with none of its
                    // own, and the menu then falls back to the disc-wide manual
                    // and extras, exactly as those discs behaved.
                    Manual: Text(game, "Manual"),
                    Extras: Text(game, "Extras"));
            }

            yield break;
        }

        if (Text(root, "SourceFolder") is string only)
        {
            yield return new ProjectEntry(only, Text(root, "GameName"), null, null, false, -1, true, null, null);
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
