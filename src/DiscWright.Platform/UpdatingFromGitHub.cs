using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using DiscWright.Core;

namespace DiscWright.Platform;

/// <summary>
/// Fetches a newer installer from the release and runs it, when somebody
/// presses Update.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Only a copy the installer put here.</strong> Inno writes its
/// uninstaller beside the program, <c>unins000.exe</c>, and a portable copy
/// or a build out of the repository has none. So that is the test, and a copy
/// without one is offered the release page instead.
/// </para>
/// <para>
/// <strong>Only from this program's releases.</strong> <see cref="TheUpdate"/>
/// already refuses any other address, and this refuses it again, because the
/// one thing worse than a button that does not update is one that runs a file
/// from somewhere else.
/// </para>
/// <para>
/// The installer is started with <c>/SILENT</c>, which shows its progress and
/// asks nothing: on an upgrade Inno keeps the folder and the choices made the
/// first time. <c>/RELAUNCH</c> is this program's own switch, read by
/// <c>packaging/DiscWright.iss</c>, and opens the new copy once it is in.
/// </para>
/// </remarks>
public sealed class UpdatingFromGitHub : IUpdateThisCopy, IDisposable
{
    /// <summary>What the installer is started with.</summary>
    public const string Quietly = "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RELAUNCH";

    private readonly string _here;
    private readonly string _downloads;
    private readonly Action _close;
    private readonly HttpClient _http;

    /// <param name="here">The folder the running program is in.</param>
    /// <param name="close">What closes the program, once the installer has started.</param>
    /// <param name="downloads">Where downloads wait to run; the temporary folder when not given.</param>
    /// <param name="http">For tests; a real client when not given.</param>
    public UpdatingFromGitHub(string here, Action close, string? downloads = null, HttpClient? http = null)
    {
        _here = here;
        _close = close;
        _downloads = downloads ?? Path.Combine(Path.GetTempPath(), "DiscWright update");
        _http = http ?? new HttpClient();

        // Long enough for a slow line to start answering. The download itself
        // is read as a stream after the answer starts, which this does not
        // limit, so a slow line still gets there.
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DiscWright");
    }

    /// <inheritdoc />
    public bool CanUpdateItself =>
        Directory.Exists(_here) && Directory.EnumerateFiles(_here, "unins*.exe").Any();

    /// <inheritdoc />
    /// <remarks>
    /// Anything an earlier press left there goes first, so the folder never
    /// holds more than the one installer on its way.
    /// </remarks>
    public string WhereTheDownloadGoes(string named)
    {
        Directory.CreateDirectory(_downloads);

        foreach (string old in Directory.EnumerateFiles(_downloads))
        {
            try
            {
                File.Delete(old);
            }
            catch (Exception wrong) when (wrong is IOException or UnauthorizedAccessException)
            {
                // Still running, perhaps, from a press a moment ago. It is
                // replaced by name below or left for Windows to tidy.
            }
        }

        return Path.Combine(_downloads, Path.GetFileName(named));
    }

    /// <summary>
    /// Deletes what an earlier update left behind, the installer it ran.
    /// </summary>
    /// <remarks>
    /// Called as the program starts. The start straight after an update is
    /// usually too soon, because that installer is still finishing and its file
    /// will not delete, so it goes at the start after. Nothing here can throw.
    /// </remarks>
    public void ClearWhatAnEarlierUpdateLeft()
    {
        try
        {
            if (Directory.Exists(_downloads))
            {
                Directory.Delete(_downloads, recursive: true);
            }
        }
        catch (Exception wrong) when (wrong is IOException or UnauthorizedAccessException)
        {
            // Still in use. The next start tries again.
        }
    }

    /// <inheritdoc />
    public async Task<string> Read(string url, CancellationToken ct)
    {
        Ours(url);
        return await _http.GetStringAsync(url, ct);
    }

    /// <inheritdoc />
    public async Task Download(string url, string into, IProgress<long> sofar, CancellationToken ct)
    {
        Ours(url);

        using HttpResponseMessage answer = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        answer.EnsureSuccessStatusCode();

        await using Stream from = await answer.Content.ReadAsStreamAsync(ct);
        await using FileStream to = new(into, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);

        byte[] buffer = new byte[1 << 16];
        long arrived = 0;
        long said = 0;
        int read;

        while ((read = await from.ReadAsync(buffer, ct)) > 0)
        {
            await to.WriteAsync(buffer.AsMemory(0, read), ct);
            arrived += read;

            // Every half a megabyte, which moves the percentage on a 25 MB
            // installer without telling the window thousands of times.
            if (arrived - said >= 1 << 19)
            {
                sofar.Report(arrived);
                said = arrived;
            }
        }

        sofar.Report(arrived);
    }

    /// <inheritdoc />
    public void ThrowAway(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <inheritdoc />
    public void InstallAndClose(string installer)
    {
        using Process? started = Process.Start(new ProcessStartInfo(installer, Quietly)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(installer) ?? _downloads,
        });

        if (started is null)
        {
            throw new InvalidOperationException("The installer did not start.");
        }

        _close();
    }

    private static void Ours(string url)
    {
        if (!url.StartsWith(TheUpdate.Downloads, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Not one of this program's releases: {url}");
        }
    }

    public void Dispose() => _http.Dispose();
}
