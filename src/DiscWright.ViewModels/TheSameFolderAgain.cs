using DiscWright.Core;

namespace DiscWright.ViewModels;

/// <summary>
/// A folder that is on the list already, added again, and what it holds that
/// the disc does not.
/// </summary>
/// <remarks>
/// <para>
/// Adding the same folder twice used to put the same game on twice: two
/// entries with one name, the installer carried twice, the size doubled and
/// two Install buttons on the menu, with nobody told. What somebody adding a
/// folder again nearly always wants is the two patches they took off a minute
/// ago, so that is what is worked out and offered: exactly what is missing,
/// and nothing twice.
/// </para>
/// <para>
/// <strong>Missing means not on the list at all.</strong> A row that is
/// unticked is still on the window and one click from coming back, so it is
/// not counted. A row taken off with Remove is gone, and this is the way it
/// comes back. A file beside the game that was never taken on counts too,
/// because from the folder's side it is the same thing: in the folder, and
/// not on the disc.
/// </para>
/// </remarks>
/// <param name="At">Where the game sits on the list, from nought.</param>
/// <param name="Name">What the game is called.</param>
/// <param name="Files">The game's own files that are not on the list.</param>
/// <param name="Left">
/// What sat beside the game in its folder and is not on the list, neither as
/// a file of the game nor as an add-on.
/// </param>
public sealed record TheSameFolderAgain(
    int At, string Name, IReadOnlyList<ListedFile> Files, IReadOnlyList<LeftBehind> Left)
{
    /// <summary>Whether the disc already has everything the folder holds.</summary>
    public bool NothingIsMissing => HowMany == 0;

    /// <summary>How many things the folder holds that the disc does not.</summary>
    public int HowMany => Files.Count + Left.Count;

    /// <summary>What is missing, by name, the game's own files first.</summary>
    public IReadOnlyList<string> WhatIsMissing =>
        [.. Files.Select(file => file.Name), .. Left.Select(each => each.Name)];

    /// <summary>
    /// The question to put, worth putting only when something is missing.
    /// The likely answer puts it back.
    /// </summary>
    public AQuestion Question => HowMany == 1
        ? new(
            $"{Name} is already on the disc, with one thing from its folder left off",
            $"The folder holds {Named()} and the disc does not. Putting it back adds exactly that, and nothing "
                + "goes on twice. Otherwise the disc stays as it is.",
            "Put it back",
            "Leave it as it is")
        : new(
            $"{Name} is already on the disc, with {HowMany} things from its folder left off",
            $"The folder holds {Named()} and the disc does not. Putting them back adds exactly those, and "
                + "nothing goes on twice. Otherwise the disc stays as it is.",
            "Put them back",
            "Leave it as it is");

    /// <summary>What to say when nothing is missing, which is not a question.</summary>
    public Refusal AlreadyWhole => Refusals.ItIsAlreadyOnTheDisc(Name);

    /// <summary>
    /// The missing things by name, up to five of them and then a count, so
    /// the question stays a question rather than becoming a listing.
    /// </summary>
    private string Named()
    {
        IReadOnlyList<string> names = WhatIsMissing;

        return names.Count switch
        {
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            <= 5 => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
            _ => string.Join(", ", names.Take(5)) + $" and {names.Count - 5} more"
        };
    }
}
