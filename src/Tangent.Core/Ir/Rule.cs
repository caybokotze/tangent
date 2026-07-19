using System;
using Tangent.Core.Text;

namespace Tangent.Core.Ir;

/// <summary>
/// A stable rule identifier such as <c>Controller/ctor/injects-only#0</c>. Derived from the contract, the
/// selector, the predicate kind and an ordinal, never from a source position, so it survives reformatting.
/// </summary>
public readonly struct RuleId : IEquatable<RuleId>
{
    /// <summary>The identifier text.</summary>
    public string Value { get; }

    /// <summary>Wraps an identifier string.</summary>
    public RuleId(string value) => Value = value ?? string.Empty;

    /// <inheritdoc/>
    public bool Equals(RuleId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is RuleId other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Value is null ? 0 : Value.GetHashCode();

    /// <inheritdoc/>
    public override string ToString() => Value ?? string.Empty;

    /// <summary>Equality operator.</summary>
    public static bool operator ==(RuleId left, RuleId right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(RuleId left, RuleId right) => !left.Equals(right);
}

/// <summary>What a rule is evaluated against: a member kind plus an optional refinement condition.</summary>
public readonly struct Selector : IEquatable<Selector>
{
    /// <summary>The kind of entity selected.</summary>
    public MemberKind Kind { get; }

    /// <summary>Index into <see cref="ArchitectureModel.Conditions"/>, or -1 when the selector is unconditional.</summary>
    public int ConditionIndex { get; }

    /// <summary>Creates a selector.</summary>
    public Selector(MemberKind kind, int conditionIndex)
    {
        Kind = kind;
        ConditionIndex = conditionIndex;
    }

    /// <summary>The selector matching the bound type itself.</summary>
    public static Selector Type => new Selector(MemberKind.Type, -1);

    /// <inheritdoc/>
    public bool Equals(Selector other) => Kind == other.Kind && ConditionIndex == other.ConditionIndex;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Selector other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => ((int)Kind * 397) ^ ConditionIndex;

    /// <summary>Equality operator.</summary>
    public static bool operator ==(Selector left, Selector right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(Selector left, Selector right) => !left.Equals(right);
}

/// <summary>A tagged union: a predicate kind plus up to two payload slots interpreted per kind.</summary>
public readonly struct Predicate : IEquatable<Predicate>
{
    /// <summary>The discriminator.</summary>
    public PredicateKind Kind { get; }

    /// <summary>The first payload; see <see cref="PredicateKind"/> for the meaning per kind.</summary>
    public int Payload { get; }

    /// <summary>The second payload; see <see cref="PredicateKind"/> for the meaning per kind.</summary>
    public int Payload2 { get; }

    /// <summary>Creates a predicate.</summary>
    public Predicate(PredicateKind kind, int payload, int payload2 = 0)
    {
        Kind = kind;
        Payload = payload;
        Payload2 = payload2;
    }

    /// <inheritdoc/>
    public bool Equals(Predicate other) => Kind == other.Kind && Payload == other.Payload && Payload2 == other.Payload2;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Predicate other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => (((int)Kind * 397) ^ Payload) * 397 ^ Payload2;

    /// <summary>Equality operator.</summary>
    public static bool operator ==(Predicate left, Predicate right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(Predicate left, Predicate right) => !left.Equals(right);
}

/// <summary>One bound rule: polarity, selector, predicate, tier, specificity and origin.</summary>
public readonly struct Rule
{
    /// <summary>The stable rule id.</summary>
    public RuleId Id { get; }

    /// <summary>Whether the predicate must hold or must not hold.</summary>
    public Polarity Polarity { get; }

    /// <summary>What the rule is evaluated against.</summary>
    public Selector Selector { get; }

    /// <summary>What is checked.</summary>
    public Predicate Predicate { get; }

    /// <summary>How expensive the rule is to evaluate.</summary>
    public RuleTier Tier { get; }

    /// <summary>Where the rule was written.</summary>
    public SourceSpan Span { get; }

    /// <summary>How narrow the selector is; larger wins when refinements nest.</summary>
    public int Specificity { get; }

    /// <summary>The index of the contract the rule was declared in, which may be an ancestor.</summary>
    public int OriginContract { get; }

    /// <summary>Creates a rule.</summary>
    public Rule(
        RuleId id,
        Polarity polarity,
        Selector selector,
        Predicate predicate,
        RuleTier tier,
        SourceSpan span,
        int specificity,
        int originContract)
    {
        Id = id;
        Polarity = polarity;
        Selector = selector;
        Predicate = predicate;
        Tier = tier;
        Span = span;
        Specificity = specificity;
        OriginContract = originContract;
    }
}
