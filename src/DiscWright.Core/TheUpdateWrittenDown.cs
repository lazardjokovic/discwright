namespace DiscWright.Core;

/// <summary>
/// Updating this copy, done the usual way, with every step written in the log.
/// </summary>
/// <remarks>
/// <para>
/// <strong>For the report that says "Update did nothing".</strong> The line
/// under the version says what a person needs, "The download did not finish",
/// and nothing about why: which address, how far it got, what Windows or the
/// network said. That is what this writes, one line a step, so a failed update
/// on somebody else's machine can be read from their log rather than guessed.
/// </para>
/// <para>
/// What the window said about it, the percentage and whether the download
/// matched its checksum, reaches the log the way every other sentence does,
/// through <c>WhatTheLogHears</c>. This writes what the window never shows.
/// </para>
/// <para>
/// Nothing is caught for good. Every failure is written and thrown on
/// unchanged, so the window decides exactly as it would without a log.
/// </para>
/// </remarks>
/// <param name="updater">The update, done the real way.</param>
/// <param name="log">Where each step goes.</param>
public sealed class TheUpdateWrittenDown(IUpdateThisCopy updater, IKeepALog log) : IUpdateThisCopy
{
    private readonly Lock _oneAtATime = new();
    private bool? _lastSaidItCan;

    /// <inheritdoc />
    /// <remarks>
    /// Written the first time and whenever it changes, because "there was no
    /// Update, only the page" is the other report worth answering from a log.
    /// </remarks>
    public bool CanUpdateItself
    {
        get
        {
            bool can = updater.CanUpdateItself;

            lock (_oneAtATime)
            {
                if (_lastSaidItCan == can)
                {
                    return can;
                }

                _lastSaidItCan = can;
            }

            log.Write(can
                ? "Update: this copy was installed, so it can update itself."
                : "Update: this copy was not installed by the installer, so it is offered the page instead.");

            return can;
        }
    }

    /// <inheritdoc />
    public string WhereTheDownloadGoes(string named) => updater.WhereTheDownloadGoes(named);

    /// <inheritdoc />
    public async Task<string> Read(string url, CancellationToken ct)
    {
        log.Write($"Update: reading the checksums at {url}");

        try
        {
            string sums = await updater.Read(url, ct);
            log.Write($"Update: the checksums list {Lines(sums)} file(s).");
            return sums;
        }
        catch (Exception wrong)
        {
            log.Write($"Update: reading the checksums failed, {Why(wrong)}");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task Download(string url, string into, IProgress<long> sofar, CancellationToken ct)
    {
        log.Write($"Update: downloading {url} into {into}");

        HowFar far = new(sofar);

        try
        {
            await updater.Download(url, into, far, ct);
            log.Write($"Update: downloaded {far.Bytes} bytes.");
        }
        catch (Exception wrong)
        {
            log.Write($"Update: the download stopped after {far.Bytes} bytes, {Why(wrong)}");
            throw;
        }
    }

    /// <inheritdoc />
    public void ThrowAway(string path)
    {
        try
        {
            updater.ThrowAway(path);
            log.Write($"Update: threw away {path}");
        }
        catch (Exception wrong)
        {
            log.Write($"Update: could not throw away {path}, {Why(wrong)}");
            throw;
        }
    }

    /// <inheritdoc />
    public void InstallAndClose(string installer)
    {
        log.Write($"Update: starting {installer} and closing.");

        try
        {
            updater.InstallAndClose(installer);
        }
        catch (Exception wrong)
        {
            log.Write($"Update: the installer would not start, {Why(wrong)}");
            throw;
        }
    }

    /// <summary>The exception in one line: what it was and what it said.</summary>
    public static string Why(Exception wrong) =>
        wrong is OperationCanceledException
            ? "because it was cancelled."
            : $"{wrong.GetType().Name}: {wrong.Message}";

    private static int Lines(string sums) =>
        sums.Split('\n').Count(line => line.Trim().Length > 0);

    /// <summary>Passes every report on, remembering the last.</summary>
    private sealed class HowFar(IProgress<long> sofar) : IProgress<long>
    {
        private long _bytes;

        public long Bytes => Interlocked.Read(ref _bytes);

        public void Report(long value)
        {
            Interlocked.Exchange(ref _bytes, value);
            sofar.Report(value);
        }
    }
}
