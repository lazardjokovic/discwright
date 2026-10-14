using DiscWright.Core;
using DiscWright.Core.Staging;

namespace DiscWright.Tests;

public class TheBuildPipeline
{
    [Fact]
    public async Task Knows_how_many_steps_apply_before_it_starts_any_of_them()
    {
        RecordedProgress watched = new();

        await Pipeline(Step("copying the files"), Step("writing the menu"), Step("writing the checksums"))
            .Run(Context(), watched, CancellationToken.None);

        // The whole reason a step is an object. Nobody estimates anything: the
        // count is known before the first one runs, which is the install
        // window's missing bar solved by shape rather than by arithmetic.
        Assert.Equal(
            ["step 1 of 3, copying the files", "step 2 of 3, writing the menu", "step 3 of 3, writing the checksums"],
            watched.Lines);
    }

    [Fact]
    public async Task Leaves_a_step_with_nothing_to_do_out_of_the_count_entirely()
    {
        RecordedProgress watched = new();

        await Pipeline(Step("copying the files"), Step("writing the menu", applies: false), Step("writing the checksums"))
            .Run(Context(), watched, CancellationToken.None);

        // Not "step 2 of 3, skipped". A person watching should see the work
        // that is going to happen, not the work that was considered.
        Assert.Equal(
            ["step 1 of 2, copying the files", "step 2 of 2, writing the checksums"],
            watched.Lines);
    }

    [Fact]
    public async Task Says_what_it_is_about_to_do_before_it_does_it()
    {
        List<string> order = [];
        RecordedProgress watched = new() { Also = order.Add };

        await Pipeline(
                Step("copying the files", onRun: () => order.Add("ran: copying the files")),
                Step("writing the menu", onRun: () => order.Add("ran: writing the menu")))
            .Run(Context(), watched, CancellationToken.None);

        // A step that takes a minute would otherwise sit at the previous step's
        // wording for the whole minute and then jump two at once.
        Assert.Equal(
            [
                "step 1 of 2, copying the files",
                "ran: copying the files",
                "step 2 of 2, writing the menu",
                "ran: writing the menu"
            ],
            order);
    }

    [Fact]
    public async Task Runs_the_steps_in_the_order_it_was_given()
    {
        List<string> order = [];

        await Pipeline(
                Step("one", onRun: () => order.Add("one")),
                Step("two", onRun: () => order.Add("two")),
                Step("three", onRun: () => order.Add("three")))
            .Run(Context(), null, CancellationToken.None);

        // The order is not decoration: the checksum step hashes what the copy
        // step wrote, so the wrong way round produces a list of hashes for an
        // empty folder and still looks like it worked.
        Assert.Equal(["one", "two", "three"], order);
    }

    [Fact]
    public async Task Stops_when_it_is_cancelled()
    {
        using CancellationTokenSource cancelling = new();
        List<string> order = [];

        BuildPipeline pipeline = Pipeline(
            Step("one", onRun: () => { order.Add("one"); cancelling.Cancel(); }),
            Step("two", onRun: () => order.Add("two")));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pipeline.Run(Context(), null, cancelling.Token));

        Assert.Equal(["one"], order);
    }

    [Fact]
    public void Can_say_what_it_would_do_without_doing_it()
    {
        IReadOnlyList<string> would = Pipeline(
                Step("copying the files"),
                Step("writing the menu", applies: false),
                Step("writing the checksums"))
            .WouldDo(Context());

        Assert.Equal(["copying the files", "writing the checksums"], would);
    }

    // ------------------------------------------------------------------ helpers

    private static BuildPipeline Pipeline(params IBuildStep[] steps) => new(steps);

    private static ScriptedStep Step(string says, bool applies = true, Action? onRun = null) =>
        new ScriptedStep(says, applies, onRun);

    private static BuildContext Context() =>
        new(new DiscProject("TEST", [], new DiscOptions()),
            new DiscLayout([new PlannedDisc(1, [], 0)], 1000, 0),
            new PlannedDisc(1, [], 0),
            @"C:\stage",
            new RecordingWriter());

    private sealed class ScriptedStep(string says, bool applies, Action? onRun) : IBuildStep
    {
        public string Says => says;

        public bool AppliesTo(BuildContext context) => applies;

        public Task Run(BuildContext context, CancellationToken ct)
        {
            onRun?.Invoke();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordedProgress : IProgress<BuildProgress>
    {
        public List<string> Lines { get; } = [];

        public Action<string>? Also { get; init; }

        public void Report(BuildProgress value)
        {
            Lines.Add(value.ToString());
            Also?.Invoke(value.ToString());
        }
    }
}
