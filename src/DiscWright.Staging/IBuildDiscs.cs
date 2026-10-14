using DiscWright.Core;
using DiscWright.Core.Staging;

namespace DiscWright.Staging;

/// <summary>
/// Whatever turns a project into discs on a disk.
/// </summary>
/// <remarks>
/// <para>
/// An interface so the screen can be tested without building anything. Pressing
/// the button is a thing with four outcomes, and three of them are hard to
/// arrange with a real builder: it refuses, it throws, it is cancelled halfway,
/// or it works. A fake gives all four in milliseconds.
/// </para>
/// <para>
/// There is one implementation and there is not expected to be another. That is
/// a fine reason for an interface when the alternative is a screen that can
/// only be tested by writing gigabytes to a disk.
/// </para>
/// </remarks>
public interface IBuildDiscs
{
    Task<Outcome<BuiltDiscs>> Build(
        DiscProject project,
        Medium medium,
        string outputFolder,
        IProgress<BuildProgress>? progress,
        CancellationToken ct);
}
