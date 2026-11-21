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

    /// <summary>
    /// Takes away a folder a build made for one game's disc, if there is
    /// nothing left in it. One with anything in it is left exactly as it is:
    /// whatever is there is not this build's to judge.
    /// </summary>
    void RemoveIfEmpty(string folder)
    {
    }

    /// <summary>
    /// Deletes a disc image a stopped build had made. Only ever a file whose
    /// name ends in <c>.iso</c>, and nothing when it is not there.
    /// </summary>
    void RemoveTheImage(string imagePath)
    {
    }
}
