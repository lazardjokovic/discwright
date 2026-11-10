namespace DiscWright.Core;

/// <summary>
/// Somewhere to write down what happened, one line at a time, for whoever has
/// to work out afterwards why a burn went the way it did.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Asked for by the owner on 2026-11-10</strong>, after a night of
/// working out a failed set from screenshots and the Windows System log. That
/// log knew a drive had bad blocks and nothing else: not which disc of which
/// set, not what the program had asked, not how long anything had waited.
/// </para>
/// <para>
/// Writing must never fail anything. A burn that is going well does not stop
/// because a line about it could not be saved.
/// </para>
/// </remarks>
public interface IKeepALog
{
    /// <summary>Writes one line, with the time in front of it.</summary>
    void Write(string line);
}

/// <summary>A log that keeps nothing, for tests and for anything built without one.</summary>
public sealed class NothingIsLogged : IKeepALog
{
    /// <inheritdoc />
    public void Write(string line)
    {
    }
}
