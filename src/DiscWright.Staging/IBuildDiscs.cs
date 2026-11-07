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

    /// <summary>
    /// Writes the recipe alone: <c>discproject.json</c> at the top of the
    /// output folder, exactly where and how a finished build leaves it, with
    /// no disc built and nothing else touched.
    /// </summary>
    /// <remarks>
    /// Defaulted to refuse rather than to do nothing, deliberately. Sixteen
    /// fakes stand in for this interface and almost none of them save, so a
    /// quiet default keeps them short, but a quiet default that succeeded
    /// would let a test walk through a save that saved nothing and pass.
    /// </remarks>
    void SaveTheRecipe(DiscProject project, Medium medium, string outputFolder) =>
        throw new NotSupportedException("This builder does not save recipes.");
}
