using System.Net;
using System.Net.Http;
using System.Text;
using DiscWright.Core;
using DiscWright.Platform;

namespace DiscWright.Tests;

/// <summary>
/// What the asking does when the asking cannot be done.
/// </summary>
/// <remarks>
/// <para>
/// This is a program for putting things on a disc so they keep working with
/// no network at all, so a machine with no network is not an edge case here.
/// <strong>Nothing may break, and nothing may throw.</strong>
/// </para>
/// <para>
/// The real class, with a stubbed way out. Testing a hand written stand-in
/// would prove only that the stand-in behaves, and the catches being tested
/// are in the real one.
/// </para>
/// </remarks>
public class WhenThereIsNoInternet
{
    [Fact]
    public async Task Says_there_is_no_way_out_rather_than_throwing()
    {
        using WhatGitHubHasReleased asking = Through(new ItFellOver(new HttpRequestException("no such host")));

        WhatWasReleased said = await asking.TheLatest(CancellationToken.None);

        Assert.Null(said.Tag);
        Assert.Equal("There is no way out to the internet from here.", said.WhyNot);
    }

    [Fact]
    public async Task Says_it_took_too_long_rather_than_hanging_about()
    {
        // What a dropped connection looks like from the inside: the request
        // is cancelled by its own timeout rather than by anybody asking.
        using WhatGitHubHasReleased asking = Through(new ItFellOver(new TaskCanceledException("timed out")));

        WhatWasReleased said = await asking.TheLatest(CancellationToken.None);

        Assert.Equal("It took too long to answer.", said.WhyNot);
    }

    [Fact]
    public async Task Says_when_github_is_asking_for_a_rest()
    {
        using WhatGitHubHasReleased asking = Through(new ItAnswered(HttpStatusCode.Forbidden, "{}"));

        WhatWasReleased said = await asking.TheLatest(CancellationToken.None);

        Assert.Equal("GitHub is asking for a rest. Try again in a few minutes.", said.WhyNot);
    }

    [Fact]
    public async Task Says_when_nothing_has_been_published_there()
    {
        using WhatGitHubHasReleased asking = Through(new ItAnswered(HttpStatusCode.NotFound, "{}"));

        WhatWasReleased said = await asking.TheLatest(CancellationToken.None);

        Assert.Equal("Nothing has been published there yet.", said.WhyNot);
    }

    [Fact]
    public async Task Says_when_what_came_back_was_not_readable()
    {
        using WhatGitHubHasReleased asking = Through(new ItAnswered(HttpStatusCode.OK, "<html>a login page</html>"));

        // A captive portal in a hotel answers every request with its own page
        // and a perfectly cheerful 200.
        WhatWasReleased said = await asking.TheLatest(CancellationToken.None);

        Assert.Equal("What came back was not something this could read.", said.WhyNot);
    }

    [Fact]
    public async Task Reads_the_tag_and_the_page_when_it_does_work()
    {
        using WhatGitHubHasReleased asking = Through(new ItAnswered(
            HttpStatusCode.OK,
            """{"tag_name":"v2.1.0","html_url":"https://github.com/x/y/releases/tag/v2.1.0","other":1}"""));

        WhatWasReleased said = await asking.TheLatest(CancellationToken.None);

        Assert.Null(said.WhyNot);
        Assert.Equal("v2.1.0", said.Tag);
        Assert.Equal("https://github.com/x/y/releases/tag/v2.1.0", said.Where);
    }

    [Fact]
    public async Task Minds_nothing_when_the_answer_is_missing_the_bits_it_wants()
    {
        using WhatGitHubHasReleased asking = Through(new ItAnswered(HttpStatusCode.OK, """{"nothing":"useful"}"""));

        WhatWasReleased said = await asking.TheLatest(CancellationToken.None);

        // Valid JSON saying nothing is not a failure to ask, and the
        // comparison is built to cope with knowing no tag.
        Assert.Null(said.WhyNot);
        Assert.Null(said.Tag);
        Assert.Null(said.Where);
    }

    [Fact]
    public async Task Still_stops_when_somebody_actually_cancels()
    {
        using CancellationTokenSource stopping = new();
        await stopping.CancelAsync();

        using WhatGitHubHasReleased asking = Through(new ItFellOver(new TaskCanceledException("stopped")));

        // Being cancelled on purpose is not the same as timing out, and
        // swallowing it would make a stop button that does nothing.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => asking.TheLatest(stopping.Token));
    }

    private static WhatGitHubHasReleased Through(HttpMessageHandler handler) => new(new HttpClient(handler));

    private sealed class ItFellOver(Exception how) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            throw how;
        }
    }

    private sealed class ItAnswered(HttpStatusCode code, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(code)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }
}
