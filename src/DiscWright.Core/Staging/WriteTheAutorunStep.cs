namespace DiscWright.Core.Staging;

/// <summary>
/// Writes the <c>autorun.inf</c> that gives the disc its name, its icon, and
/// what happens when it goes in.
/// </summary>
/// <remarks>
/// Always, even when there is no menu. Without it the disc is called something
/// like <c>DVD_ROM</c> in This PC, which is what somebody sees first and is the
/// part they will remember in ten years.
/// </remarks>
public sealed class WriteTheAutorunStep : IBuildStep
{
    public string Says => "naming the disc";

    /// <summary>
    /// Every disc gets one. There is no option for this and there should not
    /// be: a disc with no name is a disc nobody can identify in a drawer.
    /// </summary>
    public bool AppliesTo(BuildContext context) => true;

    public Task Run(BuildContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Both from the context, so the name in icon= and the name of the
        // file that gets written cannot drift apart. They used to be a const
        // here and a const read from here, which was the same thing said
        // twice and worked only because neither ever changed.
        //
        // TheIconOnTheDisc rather than IconFileName, because the second is
        // what a chosen icon WOULD be called and the first is what is really
        // on the disc. Naming the second wrote icon= for discs nobody had
        // chosen an icon for, and pointed Windows at a file no step wrote.
        context.Writer.WriteAutorunText(
            context.On("autorun.inf"),
            AutorunFile.For(context.WhatThisDiscIsCalled, context.TheIconOnTheDisc, context.Project.Options.Menu));

        return Task.CompletedTask;
    }
}
