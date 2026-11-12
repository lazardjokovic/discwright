using DiscWright.Core;
using DiscWright.Staging;

namespace DiscWright.ViewModels;

/// <summary>
/// Everything the screen needs from the machine it happens to be running on.
/// </summary>
/// <remarks>
/// <para>
/// One thing rather than nine arguments. They arrived one at a time as the
/// program grew, and a constructor somebody has to count the commas in is a
/// constructor nobody reads.
/// </para>
/// <para>
/// Every one of them is an interface, and not one of them is Windows as far as
/// this project is concerned. That is what lets the whole screen be driven by a
/// test with no disk, no drive and no disc.
/// </para>
/// </remarks>
/// <param name="Encoding">What AutoRun can carry, for the warning about a name.</param>
/// <param name="Builder">What turns a project into discs on a disk.</param>
/// <param name="Images">What turns a staged disc into one file.</param>
/// <param name="Drives">What says which recorders are on the machine.</param>
/// <param name="Burner">What writes an image to a disc.</param>
/// <param name="Eraser">What blanks a rewritable one, when told to.</param>
/// <param name="Disk">For reading things back, including a burned disc.</param>
/// <param name="Hasher">For proving what came back is what went on.</param>
/// <param name="SetsPlace">Where a set part way through keeps its place.</param>
/// <param name="Tray">What opens and shuts a drive, and says whether it can.</param>
/// <param name="Releases">What says which version is newest, when asked.</param>
/// <param name="Preview">What writes a menu somebody can look at.</param>
/// <param name="Pictures">What can read a picture, for checking one when it is picked.</param>
/// <param name="ThisBuild">Which version this one is.</param>
/// <param name="HowBigIs">
/// How big a file or folder is. Handed in rather than read here, because this
/// is asked again on every keystroke and the window must not touch a disk that
/// often.
/// </param>
public sealed record TheMachine(
    IAutorunEncoding Encoding,
    IBuildDiscs Builder,
    IMakeImages Images,
    IFindRecorders Drives,
    IBurnDiscs Burner,
    IEraseDiscs Eraser,
    IFileSystem Disk,
    IHasher Hasher,
    IKeepTheSetsPlace SetsPlace,
    IWorkTheTray Tray,
    IAskWhatIsReleased Releases,
    IShowTheMenu Preview,
    IImages Pictures,
    string ThisBuild,
    Func<string, long> HowBigIs,

    // Last and optional, so every place that builds one of these for a test
    // keeps working and the ones that care about a folder say so.
    WhereDiscsWouldGo? Places = null,

    // How long a drive gets to say what is in it, which is as much a fact
    // about this machine as the drives themselves. A real one needs seconds
    // and a test needs none. Null means what a real one needs.
    HowLongADriveGets? Patience = null,

    // What fetches and runs a newer installer, when somebody presses Update.
    // Null means this copy cannot update itself, and a newer release offers
    // its page, which is what it did before there was an Update.
    IUpdateThisCopy? Updater = null,

    // The copy's page in the Microsoft Store, when it came from there, and
    // nothing otherwise. The Store keeps such a copy up to date, so it never
    // asks GitHub and never updates itself: its version link is this page.
    string? StorePage = null);
