namespace DiscWright.Core;

/// <summary>
/// One way of recognising a folder. Asked in turn until one of them claims it.
/// </summary>
/// <remarks>
/// Recognition is where new cases keep arriving, which is why it is a chain:
/// adding a kind of source is one new class and one line in the list, and
/// nothing already written is edited.
/// </remarks>
public interface IGameFolderRecogniser
{
    /// <summary>What this one recognises, in words. "a GOG download".</summary>
    string Describes { get; }

    /// <summary>
    /// Three answers, not two.
    /// </summary>
    /// <returns>
    /// <para><c>null</c> means <em>not mine, ask the next one</em>.</para>
    /// <para><c>Ok</c> means <em>mine, and here it is</em>.</para>
    /// <para>
    /// <c>No</c> means <strong>mine, and broken</strong>, and it must not fall
    /// through to the next recogniser. The case that proves this is needed:
    /// a folder with no <c>setup_*.exe</c> but a <c>disc</c> subfolder holding
    /// one is an output folder somebody picked by mistake. Passed on, the next
    /// recogniser accepts it happily as a folder of game files, and the disc
    /// gets a disc inside it. Only the recogniser that knows what the folder
    /// looks like can say what is wrong with it.
    /// </para>
    /// </returns>
    Outcome<GameFolder>? Recognise(FolderListing folder);
}
