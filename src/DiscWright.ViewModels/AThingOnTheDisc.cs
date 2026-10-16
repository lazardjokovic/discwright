using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DiscWright.Core;

namespace DiscWright.ViewModels;

/// <summary>
/// One row of the tree of what goes on the disc: a game, a folder inside it,
/// a file, or an add-on.
/// </summary>
/// <remarks>
/// <para>
/// A tick that can be taken off. Everything underneath a row follows it, and a
/// row follows what is underneath it: a folder with some of its files left out
/// is neither on nor off, and saying it is either would be a lie about what
/// will be written.
/// </para>
/// <para>
/// The tree holds <strong>everything that was found</strong> and never forgets
/// a row that was turned off, because turning it back on has to put the same
/// file back. What will actually be written is read off the tree each time it
/// changes, so there is one source of truth and no second list to drift.
/// </para>
/// </remarks>
public sealed class AThingOnTheDisc : INotifyPropertyChanged
{
    private readonly List<AThingOnTheDisc> _inside = [];
    private readonly Action? _tell;
    private readonly long _own;
    private AThingOnTheDisc? _above;
    private bool _on = true;

    /// <summary>A row with nothing under it: a file, or an add-on.</summary>
    /// <param name="name">What to show.</param>
    /// <param name="bytes">How big it is.</param>
    /// <param name="what">
    /// What kind of thing it is, where that matters: the installer and an
    /// add-on are both worth naming, an ordinary file is not.
    /// </param>
    /// <param name="itsPath">
    /// Where it is, as the thing that will be built needs to name it. For a
    /// file that is its path relative to the game folder; for an add-on it is
    /// the full path it was found at.
    /// </param>
    /// <param name="tell">Called whenever any tick anywhere below changes.</param>
    public AThingOnTheDisc(string name, long bytes, string? what, string itsPath, Action? tell = null)
    {
        Name = name;
        _own = bytes;
        What = what;
        ItsPath = itsPath;
        _tell = tell;
        Inside = new ReadOnlyCollection<AThingOnTheDisc>(_inside);
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>What to show for it.</summary>
    public string Name { get; }

    /// <summary>
    /// What kind of thing it is, or nothing for an ordinary file.
    /// </summary>
    public string? What { get; }

    /// <summary>
    /// Where it is. A file's path relative to its game folder, or an add-on's
    /// own path.
    /// </summary>
    public string ItsPath { get; }

    /// <summary>Everything directly under it.</summary>
    public ReadOnlyCollection<AThingOnTheDisc> Inside { get; }

    /// <summary>
    /// How big it is: its own size, or everything under it added up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Of everything, not of what is ticked. A folder that shrinks as rows are
    /// turned off would make it impossible to see how much is being left
    /// behind, which is the question somebody turning rows off is asking.
    /// </para>
    /// <para>
    /// Worked out rather than added up as rows arrive. A folder is put under
    /// its parent before anything goes in it, so a running total taken at that
    /// moment is a total of nothing.
    /// </para>
    /// </remarks>
    public long Bytes => _inside.Count == 0 ? _own : _inside.Sum(each => each.Bytes);

    /// <summary>How big it is, in words, for the row to show.</summary>
    /// <remarks>
    /// Said here rather than through a converter in the window. A converter
    /// would be a second place for the same decision to live, and this can be
    /// asked a question by a test that has no window in it.
    /// </remarks>
    public string HowBig => ByteSize.Describe(Bytes);

    /// <summary>Whether it has anything under it.</summary>
    public bool HasThingsInside => _inside.Count > 0;

    /// <summary>
    /// Whether it goes on the disc. Nothing, for a folder with some of its
    /// contents left out.
    /// </summary>
    public bool? IsOn
    {
        get
        {
            if (_inside.Count == 0)
            {
                return _on;
            }

            bool all = _inside.TrueForAll(each => each.IsOn == true);
            if (all)
            {
                return true;
            }

            return _inside.TrueForAll(each => each.IsOn == false) ? false : null;
        }

        set
        {
            // A box with no answer is what a mixed folder shows, never
            // something anybody sets. Clicking a mixed folder turns all of it
            // on, which is the useful half of the gesture.
            bool wanted = value ?? true;

            if (_inside.Count == 0)
            {
                if (_on == wanted)
                {
                    return;
                }

                _on = wanted;
            }
            else
            {
                foreach (AThingOnTheDisc each in _inside)
                {
                    each.SetQuietly(wanted);
                }
            }

            Changed();
            TellTheWayUp();
            TheGameItIsIn._tell?.Invoke();
        }
    }

    /// <summary>
    /// The game this row is in, which is itself when it is the game.
    /// </summary>
    /// <remarks>
    /// Remove takes a game off the disc, and the row somebody had selected
    /// when they pressed it may be a file three folders down. Removing that
    /// file instead would be a different thing wearing the same button.
    /// </remarks>
    public AThingOnTheDisc TheGameItIsIn => _above is null ? this : _above.TheGameItIsIn;

    /// <summary>Whether this row is a game rather than something in one.</summary>
    public bool IsAWholeGame => _above is null;

    /// <summary>Puts something under it.</summary>
    public AThingOnTheDisc Holding(AThingOnTheDisc thing)
    {
        thing._above = this;
        _inside.Add(thing);
        return this;
    }

    /// <summary>
    /// Every row under it with nothing under them, itself included when it is
    /// one, whether ticked or not.
    /// </summary>
    public IEnumerable<AThingOnTheDisc> EveryThing()
    {
        if (_inside.Count == 0)
        {
            yield return this;
            yield break;
        }

        foreach (AThingOnTheDisc each in _inside)
        {
            foreach (AThingOnTheDisc deeper in each.EveryThing())
            {
                yield return deeper;
            }
        }
    }

    /// <summary>Everything under it that is ticked.</summary>
    public IEnumerable<AThingOnTheDisc> EverythingOn() => EveryThing().Where(each => each._on);

    private void SetQuietly(bool on)
    {
        if (_inside.Count == 0)
        {
            if (_on != on)
            {
                _on = on;
                Changed();
            }

            return;
        }

        foreach (AThingOnTheDisc each in _inside)
        {
            each.SetQuietly(on);
        }

        Changed();
    }

    private void TellTheWayUp()
    {
        for (AThingOnTheDisc? up = _above; up is not null; up = up._above)
        {
            up.Changed();
        }
    }

    private void Changed([CallerMemberName] string? _ = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOn)));
}
