using System.ComponentModel;

namespace DiscWright.ViewModels;

/// <summary>
/// Which of the window's amber lines are showing, given the ones somebody has
/// put aside with the small cross beside them.
/// </summary>
/// <remarks>
/// <para>
/// A line put aside stays hidden only while it says the same thing. The owner,
/// 25 November 2026: an amber line somebody has read and decided about should
/// go, and come back when it has something new to say. Hidden for ever, a line
/// put aside about one game would also hide the same warning about the next,
/// which is the thing amber exists to say.
/// </para>
/// <para>
/// Words that change forget the putting aside, so a line that goes back to
/// what it said before shows again: a second time is a second time.
/// </para>
/// <para>
/// Read by name, through the indexer, so the window binds each line with
/// <c>Notices[WhatTheDownloadsSay]</c> and there is one place to add a line.
/// </para>
/// </remarks>
public sealed class TheNotices : INotifyPropertyChanged
{
    /// <summary>The window's amber lines, by the name of what each one shows.</summary>
    public static readonly IReadOnlyList<string> Names =
    [
        nameof(MakeADisc.WhatTheDownloadsSay),
        nameof(MakeADisc.APatchWithNothingToPatch),
        nameof(MakeADisc.WhoWearsTheSameName),
        nameof(MakeADisc.WhatDidNotComeBack),
        nameof(MakeADisc.WhyThatMatters),
        nameof(MakeADisc.AboutTheDiscInTheDrive),
        nameof(MakeADisc.Worry),
    ];

    private readonly Func<string, string?> _wordsOf;
    private readonly Dictionary<string, string> _putAside = new(StringComparer.Ordinal);

    internal TheNotices(Func<string, string?> wordsOf) => _wordsOf = wordsOf;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Whether that line has something to say and has not been put aside saying it.</summary>
    public bool this[string which] =>
        Words(which) is string words
        && !(_putAside.TryGetValue(which, out string? aside) && aside == words);

    /// <summary>Hides that line for as long as it goes on saying what it says now.</summary>
    public void PutAside(string which)
    {
        if (Words(which) is string words)
        {
            _putAside[which] = words;
            Changed();
        }
    }

    /// <summary>
    /// Forgets what has changed its words since it was put aside, and tells
    /// the window to look again.
    /// </summary>
    internal void Look()
    {
        foreach (string which in _putAside.Keys.ToList())
        {
            if (Words(which) != _putAside[which])
            {
                _putAside.Remove(which);
            }
        }

        Changed();
    }

    /// <summary>Every line back, for a new disc.</summary>
    internal void Forget()
    {
        _putAside.Clear();
        Changed();
    }

    private string? Words(string which) =>
        _wordsOf(which) is string words && !string.IsNullOrWhiteSpace(words) ? words : null;

    // Binding.IndexerName, which WPF listens for when any indexed value moves.
    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
}
