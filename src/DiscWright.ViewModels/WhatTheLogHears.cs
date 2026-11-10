using System.ComponentModel;
using System.Text.RegularExpressions;
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
/// rebuilt from screenshots.
/// </para>
/// <para>
/// A line about progress that differs from the last only in its numbers is
/// not written again. A burn reports its percentage hundreds of times, and
/// what anybody reading the log afterwards wants is when each stage started.
/// </para>
/// </remarks>
public sealed partial class WhatTheLogHears
{
    private readonly MakeADisc _screen;
    private readonly IKeepALog _log;
    private readonly Lock _oneAtATime = new();
    private string? _lastDoing;
    private Refusal? _lastRefusal;
    private DiscCheck? _lastCheck;
    private TheSetSoFar? _lastSet;
    private string? _lastBurn;

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

                    if (burn.Length > 0 && burn != _lastBurn)
                    {
                        _log.Write($"Shown: {burn}");
                    }

                    _lastBurn = burn;
                    break;
            }
        }
    }

    private void Doing(string doing)
    {
        if (doing.Length == 0)
        {
            return;
        }

        string shape = ShapeOf(doing);

        if (shape == _lastDoing)
        {
            return;
        }

        _lastDoing = shape;
        _log.Write($"Doing: {doing}");
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

    /// <summary>
    /// A progress line with its numbers taken out, so that 45% and 46% of the
    /// same stage are the same line and the next stage is a new one.
    /// </summary>
    public static string ShapeOf(string doing) => Numbers().Replace(doing, "#");

    [GeneratedRegex(@"\d+")]
    private static partial Regex Numbers();
}
