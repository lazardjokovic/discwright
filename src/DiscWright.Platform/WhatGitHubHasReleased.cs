using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using DiscWright.Core;

namespace DiscWright.Platform;

/// <summary>
/// Asks GitHub which release is newest.
/// </summary>
/// <remarks>
/// <para>
/// One request, to a public endpoint, with no account and no key. GitHub
/// allows sixty of those an hour from one address, which nobody pressing a
/// button will ever reach.
/// </para>
/// <para>
/// <strong>Nothing identifying goes with it.</strong> The user agent is the
/// program's name, which GitHub requires and which says nothing about who is
/// running it.
/// </para>
/// </remarks>
public sealed class WhatGitHubHasReleased : IAskWhatIsReleased, IDisposable
{
    /// <summary>Where 2.0 will be published, which is where v1 already is.</summary>
    private const string Latest = "https://api.github.com/repos/lazardjokovic/discwright/releases/latest";

    private readonly HttpClient _http;

    public WhatGitHubHasReleased(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();

        // Long enough for a slow line, short enough that nobody wonders
        // whether the button did anything.
        _http.Timeout = TimeSpan.FromSeconds(12);

        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DiscWright");
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    /// <inheritdoc />
    public async Task<WhatWasReleased> TheLatest(CancellationToken ct)
    {
        try
        {
            using HttpResponseMessage answer = await _http.GetAsync(Latest, ct);

            // Each of these is an ordinary afternoon rather than a fault, and
            // each deserves its own sentence: somebody told "it did not work"
            // has no idea whether to try again in a minute or give up.
            if (answer.StatusCode == HttpStatusCode.NotFound)
            {
                return new WhatWasReleased(null, null, "Nothing has been published there yet.");
            }

            if (answer.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                return new WhatWasReleased(null, null, "GitHub is asking for a rest. Try again in a few minutes.");
            }

            if (!answer.IsSuccessStatusCode)
            {
                return new WhatWasReleased(null, null, $"GitHub answered {(int)answer.StatusCode}.");
            }

            using JsonDocument json = JsonDocument.Parse(await answer.Content.ReadAsStringAsync(ct));

            return new WhatWasReleased(
                Text(json.RootElement, "tag_name"),
                Text(json.RootElement, "html_url"),
                Files: FilesOf(json.RootElement));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new WhatWasReleased(null, null, "It took too long to answer.");
        }
        catch (HttpRequestException)
        {
            return new WhatWasReleased(null, null, "There is no way out to the internet from here.");
        }
        catch (JsonException)
        {
            return new WhatWasReleased(null, null, "What came back was not something this could read.");
        }
    }

    /// <summary>What the release says is attached to it, leaving out anything it does not say fully.</summary>
    private static List<AReleasedFile> FilesOf(JsonElement release)
    {
        List<AReleasedFile> files = [];

        if (!release.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return files;
        }

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            if (Text(asset, "name") is string name
                && Text(asset, "browser_download_url") is string url
                && asset.TryGetProperty("size", out JsonElement size)
                && size.TryGetInt64(out long bytes))
            {
                files.Add(new AReleasedFile(name, url, bytes));
            }
        }

        return files;
    }

    private static string? Text(JsonElement element, string named) =>
        element.TryGetProperty(named, out JsonElement found) && found.ValueKind == JsonValueKind.String
            ? found.GetString()
            : null;

    public void Dispose() => _http.Dispose();
}
