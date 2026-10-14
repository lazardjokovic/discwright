namespace DiscWright.Core;

/// <summary>
/// One file waiting to go on a disc, as the planner needs to see it.
/// </summary>
/// <remarks>
/// Three strings and a number, and deliberately nothing else. The planner is
/// arithmetic over sizes: it never opens a file, never asks what kind of file it
/// is, and never cares which game it belongs to. That is what lets every case in
/// the catalogue be tested without a single byte on disk.
/// </remarks>
/// <param name="RelativePath">Where it goes on the disc, relative to the root.</param>
/// <param name="Source">Where it comes from. Carried through untouched, for whoever copies it.</param>
/// <param name="Bytes">How big it is.</param>
public sealed record PlanFile(string RelativePath, string Source, long Bytes);
