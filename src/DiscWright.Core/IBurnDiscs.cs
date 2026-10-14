namespace DiscWright.Core;

/// <summary>
/// Whatever can tell you about the drives on this machine.
/// </summary>
/// <remarks>
/// Asking costs nothing and consumes no media, which is the whole point of
/// doing it before anything is written. A CD-R is written once: every mistake
/// caught by asking the drive a question costs nothing, and every mistake not
/// caught costs a disc.
/// </remarks>
public interface IFindRecorders
{
    /// <summary>Every recorder on the machine, with what is in it.</summary>
    IReadOnlyList<Recorder> All();
}

/// <summary>Whatever writes an image to a disc.</summary>
public interface IBurnDiscs
{
    Task<Outcome<BurnedDisc>> Burn(
        string imagePath,
        Recorder onto,
        IProgress<BurnProgress>? progress,
        CancellationToken ct);
}

/// <summary>One drive, and whatever is in it at the moment of asking.</summary>
/// <param name="Id">What IMAPI calls it, which is not something to show anybody.</param>
/// <param name="Drive">The letter, with its colon, or empty if it has none.</param>
/// <param name="Name">The make and model, tidied.</param>
/// <param name="MediaType">What is in it, as IMAPI's number.</param>
/// <param name="Blank">Whether what is in it is empty.</param>
/// <param name="FreeSectors">How much of it is left.</param>
/// <param name="Speeds">The write speeds it offered, in sectors a second.</param>
/// <param name="WhyNot">
/// Why nothing could be asked of it, when nothing could: usually that the tray
/// is empty, which is not a fault.
/// </param>
public sealed record Recorder(
    string Id,
    string Drive,
    string Name,
    int MediaType,
    bool Blank,
    long FreeSectors,
    IReadOnlyList<int> Speeds,
    string? WhyNot = null)
{
    /// <summary>What is in it, in words.</summary>
    public string Holding => WhyNot is not null ? "nothing readable" : OpticalMedia.NameOf(MediaType);

    /// <summary>Whether a disc could be written to it right now.</summary>
    public bool Ready => WhyNot is null && Blank && OpticalMedia.CanBeWritten(MediaType) && FreeSectors > 0;

    /// <summary>Whether erasing what is in it would make it usable.</summary>
    public bool ErasingWouldHelp => WhyNot is null && OpticalMedia.ErasingWouldHelp(MediaType, Blank);

    /// <summary>One line about it, for a list somebody chooses from.</summary>
    public override string ToString() =>
        $"{Drive} {Name}, holding {Holding}";
}

/// <summary>One disc, written.</summary>
/// <param name="Drive">Where it was written.</param>
/// <param name="Seconds">How long the write took.</param>
/// <param name="Reports">
/// How many times the drive said anything while it worked, which is worth
/// keeping even when it worked.
/// </param>
public sealed record BurnedDisc(string Drive, int Seconds, int Reports);

/// <summary>How far through writing a disc.</summary>
/// <param name="Doing">What the drive says it is doing.</param>
/// <param name="HowFar">From nothing to one, or null when the drive will not say.</param>
/// <param name="SecondsLeft">What the drive thinks is left, or null.</param>
public sealed record BurnProgress(string Doing, double? HowFar, int? SecondsLeft)
{
    public override string ToString() =>
        SecondsLeft is int left && left > 0
            ? $"{Doing}, about {Clock(left)} left"
            : Doing;

    private static string Clock(int seconds) =>
        seconds >= 60 ? $"{seconds / 60} min {seconds % 60} sec" : $"{seconds} sec";
}
