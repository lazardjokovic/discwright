namespace DiscWright.Core.Staging;

/// <summary>
/// Everything a step is given, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// This is the type the plan warned about, the one that decides whether the
/// step arrangement pays off or quietly becomes a god object handed to
/// everything. Five fields, each of which some step genuinely needs.
/// </para>
/// <para>
/// <strong>What is deliberately not here: progress and logging.</strong> They
/// belong to the pipeline, which is the only thing that knows how many steps
/// there are and which one is running. A step that could report its own
/// progress would have to know its own number, and then every step would need
/// telling where it sits in a list it is not part of.
/// </para>
/// <para>
/// It describes <em>one disc</em>. A set runs the pipeline once per disc with a
/// different <see cref="Disc"/> and <see cref="StageFolder"/> each time, which
/// is what keeps "copy the files" and "write the set manifest" from having to
/// be the same shape.
/// </para>
/// </remarks>
/// <param name="Project">What the person asked for.</param>
/// <param name="Layout">All the discs, so a step can say "2 of 3".</param>
/// <param name="Disc">The one being made now.</param>
/// <param name="StageFolder">The folder that becomes this disc.</param>
/// <param name="Writer">The only way anything here reaches a disk.</param>
public sealed record BuildContext(
    DiscProject Project,
    DiscLayout Layout,
    PlannedDisc Disc,
    string StageFolder,
    IStageWriter Writer)
{
    /// <summary>Where the menu lives, and so where the fallback icon lives.</summary>
    private const string MenuFolder = "AUTORUN";

    /// <summary>DiscWright's own icon, which the menu step writes beside the page.</summary>
    private const string OurOwnIcon = "menu.ico";

    /// <summary>
    /// A backslash. Written as a character code because a typed one arrives
    /// halved in generated code and the file still parses.
    /// </summary>
    private const char Separator = (char)92;

    /// <summary>Whether this disc is one of several.</summary>
    public bool IsPartOfASet => Layout.Discs.Count > 1;

    /// <summary>
    /// What this one disc is called, which is the project's label with the
    /// disc's own number after it when there is more than one.
    /// </summary>
    /// <remarks>
    /// Here rather than in the step that writes autorun.inf, because the icon
    /// file is named after it and the two must not be able to disagree about
    /// which disc this is.
    ///
    /// Not called DiscLabel, which is already a class in this namespace's
    /// parent: a property of that name shadows the type inside this record,
    /// and the first person to want DiscLabel.WhatWindowsWillMakeOfIt in here
    /// would get an error they have to read twice.
    /// </remarks>
    public string WhatThisDiscIsCalled => IsPartOfASet
        ? Project.Label + " " + Disc.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : Project.Label;

    /// <summary>
    /// What this disc's icon file is called, at the root and beside the menu.
    /// </summary>
    /// <remarks>
    /// Named after the disc rather than a fixed <c>disc.ico</c>, because
    /// Explorer and the taskbar cache icons by path and a shared name means a
    /// new disc in an old drive letter wears the old disc's icon. See
    /// <see cref="DiscIconName"/>, which is where that is explained properly.
    /// </remarks>
    public string IconFileName => DiscIconName.For(Project.Label, Disc.Number, Layout.Discs.Count);

    /// <summary>
    /// The icon this disc really carries, spelled the way <c>autorun.inf</c>
    /// needs it, or <c>null</c> when it carries none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are the three things a build actually writes, and nothing else.
    /// A chosen icon goes to the root under <see cref="IconFileName"/>. With
    /// none chosen the menu step writes ours beside the page, but only when
    /// there is a menu to put it beside. With neither, no icon is written at
    /// all and the honest answer is to say nothing.
    /// </para>
    /// <para>
    /// It was the first of those three for every disc, whether or not anybody
    /// had chosen an icon, so every disc built without artwork named a file
    /// that was not on it. The menu had the fallback right and the two
    /// disagreed, which is how it was found: seventeen discs out of seventeen
    /// in the kit sweep, each pointing Windows at nothing.
    /// </para>
    /// </remarks>
    public string? TheIconOnTheDisc =>
        Project.Artwork?.IconPath is not null ? IconFileName
        : Project.Options.Menu ? MenuFolder + Separator + OurOwnIcon
        : null;

    /// <summary>Where a file on this disc goes, as a full path.</summary>
    public string On(string relativePath) =>
        System.IO.Path.Combine(StageFolder, relativePath);
}
