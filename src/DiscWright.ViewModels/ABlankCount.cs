using System.ComponentModel;
using System.Globalization;

using DiscWright.Core;

namespace DiscWright.ViewModels;

/// <summary>
/// One size of disc and how many blanks of it somebody has, as a small box
/// on the window shows it.
/// </summary>
/// <remarks>
/// The box holds what was typed, so it never fights the person typing in it:
/// emptied, it is empty and means none. What reaches the screen is only ever
/// a count, and anything that is not one is none.
/// </remarks>
public sealed class ABlankCount : INotifyPropertyChanged
{
    private readonly Action<Medium, int> _changed;
    private string _typed;

    internal ABlankCount(Medium medium, int count, Action<Medium, int> changed)
    {
        Medium = medium;
        _typed = AsTyped(count);
        _changed = changed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The size.</summary>
    public Medium Medium { get; }

    /// <summary>What the box is labelled: the short name every size has.</summary>
    public string Name => Medium.Key;

    /// <summary>The whole name, for a reader and for the hover.</summary>
    public string InFull => $"Blank {Medium.Name} discs you have";

    /// <summary>What the box answers to, which has to differ from box to box.</summary>
    public string BoxId => "Blanks" + Medium.Key;

    /// <summary>What is in the box.</summary>
    public string Typed
    {
        get => _typed;
        set
        {
            string typed = value ?? string.Empty;

            if (typed == _typed)
            {
                return;
            }

            _typed = typed;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Typed)));
            _changed(Medium, CountIn(typed));
        }
    }

    /// <summary>
    /// The count the screen has now, shown in the box. Not through
    /// <see cref="Typed"/>, which would be the screen telling itself.
    /// </summary>
    internal void Show(int count)
    {
        if (CountIn(_typed) == count)
        {
            return;
        }

        _typed = AsTyped(count);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Typed)));
    }

    private static string AsTyped(int count) =>
        count > 0 ? count.ToString(CultureInfo.InvariantCulture) : string.Empty;

    private static int CountIn(string typed) =>
        int.TryParse(typed.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int count)
            ? Math.Clamp(count, 0, BlanksOwned.TheMostOfOneSize)
            : 0;
}
