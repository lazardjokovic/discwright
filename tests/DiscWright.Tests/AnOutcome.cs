using DiscWright.Core;

namespace DiscWright.Tests;

public class AnOutcome
{
    [Fact]
    public void Carries_the_answer_when_it_worked()
    {
        Outcome<int> outcome = 7;

        Outcome<int>.Ok ok = Assert.IsType<Outcome<int>.Ok>(outcome);
        Assert.Equal(7, ok.Value);
    }

    [Fact]
    public void Carries_the_refusal_when_it_did_not()
    {
        Refusal refusal = new("That file will not fit on one disc", "setup_the_witcher_3.bin is 6.1 GB");

        Outcome<int> outcome = refusal;

        Outcome<int>.No no = Assert.IsType<Outcome<int>.No>(outcome);
        Assert.Same(refusal, no.Refusal);
    }

    [Fact]
    public void Has_exactly_two_cases_so_a_switch_over_it_can_be_checked()
    {
        // The constructor is private, so only the two nested records can derive
        // from it. This asserts the closed set rather than trusting that nobody
        // adds a third from somewhere else, because the day somebody does, every
        // switch in the app silently gains an unhandled path.
        Type[] cases =
        [
            .. typeof(Outcome<int>).Assembly
                .GetTypes()
                .Where(type => type.BaseType is not null && type.BaseType.Name == typeof(Outcome<>).Name)
        ];

        Assert.Equal(2, cases.Length);
    }

    [Fact]
    public void Describes_a_refusal_with_both_numbers_and_a_way_out()
    {
        Refusal refusal = new(
            "That file will not fit on one disc",
            "setup_the_witcher_3_wild_hunt_goty-1.bin is bigger than any DVD",
            [new Figure("the file", "6.1 GB"), new Figure("a DVD holds", "4.37 GiB")],
            new Remedy("Use a double layer DVD, 8.5 GB", RemedyKind.ChangeMedium));

        // This is the shape every refusal in the design promises: it names the
        // thing, gives both numbers, and offers the fix.
        Assert.Contains("-1.bin", refusal.Because);
        Assert.Equal(2, refusal.Figures.Count);
        Assert.Equal(RemedyKind.ChangeMedium, refusal.Remedy?.Kind);
    }

    [Fact]
    public void Can_be_a_refusal_with_nothing_to_count()
    {
        Refusal refusal = new("That folder holds no game", "Nothing in it looks like an installer or a program");

        Assert.Empty(refusal.Figures);
        Assert.Null(refusal.Remedy);
        Assert.NotEmpty(refusal.Headline);
        Assert.NotEmpty(refusal.Because);
    }
}
