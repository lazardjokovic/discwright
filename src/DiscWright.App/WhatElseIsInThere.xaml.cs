using System.ComponentModel;
using System.Windows;

using DiscWright.Core;

namespace DiscWright.App;

/// <summary>
/// What else was in the folder, and what to do with it.
/// </summary>
public partial class WhatElseIsInThere : Window, INotifyPropertyChanged
{
    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Whether one of the two buttons was pressed, as against the window
    /// being closed with the X.
    /// </summary>
    /// <remarks>
    /// ShowDialog answers false for both, and they are not the same thing.
    /// "None of them" is a decision to leave files off, taken by somebody who
    /// read the list. The X is somebody wanting out of the question, and
    /// treating it as the decision dropped every file in silence, which is
    /// the one thing this dialog exists to prevent.
    /// </remarks>
    private bool _answered;

    private WhatElseIsInThere(IReadOnlyList<LeftBehind> left, string? installer)
    {
        Files = [.. left.Select(each => new OneOfThem(each, installer))];

        Headline = left.Count == 1
            ? "There is one more file in that folder"
            : $"There are {left.Count} more files in that folder";

        AboutPatches = WhatWasLeftBehind.AboutThePatches(installer, left);

        // The two lines that follow the ticks follow every tick: the big
        // button greys when nothing is wanted, and the tick-them-all link
        // greys when there is nothing left for it to tick.
        // The two lines that follow the ticks follow every tick: the big
        // button greys when nothing is wanted, and the tick-them-all link
        // greys when there is nothing left for it to tick.
        foreach (OneOfThem each in Files)
        {
            each.PropertyChanged += (_, what) =>
            {
                if (what.PropertyName == nameof(OneOfThem.Wanted))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AnyWanted)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanTickThemAll)));
                }
            };
        }

        InitializeComponent();
        DataContext = this;
    }

    /// <summary>
    /// Whether anything is ticked at all. With nothing ticked, "Put these
    /// on too" would put nothing on, which is the other button's job, so
    /// it greys instead of lying.
    /// </summary>
    public bool AnyWanted => Files.Any(one => one.Wanted);

    /// <summary>
    /// Whether the tick-them-all link has anything left to do.
    /// </summary>
    public bool CanTickThemAll => Files.Any(one => !one.Wanted);

    /// <summary>
    /// Every row ticked at once, for the person keeping everything: the
    /// patch defaults untick what the installer covers, and an archive
    /// disc wants it anyway.
    /// </summary>
    private void TickThemAll(object sender, RoutedEventArgs e)
    {
        foreach (OneOfThem each in Files)
        {
            each.Wanted = true;
        }
    }

    /// <summary>The files, each with what somebody wants done with it.</summary>
    public IReadOnlyList<OneOfThem> Files { get; }

    /// <summary>The line at the top.</summary>
    public string Headline { get; }

    /// <summary>
    /// What a patch is and what this disc's installer already carries, said
    /// once above the rows, or nothing when no patch is among the files.
    /// </summary>
    public string? AboutPatches { get; }

    /// <summary>
    /// Asks about the files a folder held beyond the game, and answers with
    /// the game as it should now be and whatever is to be attached to it, or
    /// with nothing when the question was closed rather than answered.
    /// </summary>
    /// <remarks>
    /// Nothing is asked when nothing was left behind, which is every folder of
    /// game files and most downloads. A dialog that appears to say "there is
    /// nothing to tell you" is worse than silence.
    /// </remarks>
    internal static (GameFolder Game, IReadOnlyList<LeftBehind> AddOns)? Ask(
        Window owner, GameFolder game, IReadOnlyList<LeftBehind> left)
    {
        if (left.Count == 0)
        {
            return (game, []);
        }

        WhatElseIsInThere asking = new(left, game.Installer?.Name) { Owner = owner };

        bool? said = asking.ShowDialog();

        // The X, which is neither answer. The folder is left alone and can be
        // pointed at again.
        if (!asking._answered)
        {
            return null;
        }

        if (said != true)
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

    private void TakeThem(object sender, RoutedEventArgs e)
    {
        _answered = true;
        DialogResult = true;
    }

    private void NoneOfThem(object sender, RoutedEventArgs e)
    {
        _answered = true;
        DialogResult = false;
    }
}

/// <summary>One file in the list, and what is to become of it.</summary>
public sealed class OneOfThem : INotifyPropertyChanged
{
    private readonly string? _installer;
    private bool _wanted;
    private int _treatment;

    internal OneOfThem(LeftBehind what, string? installer)
    {
        What = what;
        _installer = installer;

        // Ticked to begin with, because somebody pointed at the folder these
        // came from, and the surprise worth avoiding is losing a file rather
        // than gaining one. The one exception is a patch the installer
        // provably already covers, which starts off with its reason on its
        // line, the owner's design on 2026-11-09.
        _wanted = what.WantedAgainst(installer);
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

    /// <summary>
    /// The line under the name: what the file looks like, and for a patch
    /// the names can judge, what it adds against this disc's installer.
    /// </summary>
    public string Explains => What.ExplainedAgainst(_installer);

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
