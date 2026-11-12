using DiscWright.Core;

namespace DiscWright.Tests;

/// <summary>A log whose lines land in a list, for a test to read back.</summary>
internal sealed class TheLogAsAList : IKeepALog
{
    private readonly List<string> _lines = [];

    /// <summary>Every line so far, in the order written.</summary>
    internal IReadOnlyList<string> Lines
    {
        get
        {
            lock (_lines)
            {
                return [.. _lines];
            }
        }
    }

    public void Write(string line)
    {
        lock (_lines)
        {
            _lines.Add(line);
        }
    }
}
