using System.ComponentModel;
using DiscWright.Core;

namespace DiscWright.ViewModels;

/// <summary>
/// Writes in the log what the window says, as the window is told it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It listens rather than being called.</strong> Every sentence the
/// window shows reaches it through an announcement, and
/// <c>TheWindowChecksItself</c> already holds the screen to announcing every
/// change it makes. So nothing has to remember to log anything: a new
/// refusal written next year is in the log the day it is written, because it
/// cannot reach the window any other way.
/// </para>
/// <para>
/// <strong>What it writes:</strong> what the window is doing, every refusal
/// with its reason, how a disc read back with the files that failed by name,
/// and how a set went. Those are the lines the night of 2026-11-10 had to be
/// rebuilt from screenshots. And the line under the version, so an update that
/// went wrong on somebody else's machine can be followed step by step.
/// </para>
/// <para>
/// Progress is written once a stage and once more each time its percentage
/// reaches a new tenth, which <see cref="TheProgressWorthWriting"/> decides. A
/// burn reports its percentage hundreds of times, and what anybody reading the
/// log afterwards wants is when each stage started and that it kept moving.
/// </para>
/// </remarks>
public sealed class WhatTheLogHears
{
    private readonly MakeADisc _screen;
    private readonly IKeepALog _log;
    private readonly Lock _oneAtATime = new();
    private readonly TheProgressWorthWriting _progress = new();
    private readonly TheProgressWorthWriting _versionClimb = new();
    private Refusal? _lastRefusal;
    private DiscCheck? _lastCheck;
    private TheSetSoFar? _lastSet;
    private string? _lastBurn;
    private string? _lastVersion;

    private WhatTheLogHears(MakeADisc screen, IKeepALog log)
    {
        _screen = screen;
        _log = log;
    }

    /// <summary>Starts writing down what this screen says, for as long as it lives.</summary>
    public static void Listen(MakeADisc screen, IKeepALog log)
    {
        WhatTheLogHears ears = new(screen, log);
        screen.PropertyChanged += ears.Heard;
    }

    private void Heard(object? sender, PropertyChangedEventArgs e)
    {
        lock (_oneAtATime)
        {
            switch (e.PropertyName)
            {
                case nameof(MakeADisc.Doing):
                    Doing(_screen.Doing);
                    break;

                case nameof(MakeADisc.ItWentWrong):
                    if (_screen.ItWentWrong is Refusal refused && !Equals(refused, _lastRefusal))
                    {
                        _log.Write($"Refused: {refused.Headline}. {refused.Because}");
                    }

                    _lastRefusal = _screen.ItWentWrong;
                    break;

                case nameof(MakeADisc.HowTheDiscCameOut):
                    if (_screen.HowTheDiscCameOut is DiscCheck came && !ReferenceEquals(came, _lastCheck))
                    {
                        _log.Write(AboutTheCheck(came));
                    }

                    _lastCheck = _screen.HowTheDiscCameOut;
                    break;

                case nameof(MakeADisc.HowTheSetWent):
                    if (_screen.HowTheSetWent is TheSetSoFar set && !Equals(set, _lastSet))
                    {
                        _log.Write($"The set: {set.Written} of {set.OfDiscs} written"
                            + (set.BroughtForward > 0 ? $", {set.BroughtForward} of them before this run" : string.Empty)
                            + (set.WhyItStopped is string why ? $", stopped because {why}." : "."));
                    }

                    _lastSet = _screen.HowTheSetWent;
                    break;

                case nameof(MakeADisc.HowTheBurnWent):
                    string burn = _screen.HowTheBurnWent;

                    // Stryker's "burn == _lastBurn" survives here and no test
                    // can kill it. The window announces a new result twice,
                    // once as it lands and again from Started as the run ends,
                    // so writing only on the repeat writes the same one line a
                    // moment later. The comparison is what keeps it one line.
                    if (burn.Length > 0 && burn != _lastBurn)
                    {
                        _log.Write($"Shown: {burn}");
                    }

                    _lastBurn = burn;
                    break;

                case nameof(MakeADisc.WhatTheVersionSays):
                    Version(_screen.WhatTheVersionSays);
                    break;
            }
        }
    }

    private void Doing(string doing)
    {
        if (_progress.Worth(doing))
        {
            _log.Write($"Doing: {doing}");
        }
    }

    /// <summary>
    /// The line under the version: what asking GitHub found, and every step of
    /// an update, from the press to whether the download matched.
    /// </summary>
    /// <remarks>
    /// A download reports its bytes hundreds of times, so its percentage is
    /// written at every new tenth, the way a burn's is. Any other line is
    /// written whenever its words change, including a line that differs only
    /// in a version number, which is exactly the difference that matters here.
    /// </remarks>
    private void Version(string said)
    {
        bool climbed = _versionClimb.Worth(said);

        if (said.Length > 0 && said != _lastVersion
            && (climbed || TheProgressWorthWriting.TenthOf(said) < 0))
        {
            _log.Write($"Version: {said}");
        }

        _lastVersion = said;
    }

    /// <summary>
    /// How a disc read back, naming every file that failed, because the
    /// window shows the first few and the log is where the rest go.
    /// </summary>
    public static string AboutTheCheck(DiscCheck came)
    {
        List<string> parts = [$"Read back: {came.Matched} matched"];

        if (came.Unreadable.Count > 0)
        {
            parts.Add($"unreadable: {string.Join(", ", came.Unreadable)}");
        }

        if (came.Wrong.Count > 0)
        {
            parts.Add($"different: {string.Join(", ", came.Wrong)}");
        }

        if (came.Missing.Count > 0)
        {
            parts.Add($"missing: {string.Join(", ", came.Missing)}");
        }

        if (came.Unexpected.Count > 0)
        {
            parts.Add($"not expected: {string.Join(", ", came.Unexpected)}");
        }

        if (came.WhyNot is string why)
        {
            parts.Add($"could not check: {why}");
        }

        return string.Join("; ", parts) + (came.Good ? ". Good." : ". NOT GOOD.");
    }

    /// <inheritdoc cref="TheProgressWorthWriting.ShapeOf"/>
    public static string ShapeOf(string doing) => TheProgressWorthWriting.ShapeOf(doing);
}
