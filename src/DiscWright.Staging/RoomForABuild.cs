using DiscWright.Core;

namespace DiscWright.Staging;

/// <summary>
/// How much room a build takes on the drive it is written to, and how much
/// of that is already there to be had.
/// </summary>
/// <remarks>
/// <para>
/// A build writes every disc twice: the folder, and the image made from it,
/// which is as big again. Nothing looked at whether the drive could hold
/// that before starting. The owner's own build, 21 November 2026: four
/// games, 153 GB written over forty minutes onto a drive that could not
/// take the last 37, and the first he heard of it was the drive at nothing
/// free and the last image half written.
/// </para>
/// <para>
/// A sum for a question and not for a refusal. It does not know what else
/// will be written to the drive in the next hour or deleted from it, and a
/// drive can report less than it will give. So the window asks, with the
/// figures, and somebody who knows better builds anyway.
/// </para>
/// </remarks>
public sealed record RoomForABuild(long Needed, long Free, long ComesBack)
{
    /// <summary>What there is to write into: what is free, and what an earlier build gives back as this one replaces it.</summary>
    public long ThereIs => Free + ComesBack;

    /// <summary>Whether the drive holds it.</summary>
    public bool IsEnough => Needed <= ThereIs;

    /// <summary>How much more it wants than there is, or nothing.</summary>
    public long TooLittleBy => Math.Max(0, Needed - ThereIs);

    /// <summary>
    /// What a build needs: every disc's folder and an image of it.
    /// </summary>
    /// <remarks>
    /// Twice the discs, whether or not only the images are kept. The folders
    /// go once every image is made and not before, because a folder deleted
    /// before its image was written would be a build that left nothing, so
    /// at its fullest the drive holds both of everything.
    /// </remarks>
    public static long NeededBy(PlannedBuild build) =>
        2 * build.Outputs.Sum(output => output.Layout.Discs.Sum(disc => disc.Bytes));

    /// <summary>
    /// The figures for building this into that folder, or nothing when the
    /// drive will not say how much room it has, which is no reason to stop
    /// anybody.
    /// </summary>
    public static RoomForABuild? For(PlannedBuild build, IFileSystem disk, string outputFolder)
    {
        if (string.IsNullOrWhiteSpace(outputFolder) || disk.RoomAt(outputFolder) is not long free)
        {
            return null;
        }

        return new RoomForABuild(NeededBy(build), free, WhatComesBack(build, disk, outputFolder));
    }

    /// <summary>
    /// The size of what an earlier build left where this one goes. Building
    /// over it deletes it, so its room is this build's to use.
    /// </summary>
    /// <remarks>
    /// Looked for where this build will write and nowhere else: the folder
    /// itself for one disc or a set, and a folder for each game when they
    /// get a disc each. Only disc folders and images named as this build's
    /// will be, which are the only things a build deletes.
    /// </remarks>
    private static long WhatComesBack(PlannedBuild build, IFileSystem disk, string outputFolder)
    {
        long bytes = 0;

        foreach (PlannedOutput output in build.Outputs)
        {
            string where = output.InItsOwnFolder
                ? System.IO.Path.Combine(outputFolder, OnTheDisc.Tidied(output.Label))
                : outputFolder;

            AnEarlierBuild there = AnEarlierBuild.In(disk, where, output.Label);

            if (!there.IsThere)
            {
                continue;
            }

            bytes += there.Folders.Sum(folder => disk.List(System.IO.Path.Combine(where, folder)).Bytes);

            HashSet<string> images = new(there.Images, StringComparer.OrdinalIgnoreCase);

            bytes += disk.ListHere(where).Files.Where(file => images.Contains(file.Name)).Sum(file => file.Bytes);
        }

        return bytes;
    }
}
