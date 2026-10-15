using System.Windows;

namespace DiscWright.App;

/// <summary>
/// One out of several, where picking for somebody would be guessing.
/// </summary>
/// <remarks>
/// Two things in this program refuse to guess: which drive, when two of them
/// hold blanks, and which image, when a set is sitting in the folder. Both end
/// up here.
/// </remarks>
public partial class PickOne : Window
{
    private PickOne(string headline, string because, IEnumerable<string> choices)
    {
        Headline = headline;
        Because = because;
        Choices = [.. choices];

        InitializeComponent();
        DataContext = this;
    }

    public string Headline { get; }

    public string Because { get; }

    /// <summary>What is on offer, in the words somebody reads.</summary>
    public IReadOnlyList<string> Choices { get; }

    /// <summary>Which one was taken, or -1.</summary>
    private int Chosen { get; set; } = -1;

    /// <summary>
    /// Asks, and answers with the one chosen, or nothing when nobody chose.
    /// </summary>
    /// <param name="describe">
    /// How to write each one down. What goes back is the thing itself, not the
    /// words, so a drive comes back as a drive.
    /// </param>
    internal static T? From<T>(
        Window owner,
        string headline,
        string because,
        IReadOnlyList<T> choices,
        Func<T, string> describe)
        where T : class
    {
        PickOne asking = new(headline, because, choices.Select(describe)) { Owner = owner };

        return asking.ShowDialog() == true && asking.Chosen >= 0 && asking.Chosen < choices.Count
            ? choices[asking.Chosen]
            : null;
    }

    private void TakeThatOne(object sender, RoutedEventArgs e)
    {
        Chosen = TheList.SelectedIndex;
        DialogResult = Chosen >= 0;
    }

    private void NoneOfThem(object sender, RoutedEventArgs e) => DialogResult = false;
}
