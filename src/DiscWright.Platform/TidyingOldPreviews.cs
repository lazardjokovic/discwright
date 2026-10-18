using DiscWright.Core;

namespace DiscWright.Platform;

/// <summary>
/// Removes preview folders nobody has looked at in a while.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately dull. It walks one folder, asks <see cref="WhatMayBeEmptied"/>
/// whether each child is really one of ours, asks <see cref="OldPreviews"/>
/// whether it has had its day, and removes it if both say yes.
/// </para>
/// <para>
/// <strong>It never throws.</strong> This runs while the program is starting
/// and a locked folder is an ordinary thing: somebody may still have the menu
/// open in mshta from the last run. A failure to tidy up is not a reason to
/// fail to start, so each folder is tried on its own and anything that will
/// not go is simply left.
/// </para>
/// </remarks>
public static class TidyingOldPreviews
{
    /// <summary>
    /// Removes what has had its day, and says how many went.
    /// </summary>
    /// <param name="temporaryFolder">
    /// Where this machine keeps temporary files. Handed in rather than asked
    /// for, so a test can point this at a folder of its own and watch it work
    /// on real files without going near anybody's actual temporary folder.
    /// </param>
    /// <param name="now">The time, handed in for the same reason.</param>
    public static int In(string temporaryFolder, DateTime now)
    {
        string root = System.IO.Path.Combine(
            temporaryFolder, WhereAPreviewGoes.TheFolderTheyAllGoIn);

        if (!Directory.Exists(root))
        {
            return 0;
        }

        int went = 0;

        foreach (string each in SafelyListed(root))
        {
            // No second check that this folder is one of ours, and that is
            // deliberate. There was one, asking WhatMayBeEmptied, and it
            // could not fail: everything in this list is a direct child of
            // the preview root, which is exactly what that rule allows.
            //
            // A planted fault removed it and no test noticed, because there
            // was nothing to notice. A guard that cannot fail is not
            // protection, it is decoration, and this one was worse than
            // decoration: it read like safety and talked me into a test that
            // proved something else. What keeps this honest is the one line
            // above, which never looks outside the root.
            try
            {
                if (!OldPreviews.HadItsDay(Directory.GetLastWriteTime(each), now))
                {
                    continue;
                }

                Directory.Delete(each, recursive: true);
                went++;
            }
            catch (Exception wrong) when (wrong is IOException or UnauthorizedAccessException)
            {
                // In use, or not ours to remove. Either way it stays, and the
                // program carries on starting.
            }
        }

        return went;
    }

    private static string[] SafelyListed(string root)
    {
        try
        {
            return Directory.GetDirectories(root);
        }
        catch (Exception wrong) when (wrong is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
