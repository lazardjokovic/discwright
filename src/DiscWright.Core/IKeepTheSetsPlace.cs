namespace DiscWright.Core;

/// <summary>
/// Where a set that is part way through keeps its place.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="IStageWriter"/>, which builds a disc, and from
/// <see cref="IFileSystem"/>, which reads folders and promised to stay two
/// methods wide. This is one small note in one known place, read at the start
/// of a burn and written after every disc that comes out right.
/// </para>
/// <para>
/// Failing to leave the note is not failing to burn. A folder somebody cannot
/// write to costs them the offer to carry on, and nothing else, so the
/// implementation is expected to give up quietly rather than take a finished
/// disc down with it.
/// </para>
/// </remarks>
public interface IKeepTheSetsPlace
{
    /// <summary>
    /// What the note in this folder says, or nothing when there is none to
    /// read.
    /// </summary>
    string? WhatItSays(string folder);

    /// <summary>Leaves the note in this folder, over any already there.</summary>
    void Leave(string folder, string text);
}
