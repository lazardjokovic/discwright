namespace DiscWright.Core;

/// <summary>
/// Deleting a disc folder once its image is made, for somebody who wants the
/// image alone.
/// </summary>
/// <remarks>
/// <para>
/// A port of its own rather than one more thing the stage writer does for
/// the builder. The builder never deletes what it has just made; this is the
/// screen's decision, taken after the image is safely written, and a test
/// has to be able to see that it was asked for and for which folder.
/// </para>
/// <para>
/// Whatever does it obeys <see cref="WhatMayBeEmptied"/>, the one rule about
/// which folders this program may destroy: a folder it named <c>disc</c> or
/// <c>disc D1</c>, and nothing else, ever.
/// </para>
/// </remarks>
public interface IRemoveADiscFolder
{
    /// <summary>
    /// Deletes the folder and everything in it, or leaves all of it where it
    /// is.
    /// </summary>
    /// <exception cref="TheFolderIsInUse">
    /// Something has it open. Nothing has been deleted.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// It is not a disc folder this program named. Nothing has been deleted.
    /// </exception>
    void Remove(string discFolder);
}
