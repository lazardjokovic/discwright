namespace DiscWright.Core;

/// <summary>
/// What AutoRun can actually read.
/// </summary>
/// <remarks>
/// <para>
/// A port, because the answer is a property of the machine the disc is made on
/// and the machine it goes into, not of this program. AutoRun reads
/// <c>autorun.inf</c> in the system ANSI codepage and <strong>has no Unicode
/// mode at all</strong>, so a name it cannot hold arrives in This PC as
/// question marks. Both machines here are codepage 1252, where "Uber Alles"
/// survives and Cyrillic and Japanese do not.
/// </para>
/// <para>
/// Kept out of <c>Core</c> for a second reason: reaching the real codepage in
/// .NET needs an encoding provider registered, and <c>Encoding.Default</c>
/// there is UTF-8 rather than the ANSI codepage it is in Windows PowerShell. A
/// direct port of v1's line would compile, run, look right in an editor and
/// quietly write a file AutoRun reads wrongly.
/// </para>
/// </remarks>
public interface IAutorunEncoding
{
    /// <summary>
    /// The text as AutoRun will see it, once the codepage has had it.
    /// </summary>
    /// <remarks>
    /// Returns what survives rather than whether anything was lost, because the
    /// surviving text is what somebody needs to be shown. Being told "some
    /// characters will be lost" is not the same as being shown
    /// <c>??????? ????</c> and deciding it is not good enough.
    /// </remarks>
    string AsAutorunWillSeeIt(string text);
}
