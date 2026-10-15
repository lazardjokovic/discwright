using DiscWright.Core;

namespace DiscWright.Staging;

/// <summary>
/// What a built disc's image is called, and where it sits.
/// </summary>
/// <remarks>
/// <para>
/// Every disc of a set carries the same label, so a name made from the label
/// alone is the same name for all of them: the second disc's image lands on
/// top of the first, and the set is one file that says it is three.
/// </para>
/// <para>
/// Beside the disc folders rather than inside one, and named the way those
/// folders are: <c>disc</c> and <c>disc D1</c> there, <c>LABEL.iso</c> and
/// <c>LABEL D1.iso</c> here.
/// </para>
/// </remarks>
public static class TheImageOf
{
    public static string That(BuiltDisc disc)
    {
        string beside = System.IO.Path.GetDirectoryName(disc.Folder) ?? disc.Folder;
        string name = OnTheDisc.Tidied(disc.Label);

        return System.IO.Path.Combine(
            beside,
            disc.OfDiscs > 1 ? $"{name} D{disc.Number}.iso" : $"{name}.iso");
    }
}
