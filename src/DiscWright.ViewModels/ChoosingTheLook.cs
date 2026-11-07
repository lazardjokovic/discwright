using System.ComponentModel;

namespace DiscWright.ViewModels;

/// <summary>The three ways the program can dress.</summary>
public enum ALook
{
    /// <summary>Whatever Windows wears, which is the default.</summary>
    FollowingWindows,

    Light,

    Dark,
}

/// <summary>
/// Which look somebody chose for the program, discwright#98's "application
/// themes".
/// </summary>
/// <remarks>
/// <para>
/// Following Windows is the default and the design: the one honest answer to
/// "which theme" is usually the one somebody already gave Windows. But plenty
/// of people keep Windows light and want one program dark, or the other way
/// round, so the owner decided on 2026-11-07 that the question may be
/// answered for this program alone. The default did not move.
/// </para>
/// <para>
/// One quiet control cycles the three answers rather than a dropdown,
/// because this is a preference somebody touches once, not a setting to
/// study. The choice is kept through whoever constructs this, so the window
/// opens dressed the way it was left.
/// </para>
/// </remarks>
public sealed class ChoosingTheLook : INotifyPropertyChanged
{
    private readonly Action<string> _keep;

    /// <param name="kept">What was kept last time, or nothing at all.</param>
    /// <param name="keep">Where the next choice goes to be kept.</param>
    public ChoosingTheLook(string? kept, Action<string> keep)
    {
        _keep = keep;
        Chosen = FromText(kept);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ALook Chosen { get; private set; }

    /// <summary>What the control says, naming the choice it carries now.</summary>
    public string InWords => Chosen switch
    {
        ALook.Light => "Appearance: light",
        ALook.Dark => "Appearance: dark",
        _ => "Appearance: following Windows",
    };

    /// <summary>
    /// The next answer round the circle: following Windows, light, dark, and
    /// back to following.
    /// </summary>
    public void Next()
    {
        Chosen = Chosen switch
        {
            ALook.FollowingWindows => ALook.Light,
            ALook.Light => ALook.Dark,
            _ => ALook.FollowingWindows,
        };

        _keep(AsText(Chosen));

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Chosen)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InWords)));
    }

    /// <summary>
    /// Whether the program wears the dark, given what Windows wears.
    /// </summary>
    public bool WearsTheDark(bool windowsWearsIt) =>
        Chosen == ALook.Dark || (Chosen == ALook.FollowingWindows && windowsWearsIt);

    /// <summary>
    /// Anything unrecognised is the default, because a file somebody edited
    /// into nonsense must not pick a side.
    /// </summary>
    private static ALook FromText(string? kept) => kept?.Trim().ToLowerInvariant() switch
    {
        "light" => ALook.Light,
        "dark" => ALook.Dark,
        _ => ALook.FollowingWindows,
    };

    private static string AsText(ALook look) => look switch
    {
        ALook.Light => "light",
        ALook.Dark => "dark",
        _ => "following windows",
    };
}
