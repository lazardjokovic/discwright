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
    private PickOne(string headline, string because, IEnumerable<string> choices, bool pickTheFirst)
    {
        Headline = headline;
        Because = because;
        Choices = [.. choices];

        InitializeComponent();
        DataContext = this;

        // NOTHING IS CHOSEN FOR SOMEBODY WHEN THE ANSWER DESTROYS A DISC.
        //
        // The list used to start on the first row with "Use that one" as the
        // default button, so Enter erased whichever drive happened to be
        // first. That is fine for picking an image, where the worst case is
        // building the wrong one again, and wrong for erasing, where there
        // is nothing to undo.
        //
        // It had already happened once: pressing Erase a second time went to
        // E: without Lazar feeling he had picked it. The answer then was to
        // ask with a list instead of a yes or no, and the list still
        // answered for him.
        TheList.SelectedIndex = pickTheFirst ? 0 : -1;
        TheButton.IsEnabled = TheList.SelectedIndex >= 0;
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
    /// <param name="pickTheFirst">
    /// Whether to start with the first one chosen. False when the answer
    /// cannot be taken back, so that nobody erases a disc by pressing Enter.
    /// </param>
    internal static T? From<T>(
        Window owner,
        string headline,
        string because,
        IReadOnlyList<T> choices,
        Func<T, string> describe,
        bool pickTheFirst = true)
        where T : class
    {
        PickOne asking = new(headline, because, choices.Select(describe), pickTheFirst) { Owner = owner };

        // Written down like every yes or no, through the same log. Which disc
        // to erase came through here on 2026-11-10 and the log had the erase
        // but not the question, because only WorthAsking wrote anything.
        WorthAsking.Log.Write($"Asked: {headline} ({string.Join("; ", asking.Choices)})");

        T? chosen = asking.ShowDialog() == true && asking.Chosen >= 0 && asking.Chosen < choices.Count
            ? choices[asking.Chosen]
            : null;

        WorthAsking.Log.Write(chosen is null ? "Answered: none of them" : $"Answered: {asking.Choices[asking.Chosen]}");

        return chosen;
    }

    private void TakeThatOne(object sender, RoutedEventArgs e)
    {
        Chosen = TheList.SelectedIndex;
        DialogResult = Chosen >= 0;
    }

    private void NoneOfThem(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>Lets the button through once there is something to use.</summary>
    private void SomethingWasChosen(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        TheButton.IsEnabled = TheList.SelectedIndex >= 0;
}
