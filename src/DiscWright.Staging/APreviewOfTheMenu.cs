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
    public Outcome<string> Write(DiscProject project, Medium medium, Func<string, long> sizeOf, string into)
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
        // the only thing EmptyTheFolder is for.
        writer.EmptyTheFolder(into);

        // Planned the way building plans, because which games land on disc one
        // is part of what somebody is previewing. Asked once: planning walks
        // every file in the project, and a refusal is an answer rather than a
        // reason to go round again.
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

        PlannedOutput first = planned.Outputs[0];

        BuildContext context = new(
            project with { Label = first.Label, Games = first.Games },
            first.Layout,
            first.Layout.Discs[0],
            into,
            writer);


        // The artwork and the music first, and not only the menu.
        //
        // A preview that leaves them out shows a menu drawn on nothing, which
        // is the one thing somebody choosing a background most wants to look
        // at. The point of a preview is the disc as it will be.
        //
        // The same steps the build runs, in the order the build runs them:
        // the menu names a background file that has to already be there.
        foreach (IBuildStep step in new IBuildStep[]
        {
            new CopyTheArtworkStep(pictures),
            new CopyTheMusicStep(),
            new WriteTheMenuStep(forADisc: false)
        })
        {
            if (step.AppliesTo(context))
            {
                step.Run(context, CancellationToken.None).GetAwaiter().GetResult();
            }
        }

        return System.IO.Path.Combine(into, Folder, "menu.hta");
    }
}

/// <summary>
/// Writing out a menu somebody can look at without making a disc.
/// </summary>
/// <remarks>
/// A port, so that the screen can offer a preview without knowing what opens
/// an HTA on this machine, and so that a test can ask for one with no folder,
/// no browser and nothing on screen.
/// </remarks>
public interface IShowTheMenu
{
    /// <summary>
    /// Writes the menu for the first disc this project would make, and says
    /// where it put it, or why it could not.
    /// </summary>
    Outcome<string> Write(DiscProject project, Medium medium, Func<string, long> sizeOf, string into);
}
