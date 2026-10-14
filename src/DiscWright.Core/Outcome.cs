namespace DiscWright.Core;

/// <summary>
/// One thing or the other: an answer, or a refusal that explains itself.
/// </summary>
/// <remarks>
/// <para>
/// The caller cannot reach the value without dealing with the refusal, which is
/// the whole reason this is not an exception. A file too big for a disc is an
/// <em>answer</em>, not a failure. The shipped app learned that the hard way:
/// thrown, a refusal arrived in front of the words "The build failed:", in
/// front of a sentence calmly explaining that one file was too big.
/// </para>
/// <para>
/// The constructor is private, so the only two cases are the two nested here.
/// Nothing elsewhere can add a third, which is what makes a <c>switch</c> over
/// an outcome something the compiler can check.
/// </para>
/// </remarks>
public abstract record Outcome<T>
{
    private Outcome()
    {
    }

    /// <summary>It worked, and here is the answer.</summary>
    public sealed record Ok(T Value) : Outcome<T>;

    /// <summary>It will not work, and here is why, with the numbers.</summary>
    public sealed record No(Refusal Refusal) : Outcome<T>;

    /// <summary>
    /// So a method whose return type is <c>Outcome&lt;DiscLayout&gt;</c> can
    /// simply <c>return layout;</c> or <c>return refusal;</c>.
    /// </summary>
    /// <remarks>
    /// The first attempt here was a pair of static <c>From</c> methods, and the
    /// analyzer refused them under CA1000. It was right: <c>Outcome&lt;T&gt;.From(x)</c>
    /// still makes the caller name the type argument, so it was no shorter than
    /// naming the case and bought nothing. A conversion is inferred, so the
    /// noise goes away at every return in the app.
    /// </remarks>
    public static implicit operator Outcome<T>(T value) => new Ok(value);

    /// <inheritdoc cref="op_Implicit(T)"/>
    public static implicit operator Outcome<T>(Refusal refusal) => new No(refusal);
}
