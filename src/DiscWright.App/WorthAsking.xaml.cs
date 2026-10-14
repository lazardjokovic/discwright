using System.Windows;

namespace DiscWright.App;

/// <summary>
/// A question with two answers, where neither of them is a refusal.
/// </summary>
public partial class WorthAsking : Window
{
    private WorthAsking(string headline, string because, string likely, string other)
    {
        Headline = headline;
        Because = because;
        TheLikelyOne = likely;
        TheOtherWay = other;

        InitializeComponent();
        DataContext = this;
    }

    public string Headline { get; }

    public string Because { get; }

    /// <summary>The answer that is nearly always wanted, so it is the button.</summary>
    public string TheLikelyOne { get; }

    /// <summary>The other one, which is still perfectly reasonable.</summary>
    public string TheOtherWay { get; }

    /// <summary>Asks, and answers true for the likely one.</summary>
    internal static bool Ask(Window owner, string headline, string because, string likely, string other) =>
        new WorthAsking(headline, because, likely, other) { Owner = owner }.ShowDialog() == true;

    private void TakeTheLikelyOne(object sender, RoutedEventArgs e) => DialogResult = true;

    private void TakeTheOtherWay(object sender, RoutedEventArgs e) => DialogResult = false;
}
