using System.Collections.Immutable;
using Tangent.Core.Matching;
using Tangent.Core.Text;
using Xunit;

namespace Tangent.Core.Tests;

/// <summary>Unit tests for the glob engine.</summary>
public sealed class GlobTests
{
    private static Glob Compile(string pattern) => Glob.Compile(pattern.AsSpan(), new StringTable());

    [Theory]
    [InlineData("Orders.Domain.**", "Orders.Domain", true)]
    [InlineData("Orders.Domain.**", "Orders.Domain.Entities", true)]
    [InlineData("Orders.Domain.**", "Orders.Domain.Entities.Nested", true)]
    [InlineData("Orders.Domain.**", "Orders.DomainX", false)]
    [InlineData("Orders.Domain.**", "Orders", false)]
    [InlineData("Orders.Domain.**", "Other.Orders.Domain", false)]
    [InlineData("Orders.*.Entities", "Orders.Api.Entities", true)]
    [InlineData("Orders.*.Entities", "Orders.Entities", false)]
    [InlineData("Orders.*.Entities", "Orders.A.B.Entities", false)]
    [InlineData("System.**", "System", true)]
    [InlineData("System.**", "System.Collections.Generic", true)]
    [InlineData("System.**", "SystemX", false)]
    [InlineData("**", "", true)]
    [InlineData("**", "Anything.At.All", true)]
    [InlineData("**.Entities", "Entities", true)]
    [InlineData("**.Entities", "A.B.Entities", true)]
    [InlineData("**.Entities", "A.B.EntitiesX", false)]
    [InlineData("Orders", "Orders", true)]
    [InlineData("Orders", "Orders.Api", false)]
    [InlineData("Orders.**.Entities.**", "Orders.A.Entities.B.C", true)]
    [InlineData("Orders.**.Entities.**", "Orders.Entities", true)]
    [InlineData("Orders.**.Entities.**", "Orders.A.B", false)]
    public void Matches(string pattern, string name, bool expected)
    {
        Assert.Equal(expected, Compile(pattern).Matches(name.AsSpan()));
    }

    [Fact]
    public void Compiles_segments()
    {
        Glob glob = Compile("Orders.*.Entities.**");
        Assert.Equal(4, glob.Segments.Length);
        Assert.Equal(GlobSegmentKind.Literal, glob.Segments[0].Kind);
        Assert.Equal(GlobSegmentKind.Star, glob.Segments[1].Kind);
        Assert.Equal(GlobSegmentKind.Literal, glob.Segments[2].Kind);
        Assert.Equal(GlobSegmentKind.DoubleStar, glob.Segments[3].Kind);
    }

    [Fact]
    public void Handles_more_segments_than_the_stack_buffer()
    {
        var name = string.Join(".", Enumerable.Range(0, 200).Select(i => "S" + i));
        Assert.True(Compile("S0.**").Matches(name.AsSpan()));
        Assert.False(Compile("S1.**").Matches(name.AsSpan()));
    }

    [Fact]
    public void Table_interns_identical_patterns()
    {
        var table = new GlobTable(new StringTable());
        int a = table.GetOrAdd("Orders.**".AsSpan());
        int b = table.GetOrAdd("Orders.**".AsSpan());
        int c = table.GetOrAdd("Orders.Api.**".AsSpan());
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(2, table.Count);
    }
}

/// <summary>Unit tests for type-pattern matching against C# display names.</summary>
public sealed class TypePatternTests
{
    private readonly StringTable _strings = new StringTable();
    private readonly TypePatternBuilder _builder;

    public TypePatternTests()
    {
        _builder = new TypePatternBuilder(_strings);
    }

    private int Named(string name, params int[] arguments) =>
        _builder.AddNamed(_strings.Intern(name), -1, arguments);

    private TypePatternTable Freeze() => _builder.ToTable(ImmutableArray<Template>.Empty);

    [Fact]
    public void Nested_generic_with_wildcard()
    {
        int pattern = Named("Task", Named("ActionResult", _builder.AddWildcard()));
        int group = _builder.AddGroup(new[] { pattern });
        TypePatternTable table = Freeze();

        Assert.True(table.MatchesGroup(group, "Task<ActionResult<OrderDto>>"));
        Assert.True(table.MatchesGroup(group, "System.Threading.Tasks.Task<ActionResult<OrderDto>>"));
        Assert.False(table.MatchesGroup(group, "Task<IActionResult>"));
        Assert.False(table.MatchesGroup(group, "Task"));
        Assert.False(table.MatchesGroup(group, "ValueTask<ActionResult<OrderDto>>"));
    }

    [Fact]
    public void Alternation_matches_either_side()
    {
        int a = Named("Task", Named("ActionResult", _builder.AddWildcard()));
        int b = Named("Task", Named("IActionResult"));
        int group = _builder.AddGroup(new[] { a, b });
        TypePatternTable table = Freeze();

        Assert.True(table.MatchesGroup(group, "Task<ActionResult<OrderDto>>"));
        Assert.True(table.MatchesGroup(group, "Task<IActionResult>"));
        Assert.False(table.MatchesGroup(group, "Task<Unit>"));
    }

    [Fact]
    public void Arity_must_match_exactly()
    {
        int pattern = Named("IRepository", _builder.AddWildcard());
        int group = _builder.AddGroup(new[] { pattern });
        TypePatternTable table = Freeze();

        Assert.True(table.MatchesGroup(group, "IRepository<Order>"));
        Assert.False(table.MatchesGroup(group, "IRepository"));
        Assert.False(table.MatchesGroup(group, "IRepository<Order, Guid>"));
    }

    [Fact]
    public void Bare_wildcard_matches_anything()
    {
        int group = _builder.AddGroup(new[] { _builder.AddWildcard() });
        TypePatternTable table = Freeze();

        Assert.True(table.MatchesGroup(group, "Task<ActionResult<OrderDto>>"));
        Assert.True(table.MatchesGroup(group, "int"));
    }

    [Fact]
    public void Simple_name_matches_any_namespace_but_dotted_name_must_match_a_suffix()
    {
        int simple = Named("ApiControllerBase");
        int dotted = Named("Api.ApiControllerBase");
        int simpleGroup = _builder.AddGroup(new[] { simple });
        int dottedGroup = _builder.AddGroup(new[] { dotted });
        TypePatternTable table = Freeze();

        Assert.True(table.MatchesGroup(simpleGroup, "ApiControllerBase"));
        Assert.True(table.MatchesGroup(simpleGroup, "Orders.Api.ApiControllerBase"));
        Assert.True(table.MatchesGroup(dottedGroup, "Orders.Api.ApiControllerBase"));
        Assert.True(table.MatchesGroup(dottedGroup, "Api.ApiControllerBase"));
        Assert.False(table.MatchesGroup(dottedGroup, "Orders.XApi.ApiControllerBase"));
        Assert.False(table.MatchesGroup(dottedGroup, "Orders.ApiControllerBase"));
    }

    [Fact]
    public void Display_name_with_metadata_arity_suffix()
    {
        int pattern = Named("IRepository", _builder.AddWildcard());
        int group = _builder.AddGroup(new[] { pattern });
        TypePatternTable table = Freeze();

        Assert.True(table.MatchesGroup(group, "IRepository`1<Order>"));
    }

    [Fact]
    public void Describe_round_trips()
    {
        int pattern = Named("Task", Named("ActionResult", _builder.AddWildcard()));
        int group = _builder.AddGroup(new[] { pattern });
        TypePatternTable table = Freeze();

        Assert.Equal("Task<ActionResult<*>>", table.DescribeGroup(group));
    }

    [Fact]
    public void Groups_are_interned()
    {
        int a = Named("Foo");
        int first = _builder.AddGroup(new[] { a });
        int second = _builder.AddGroup(new[] { a });
        Assert.Equal(first, second);
    }
}

/// <summary>Unit tests for attribute matching.</summary>
public sealed class AttributeGroupTests
{
    [Fact]
    public void Matches_with_or_without_the_attribute_suffix()
    {
        var strings = new StringTable();
        var group = AttributeGroup.Create(strings, ImmutableArray.Create(
            new AttributeAlternative(strings.Intern("HttpGet"), false),
            new AttributeAlternative(strings.Intern("HttpPost"), false)));

        Assert.True(group.Matches("HttpGet"));
        Assert.True(group.Matches("HttpGetAttribute"));
        Assert.True(group.Matches("Microsoft.AspNetCore.Mvc.HttpPostAttribute"));
        Assert.False(group.Matches("HttpPut"));
        Assert.Equal("[HttpGet | HttpPost]", group.Describe());
    }

    [Fact]
    public void Any_args_is_recorded_and_described()
    {
        var strings = new StringTable();
        var group = AttributeGroup.Create(strings, ImmutableArray.Create(
            new AttributeAlternative(strings.Intern("Route"), true)));

        Assert.True(group.Alternatives[0].AnyArgs);
        Assert.Equal("[Route(*)]", group.Describe());
    }
}

/// <summary>Unit tests for template expansion.</summary>
public sealed class TemplateTests
{
    private static Template Parse(string text) => Template.Parse(text.AsSpan(), new StringTable());

    [Theory]
    [InlineData("I{Name}", "OrderRepository", "Orders.Infrastructure", "IOrderRepository")]
    [InlineData("{Name}", "Order", "Orders.Domain", "Order")]
    [InlineData("{Namespace}.I{Name}", "Order", "Orders.Domain", "Orders.Domain.IOrder")]
    [InlineData("{Name:strip Service}Handler", "OrderService", "N", "OrderHandler")]
    [InlineData("{Name:strip Service}Handler", "Order", "N", "OrderHandler")]
    [InlineData("Plain", "Order", "N", "Plain")]
    [InlineData("I{Unknown}", "Order", "N", "I{Unknown}")]
    public void Expands(string template, string typeName, string ns, string expected)
    {
        Assert.Equal(expected, Parse(template).Expand(typeName, ns));
    }

    [Fact]
    public void Span_overload_reports_required_length()
    {
        Template template = Parse("I{Name}");
        Span<char> tooSmall = stackalloc char[3];
        Assert.False(template.Expand("OrderRepository".AsSpan(), ReadOnlySpan<char>.Empty, tooSmall, out int written));
        Assert.Equal("IOrderRepository".Length, written);

        Span<char> big = stackalloc char[32];
        Assert.True(template.Expand("OrderRepository".AsSpan(), ReadOnlySpan<char>.Empty, big, out written));
        Assert.Equal("IOrderRepository", big.Slice(0, written).ToString());
    }

    [Fact]
    public void Literal_templates_are_flagged()
    {
        Assert.True(Parse("IThing").IsLiteral);
        Assert.False(Parse("I{Name}").IsLiteral);
    }

    [Fact]
    public void Parts_are_exposed_in_order()
    {
        Template template = Parse("I{Name}Base");
        Assert.Equal(3, template.Parts.Length);
        Assert.Equal(TemplatePartKind.Literal, template.Parts[0].Kind);
        Assert.Equal(TemplatePartKind.Name, template.Parts[1].Kind);
        Assert.Equal(TemplatePartKind.Literal, template.Parts[2].Kind);
    }
}

/// <summary>Unit tests for the string table.</summary>
public sealed class StringTableTests
{
    [Fact]
    public void Interning_is_stable()
    {
        var table = new StringTable(16);
        StringId a = table.Intern("Orders.Api".AsSpan());
        StringId b = table.Intern("Orders.Api".AsSpan());
        StringId c = table.Intern("Orders.Domain".AsSpan());

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal("Orders.Api", table.GetString(a));
        Assert.True(table.GetSpan(c).SequenceEqual("Orders.Domain".AsSpan()));
        Assert.Equal(2, table.Count);
    }

    [Fact]
    public void Grows_and_rehashes()
    {
        var table = new StringTable(16);
        var ids = new StringId[500];
        for (int i = 0; i < ids.Length; i++) ids[i] = table.Intern(("Name" + i).AsSpan());

        Assert.Equal(500, table.Count);
        for (int i = 0; i < ids.Length; i++)
        {
            Assert.Equal("Name" + i, table.GetString(ids[i]));
            Assert.Equal(ids[i], table.Intern(("Name" + i).AsSpan()));
        }
    }

    [Fact]
    public void Invalid_ids_are_safe()
    {
        var table = new StringTable();
        Assert.False(StringId.Invalid.IsValid);
        Assert.Equal(string.Empty, table.GetString(StringId.Invalid));
        Assert.True(table.GetSpan(StringId.Invalid).IsEmpty);
    }

    [Fact]
    public void TryFind_does_not_add()
    {
        var table = new StringTable();
        Assert.False(table.TryFind("Missing".AsSpan(), out _));
        Assert.Equal(0, table.Count);

        StringId id = table.Intern("Present".AsSpan());
        Assert.True(table.TryFind("Present".AsSpan(), out StringId found));
        Assert.Equal(id, found);
    }
}
