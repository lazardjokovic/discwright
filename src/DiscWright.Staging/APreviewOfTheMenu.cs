using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Menu;

namespace DiscWright.Staging;

/// <summary>
/// Showing somebody the menu their disc would get, before they make one.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It runs the same step the build runs.</strong> A preview written by
/// a second piece of code that draws something similar is a preview that drifts
/// from the disc, and the day it drifts is the day somebody burns a disc
/// because the preview looked right.
/// </para>
/// <para>
/// So it plans the discs exactly as building would, takes disc one, and runs
/// <c>WriteTheMenuStep</c> over a folder that is not a disc. What comes out is
/// the menu, to the byte, apart from the one thing that has to differ.
/// </para>
/// </remarks>
public sealed class APreviewOfTheMenu(IStageWriter writer, IImages pictures) : IShowTheMenu
{
    /// <summary>Where the menu sits, on a disc and here.</summary>
    private const string Folder = "AUTORUN";

    /// <inheritdoc />
    public Outcome<IReadOnlyList<string>> Write(
        DiscProject project, Medium medium, Func<string, long> sizeOf, string into)
    {
        // Emptied first, and before the plan is even attempted.
        //
        // This folder belongs to this project and is written to again every
        // time somebody looks, so last time's files are sitting in it. A
        // background that has since been cleared would still be there, and a
        // preview that refuses would leave the previous menu behind looking
        // like the answer to the question just asked.
        //
        // Safe here because the folder is one this program named, which is
        // the only thing EmptyTheFolder is for. The discs below go in folders
        // of their own underneath it, made fresh, so this is still the one
        // call that removes anything.
        writer.EmptyTheFolder(into);

        // Planned the way building plans, because which games land on which
        // disc is part of what somebody is previewing. Asked once: planning
        // walks every file in the project, and a refusal is an answer rather
        // than a reason to go round again.
        PlannedBuild planned;

        switch (WhatWouldBeMade.For(project, medium, sizeOf))
        {
            case Outcome<PlannedBuild>.Ok(PlannedBuild made):
                planned = made;
                break;

            case Outcome<PlannedBuild>.No refused:
                return refused.Refusal;

            default:
                return Refusals.ThereIsNothingToPutOnADisc();
        }

        List<string> written = [];
        HashSet<string> taken = new(StringComparer.OrdinalIgnoreCase);

        foreach (PlannedOutput output in planned.Outputs)
        {
            // Narrowed with its add-ons, not only its games. Cut down to the
            // games alone, every disc of a disc-each build offered the
            // first game's add-ons.
            DiscProject forThisOne = project.KeepingOnly(output.Games, output.Label);

            foreach (PlannedDisc disc in output.Layout.Discs)
            {
                bool theFirst = written.Count == 0;

                string folder = theFirst
                    ? into
                    : System.IO.Path.Combine(into, Named(output, disc, taken));

                // Made before anything is written into it, the way the builder
                // empties each disc's folder before that disc's first step.
                // The comment above said the discs below go in folders "made
                // fresh" and nothing made them: emptying the root creates the
                // root, and every disc after the first was a path nothing had
                // brought into being.
                //
                // It took an icon to show. An icon is the only thing that sits
                // at the root of a disc; everything else goes into AUTORUN or
                // ARTWORK, and making one of those makes the disc's folder on
                // the way. So previewing a set threw, and only for somebody
                // who had chosen an icon. Reported from the window as "Could
                // not find a part of the path ... HOLLOW KNIGHT
                // D2(backslash)HOLLOWKNIGHT2.ico".
                writer.MakeFolder(folder);

                BuildContext context = new(forThisOne, output.Layout, disc, folder, writer);

                // The artwork and the music first, and not only the menu.
                //
                // A preview that leaves them out shows a menu drawn on
                // nothing, which is the one thing somebody choosing a
                // background most wants to look at. The point of a preview is
                // the disc as it will be.
                //
                // The same steps the build runs, in the order the build runs
                // them: the menu names a background file that has to already
                // be there.
                //
                // The music only on the first, which is this preview's third
                // deliberate difference from a disc. Every disc of a set
                // carries the tune and every disc's menu plays it, and three
                // windows opening at once and playing the same bar over each
                // other is not a preview of anything.
                foreach (IBuildStep step in TheSteps(theFirst))
                {
                    if (step.AppliesTo(context))
                    {
                        step.Run(context, CancellationToken.None).GetAwaiter().GetResult();
                    }
                }

                written.Add(System.IO.Path.Combine(folder, Folder, "menu.hta"));
            }
        }

        return written;
    }

    private IBuildStep[] TheSteps(bool theFirst) =>
    [
        new CopyTheArtworkStep(pictures),
        .. theFirst ? new IBuildStep[] { new CopyTheMusicStep() } : [],
        new WriteTheMenuStep(forADisc: false, playsMusic: theFirst)
    ];

    /// <summary>
    /// What to call the folder a disc after the first goes in.
    /// </summary>
    /// <remarks>
    /// Readable, because somebody opening their temporary folder should be
    /// able to tell which disc is which, and unique, because two games can be
    /// called the same thing and the second one must not land on top of the
    /// first.
    /// </remarks>
    private static string Named(PlannedOutput output, PlannedDisc disc, HashSet<string> taken)
    {
        string wanted = output.Layout.Discs.Count > 1
            ? $"{output.Label} D{disc.Number}"
            : output.Label;

        string name = wanted;

        for (int again = 2; !taken.Add(name); again++)
        {
            name = $"{wanted} ({again})";
        }

        return name;
    }
}

/// <summary>
/// Writing out the menus somebody can look at without making a disc.
/// </summary>
/// <remarks>
/// A port, so that the screen can offer a preview without knowing what opens
/// an HTA on this machine, and so that a test can ask for one with no folder,
/// no browser and nothing on screen.
/// </remarks>
public interface IShowTheMenu
{
    /// <summary>
    /// Writes the menu for every disc this project would make, in the order
    /// they would be burned, and says where it put them, or why it could not.
    /// </summary>
    /// <remarks>
    /// <strong>Every disc, not only the first.</strong> Each disc of a set has
    /// a menu of its own, and the window decides how many of them to open.
    /// The manual and the extras ride disc 1 since 2026-11-09, so the one
    /// window most people open shows them.
    /// </remarks>
    Outcome<IReadOnlyList<string>> Write(
        DiscProject project, Medium medium, Func<string, long> sizeOf, string into);
}
