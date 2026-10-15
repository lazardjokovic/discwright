using System.Text;
using DiscWright.Core;

namespace DiscWright.Platform;

/// <summary>
/// The set's place, kept as a file beside the discs it is about.
/// </summary>
/// <remarks>
/// Beside the discs rather than in a settings folder somewhere, because a set
/// belongs to the folder it was built in. Copy that folder to another machine
/// and the note goes with it; throw the folder away and the note goes too,
/// which is the right answer in both cases.
/// </remarks>
public sealed class ANoteOnDisk : IKeepTheSetsPlace
{
    /// <inheritdoc />
    public string? WhatItSays(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return null;
        }

        try
        {
            string path = Path.Combine(folder, TheNoteBesideTheDiscs.FileName);

            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception wrong) when (wrong is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // No note that can be read is the same as no note, and the only
            // thing that rides on it is whether somebody is offered the chance
            // to carry on. Starting from disc 1 is the safe way to be wrong.
            return null;
        }
    }

    /// <inheritdoc />
    public void Leave(string folder, string text)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        try
        {
            // UTF-8 with no byte order mark, for the same reason as everything
            // else this program writes: a BOM turns up as three characters in
            // front of the first line for anything not expecting one.
            File.WriteAllText(
                Path.Combine(folder, TheNoteBesideTheDiscs.FileName),
                text,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception wrong) when (wrong is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A disc that is written and checked stays written and checked
            // whether or not this file could be saved. Taking the burn down
            // over a note about it would be the wrong way round.
        }
    }
}
