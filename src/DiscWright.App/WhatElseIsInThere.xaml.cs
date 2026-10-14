using System.ComponentModel;
using System.Windows;

using DiscWright.Core;

namespace DiscWright.App;

/// <summary>
/// What else was in the folder, and what to do with it.
/// </summary>
public partial class WhatElseIsInThere : Window
{
    private WhatElseIsInThere(IReadOnlyList<LeftBehind> left)
    {
        Files = [.. left.Select(each => new OneOfThem(each))];

        Headline = left.Count == 1
            ? "There is one more file in that folder"
            : $"There are {left.Count} more files in that folder";

        InitializeComponent();
        DataContext = this;
    }

    /// <summary>The files, each with what somebody wants done with it.</summary>
    public IReadOnlyList<OneOfThem> Files { get; }

    /// <summary>The line at the top.</summary>
    public string Headline { get; }

    /// <summary>
    /// Asks about the files a folder held beyond the game, and answers with
    /// the game as it should now be and whatever is to be attached to it.
    /// </summary>
    /// <remarks>
    /// Nothing is asked when nothing was left behind, which is every folder of
    /// game files and most downloads. A dialog that appears to say "there is
    /// nothing to tell you" is worse than silence.
    /// </remarks>
    internal static (GameFolder Game, IReadOnlyList<LeftBehind> AddOns) Ask(
        Window owner, GameFolder game, IReadOnlyList<LeftBehind> left)
    {
        if (left.Count == 0)
        {
            return (game, []);
        }

        WhatElseIsInThere asking = new(left) { Owner = owner };

        if (asking.ShowDialog() != true)
        {
            return (game, []);
        }

        List<LeftBehind> wanted = [.. asking.Files.Where(one => one.Wanted).Select(one => one.What)];

        return (
            WhatWasLeftBehind.Taking(game, wanted.Where(one => Treat(asking, one) == 0)),
            [.. wanted.Where(one => Treat(asking, one) == 1)]);
    }

    private static int Treat(WhatElseIsInThere asking, LeftBehind one) =>
        asking.Files.First(each => each.What == one).Treatment;

    private void TakeThem(object sender, RoutedEventArgs e) => DialogResult = true;

    private void NoneOfThem(object sender, RoutedEventArgs e) => DialogResult = false;
}

/// <summary>One file in the list, and what is to become of it.</summary>
public sealed class OneOfThem : INotifyPropertyChanged
{
    private bool _wanted = true;
    private int _treatment;

    internal OneOfThem(LeftBehind what)
    {
        What = what;

        // Ticked to begin with, because somebody pointed at the folder these
        // came from, and the surprise worth avoiding is losing a file rather
        // than gaining one.
        _treatment = what.Kind == LeftBehindKind.LooksLikeAnAddOn ? 1 : 0;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal LeftBehind What { get; }

    public string Name => What.Name;

    public string Size => ByteSize.Describe(What.Bytes);

    /// <summary>
    /// What the row's dropdown answers to, which has to differ per row.
    /// </summary>
    /// <remarks>
    /// Every row in this list is the same template, so an id written into it
    /// would be the same id on all of them, and a test finding the first would
    /// pass while saying nothing about the rest.
    /// </remarks>
    public string WhatToDoWithIt => Name + ": what to do with it";

    /// <summary>What it would be called if it is attached as an add-on.</summary>
    public string Explains =>
        What.Kind == LeftBehindKind.LooksLikeAnAddOn
            ? "Looks like something that installs onto the game, so: " + What.AsAnAddOn
            : "Looks like something to read or look at";

    public bool Wanted
    {
        get => _wanted;
        set
        {
            _wanted = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Wanted)));
        }
    }

    /// <summary>Nought to copy it onto the disc, one to attach it as an add-on.</summary>
    public int Treatment
    {
        get => _treatment;
        set
        {
            _treatment = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Treatment)));
        }
    }
}
