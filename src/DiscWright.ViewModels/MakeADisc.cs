using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

using DiscWright.Core;
using DiscWright.Staging;

namespace DiscWright.ViewModels;

/// <summary>
/// Everything the main window knows and can do, with no window in it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>There is no WPF in this project at all.</strong> That is the point:
/// "what happens when you pick a folder with no game in it" is a test that runs
/// in milliseconds with no window, no desktop and no machine, and what is left
/// for an automated window test to do is only the clicking.
/// </para>
/// <para>
/// It is also what makes the choice of window framework cheap to change. If WPF
/// turns out to be wrong, what gets thrown away is the markup; none of this
/// moves.
/// </para>
/// </remarks>
public sealed class MakeADisc : INotifyPropertyChanged
{
    private readonly IAutorunEncoding _encoding;
    private readonly Func<string, long> _howBigIs;
    private readonly List<GameFolder> _games = [];

    private string _label = string.Empty;
    private string _outputFolder = string.Empty;
    private Medium _medium = Media.Dvd5;
    private DiscOptions _options = new();

    /// <param name="encoding">
    /// What AutoRun can carry, for the warning about a name it cannot show.
    /// </param>
    /// <param name="howBigIs">
    /// How big a file or folder is. Handed in rather than read here, because
    /// this is asked again on every keystroke and the window must not touch a
    /// disk that often.
    /// </param>
    public MakeADisc(IAutorunEncoding encoding, Func<string, long> howBigIs)
    {
        _encoding = encoding;
        _howBigIs = howBigIs;
        Reconsider();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The games, in the order they were added.</summary>
    public IReadOnlyList<GameFolder> Games => _games;

    /// <summary>What This PC will show when the disc goes in.</summary>
    public string Label
    {
        get => _label;
        set => Change(ref _label, value);
    }

    /// <summary>Where the discs get written.</summary>
    public string OutputFolder
    {
        get => _outputFolder;
        set => Change(ref _outputFolder, value);
    }

    /// <summary>The size of disc being aimed at.</summary>
    public Medium Medium
    {
        get => _medium;
        set => Change(ref _medium, value);
    }

    /// <summary>Everything that has an answer already.</summary>
    public DiscOptions Options
    {
        get => _options;
        set => Change(ref _options, value);
    }

    /// <summary>
    /// What pressing the button would produce: "one disc", "a set of 3 discs",
    /// "4 games, a disc each". Empty when it would produce nothing.
    /// </summary>
    public string WhatItWillMake { get; private set; } = string.Empty;

    /// <summary>
    /// Why the button is dead, or null when it is not.
    /// </summary>
    /// <remarks>
    /// Worked out as things are chosen rather than when the button is pressed.
    /// A refusal that arrives after a click is a refusal somebody has already
    /// committed to, and the same refusal shown beforehand is just a sentence.
    /// </remarks>
    public Refusal? WhyNot { get; private set; }

    /// <summary>
    /// Something true and worth saying that does not stop anything.
    /// </summary>
    /// <remarks>
    /// Today that is only the disc's name: AutoRun has no Unicode at all, so a
    /// name it cannot carry arrives in This PC as question marks. The disc
    /// still works, still installs, and somebody may genuinely not mind. What
    /// they must not do is find out afterwards.
    /// </remarks>
    public Refusal? Worry { get; private set; }

    /// <summary>Whether there is anything to press the button for.</summary>
    public bool CanMakeIt => WhyNot is null && OutputFolder.Length > 0;

    /// <summary>Every size of disc that can be aimed at.</summary>
    /// <remarks>
    /// An instance property although it reads nothing of the instance, because
    /// a WPF binding cannot reach a static one. Static would mean every list in
    /// the window having to know the class it comes from, which is the window
    /// knowing more about this than it should.
    /// </remarks>
    [SuppressMessage("Performance", "CA1822", Justification = "A binding cannot reach a static property.")]
    public IReadOnlyList<Medium> DiscSizes => Media.All;

    /// <summary>Whether the disc gets a menu that opens when it goes in.</summary>
    public bool Menu
    {
        get => Options.Menu;
        set => Switch(Options with { Menu = value });
    }

    /// <summary>Whether the disc carries a list of what every file should be.</summary>
    public bool Checksums
    {
        get => Options.Checksums;
        set => Switch(Options with { Checksums = value });
    }

    /// <summary>Whether the cover art goes on the disc as well as on paper.</summary>
    public bool ArtworkOnDisc
    {
        get => Options.ArtworkOnDisc;
        set => Switch(Options with { ArtworkOnDisc = value });
    }

    /// <summary>Whether to add the file systems an older Windows can read.</summary>
    public bool LegacyFileSystems
    {
        get => Options.LegacyFileSystems;
        set => Switch(Options with { LegacyFileSystems = value });
    }

    /// <summary>
    /// Whether a file too big for the room left may be split across two discs.
    /// </summary>
    public bool CutFiles
    {
        get => Options.CutFiles;
        set => Switch(Options with { CutFiles = value });
    }

    /// <summary>Adds a game, and keeps the order they were added in.</summary>
    public void Add(GameFolder game)
    {
        _games.Add(game);

        // The first game names the disc, because that is nearly always what
        // somebody would have typed. Only the first: renaming it afterwards is
        // their decision, and a second game must not overwrite it.
        if (_games.Count == 1 && Label.Length == 0)
        {
            _label = game.Name.ToUpperInvariant();
            Tell(nameof(Label));
        }

        Reconsider();
        Tell(nameof(Games));
    }

    /// <summary>Takes a game back off the list.</summary>
    public void Remove(GameFolder game)
    {
        _games.Remove(game);
        Reconsider();
        Tell(nameof(Games));
    }

    /// <summary>The project as it stands, for whoever builds it.</summary>
    public DiscProject AsAProject() => new(Label, _games, Options);

    /// <summary>
    /// Works out everything the window says back, after anything changes.
    /// </summary>
    /// <remarks>
    /// One method rather than a rule per property, because the answers depend
    /// on each other: a bigger disc can turn a refusal into a build, and a
    /// game removed can turn three discs into one.
    /// </remarks>
    private void Reconsider()
    {
        Worry = Label.Length == 0 ? null : DiscLabel.WhatWindowsWillMakeOfIt(Label, _encoding);

        if (_games.Count == 0)
        {
            // Not a refusal. Somebody who has not added a game yet has not done
            // anything wrong, and telling them so on an empty window is the
            // program complaining about its own starting state.
            WhyNot = null;
            WhatItWillMake = string.Empty;
            Told();
            return;
        }

        if (Label.Length == 0)
        {
            WhyNot = Refusals.ADiscNeedsAName();
            WhatItWillMake = string.Empty;
            Told();
            return;
        }

        switch (WhatWouldBeMade.For(AsAProject(), Medium, _howBigIs))
        {
            case Outcome<PlannedBuild>.Ok(PlannedBuild planned):
                WhyNot = null;
                WhatItWillMake = planned.InWords;
                break;

            case Outcome<PlannedBuild>.No refused:
                WhyNot = refused.Refusal;
                WhatItWillMake = string.Empty;
                break;
        }

        Told();
    }

    /// <summary>
    /// Takes a changed option, and says both which one changed and that the
    /// whole set did, because the window binds to each one by name and to the
    /// set as a whole.
    /// </summary>
    private void Switch(DiscOptions to, [CallerMemberName] string? name = null)
    {
        if (_options == to)
        {
            return;
        }

        _options = to;
        Tell(name);
        Tell(nameof(Options));

        // Splitting a file changes what would be made, so the line saying so
        // has to be worked out again.
        Reconsider();
    }

    private void Told()
    {
        Tell(nameof(WhatItWillMake));
        Tell(nameof(WhyNot));
        Tell(nameof(Worry));
        Tell(nameof(CanMakeIt));
    }

    private void Change<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        Tell(name);
        Reconsider();
    }

    private void Tell(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
