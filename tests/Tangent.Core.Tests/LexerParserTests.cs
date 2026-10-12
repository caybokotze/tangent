using System.Collections.Immutable;
using Tangent.Core.Diagnostics;
using Tangent.Core.Ir;
using Tangent.Core.Syntax;
using Tangent.Core.Text;
using Xunit;

namespace Tangent.Core.Tests;

/// <summary>Focused tests for the lexer.</summary>
public sealed class LexerTests
{
    private static (Token[] Tokens, List<TangentDiagnostic> Diagnostics) Lex(string text)
    {
        var diagnostics = new List<TangentDiagnostic>();
        Token[] tokens = Lexer.Lex(text.AsSpan(), 0, "t.tan", diagnostics);
        return (tokens, diagnostics);
    }

    private static TokenKind[] Kinds(string text)
    {
        (Token[] tokens, _) = Lex(text);
        var kinds = new TokenKind[tokens.Length - 1]; // drop EndOfFile
        for (int i = 0; i < kinds.Length; i++) kinds[i] = tokens[i].Kind;
        return kinds;
    }

    [Fact]
    public void Keywords_and_identifiers()
    {
        Assert.Equal(
            new[] { TokenKind.Contract, TokenKind.Identifier, TokenKind.LeftBrace, TokenKind.Sealed, TokenKind.RightBrace },
            Kinds("contract Controller { sealed }"));
    }

    [Fact]
    public void Dotted_names_and_globs()
    {
        Assert.Equal(new[] { TokenKind.Identifier }, Kinds("Orders.Api.Controllers"));
        Assert.Equal(new[] { TokenKind.Glob }, Kinds("Orders.Api.**"));
        Assert.Equal(new[] { TokenKind.Glob }, Kinds("Orders.*.Entities"));
        Assert.Equal(new[] { TokenKind.Identifier }, Kinds("Orders"));
    }

    [Fact]
    public void Generic_close_is_never_a_shift()
    {
        Assert.Equal(
            new[]
            {
                TokenKind.Identifier, TokenKind.LessThan, TokenKind.Identifier, TokenKind.LessThan,
                TokenKind.Star, TokenKind.GreaterThan, TokenKind.GreaterThan,
            },
            Kinds("Task<ActionResult<*>>"));
    }

    [Fact]
    public void Templates_are_only_lexed_when_attached_to_a_name()
    {
        Assert.Equal(new[] { TokenKind.Template }, Kinds("I{Name}"));
        Assert.Equal(new[] { TokenKind.Template }, Kinds("X{Name:strip Suffix}"));
        Assert.Equal(new[] { TokenKind.Template }, Kinds("I{Name}Base"));
        Assert.Equal(new[] { TokenKind.LeftBrace, TokenKind.Name, TokenKind.RightBrace }, Kinds("{ name }"));
        Assert.Equal(new[] { TokenKind.Properties, TokenKind.LeftBrace, TokenKind.RightBrace }, Kinds("properties { }"));
    }

    [Fact]
    public void Hyphenated_keywords_and_arrows()
    {
        Assert.Equal(new[] { TokenKind.NullChecked }, Kinds("null-checked"));
        Assert.Equal(new[] { TokenKind.RequireEach }, Kinds("require-each"));
        Assert.Equal(new[] { TokenKind.Identifier, TokenKind.Arrow, TokenKind.Identifier }, Kinds("Api->Domain"));
        Assert.Equal(new[] { TokenKind.Identifier, TokenKind.Arrow, TokenKind.Nothing }, Kinds("Domain -> nothing"));
    }

    [Fact]
    public void Comments_are_skipped_and_lines_counted()
    {
        (Token[] tokens, List<TangentDiagnostic> diagnostics) = Lex("// one\n/* two\n   three */\nsealed");
        Assert.Empty(diagnostics);
        Assert.Equal(TokenKind.Sealed, tokens[0].Kind);
        Assert.Equal(4, tokens[0].Line);
        Assert.Equal(1, tokens[0].Column);
    }

    [Fact]
    public void Unterminated_block_comment_is_a_diagnostic()
    {
        (_, List<TangentDiagnostic> diagnostics) = Lex("/* never closed");
        Assert.Single(diagnostics);
        Assert.Equal("TNG1000", diagnostics[0].Code);
        Assert.Contains("block comment", diagnostics[0].Message);
    }

    [Fact]
    public void Strings_support_only_quote_and_backslash_escapes()
    {
        (Token[] tokens, List<TangentDiagnostic> diagnostics) = Lex("\"a\\\"b\\\\c\"");
        Assert.Empty(diagnostics);
        Assert.Equal(TokenKind.StringLiteral, tokens[0].Kind);
        ReadOnlySpan<char> content = Lexer.StringContent("\"a\\\"b\\\\c\"".AsSpan());
        Assert.Equal("a\"b\\c", Lexer.Unescape(content));

        (_, List<TangentDiagnostic> bad) = Lex("\"a\\nb\"");
        Assert.Single(bad);
        Assert.Contains("escape", bad[0].Message);
    }

    [Fact]
    public void Unterminated_string_stops_at_end_of_line()
    {
        (Token[] tokens, List<TangentDiagnostic> diagnostics) = Lex("\"oops\nsealed");
        Assert.Single(diagnostics);
        Assert.Contains("unterminated string", diagnostics[0].Message);
        Assert.Equal(TokenKind.StringLiteral, tokens[0].Kind);
        Assert.Equal(TokenKind.Sealed, tokens[1].Kind);
    }

    [Fact]
    public void Unexpected_characters_become_diagnostics_not_exceptions()
    {
        (Token[] tokens, List<TangentDiagnostic> diagnostics) = Lex("sealed # sealed");
        Assert.Single(diagnostics);
        Assert.Equal(TokenKind.Unknown, tokens[1].Kind);
        Assert.Equal(TokenKind.Sealed, tokens[2].Kind);
    }

    [Fact]
    public void Always_ends_with_end_of_file()
    {
        (Token[] tokens, _) = Lex(string.Empty);
        Assert.Single(tokens);
        Assert.Equal(TokenKind.EndOfFile, tokens[0].Kind);
    }
}

/// <summary>Focused tests for the parser and its error recovery.</summary>
public sealed class ParserTests
{
    private static SyntaxTree Parse(string text) =>
        Parser.Parse(new SourceFile("t.tan", text.Replace("\r\n", "\n")), 0, new StringTable());

    private static int CountNodes(SyntaxTree tree, NodeKind kind)
    {
        int count = 0;
        for (int i = 0; i < tree.Nodes.Count; i++)
        {
            if (tree.Nodes[i].Kind == kind) count++;
        }

        return count;
    }

    private static int FindNode(SyntaxTree tree, NodeKind kind)
    {
        for (int i = 0; i < tree.Nodes.Count; i++)
        {
            if (tree.Nodes[i].Kind == kind) return i;
        }

        throw new InvalidOperationException("no " + kind + " node");
    }

    [Fact]
    public void Project_block()
    {
        SyntaxTree tree = Parse("""
            project "Orders" {
                root: "src/"
                layer Api = Orders.Api.**
                allow Microsoft.Extensions.**
                Api -> nothing
            }
            """);

        Assert.Empty(tree.Diagnostics);
        Assert.Equal(1, CountNodes(tree, NodeKind.Project));
        Assert.Equal(1, CountNodes(tree, NodeKind.ProjectRoot));
        Assert.Equal(1, CountNodes(tree, NodeKind.LayerDecl));
        Assert.Equal(1, CountNodes(tree, NodeKind.Allow));
        Assert.Equal(1, CountNodes(tree, NodeKind.Dependency));
        Assert.Equal(1, CountNodes(tree, NodeKind.Nothing));
    }

    [Theory]
    [InlineData("contract C { forbid public fields }", NodeKind.PredMemberKind)]
    [InlineData("contract C { forbid static methods }", NodeKind.PredMemberKind)]
    [InlineData("contract C { properties { forbid public set } }", NodeKind.PredAccessor)]
    [InlineData("contract C { properties { require get } }", NodeKind.PredAccessor)]
    [InlineData("contract C { methods { returns void } }", NodeKind.PredReturns)]
    [InlineData("contract C { stateless }", NodeKind.PredStateless)]
    [InlineData("contract C { ctor { require each parameter null-checked } }", NodeKind.PredEachParamNullChecked)]
    [InlineData("contract C { ctor { require each parameter not stored } }", NodeKind.PredEachParamNotStored)]
    [InlineData("contract C { ctor { require each parameter assigned to private readonly field } }", NodeKind.PredEachParamAssignedToField)]
    [InlineData("contract C { interface I{Name} lives in Domain }", NodeKind.PredInterfaceLivesIn)]
    [InlineData("contract C { injects only A, B }", NodeKind.PredInjectsOnly)]
    [InlineData("contract C { injects IClock }", NodeKind.PredInjects)]
    [InlineData("contract C { methods where public { forbid returns void } }", NodeKind.PredReturns)]
    [InlineData("contract C { where public { forbid new DbContext } }", NodeKind.PredNew)]
    public void Predicate_forms(string source, NodeKind expected)
    {
        SyntaxTree tree = Parse(source);
        Assert.Empty(tree.Diagnostics);
        Assert.True(CountNodes(tree, expected) > 0, "expected a " + expected + " node");
    }

    [Fact]
    public void Top_level_refinement_after_a_match_clause()
    {
        SyntaxTree tree = Parse("""
            contract Entity {
                match: class in N.**
                where name ends with "Aggregate" { sealed }
            }
            """);

        Assert.Empty(tree.Diagnostics);
        Assert.Equal(1, CountNodes(tree, NodeKind.Match));
        Assert.Equal(1, CountNodes(tree, NodeKind.Refinement));
        Assert.Equal(1, CountNodes(tree, NodeKind.CondNameEndsWith));
        Assert.Equal(1, CountNodes(tree, NodeKind.Rule));

        // The condition belongs to the refinement, not to the match clause.
        int match = FindNode(tree, NodeKind.Match);
        Assert.Equal(1, tree.Nodes.ChildCount(match));
        Assert.Equal(NodeKind.GlobRef, tree.Nodes[tree.Nodes.FirstChild(match)].Kind);
    }

    [Fact]
    public void Match_keeps_its_own_where_when_a_refinement_follows()
    {
        SyntaxTree tree = Parse("""
            contract Entity {
                match: class in N.** where inherits AggregateRoot<*>
                where name ends with "Aggregate" { sealed }
            }
            """);

        Assert.Empty(tree.Diagnostics);
        int match = FindNode(tree, NodeKind.Match);
        Assert.Equal(2, tree.Nodes.ChildCount(match));
        Assert.Equal(NodeKind.CondInherits, tree.Nodes[tree.Nodes.ChildAt(match, 1)].Kind);
        Assert.Equal(1, CountNodes(tree, NodeKind.Refinement));
        Assert.Equal(1, CountNodes(tree, NodeKind.CondNameEndsWith));
    }

    [Fact]
    public void Refinements_nest_inside_member_blocks()
    {
        SyntaxTree tree = Parse("""
            contract C {
                methods { where public { forbid returns void } }
                properties where public { where readonly { forbid set } }
            }
            """);

        Assert.Empty(tree.Diagnostics);
        Assert.Equal(2, CountNodes(tree, NodeKind.MemberBlock));
        Assert.Equal(2, CountNodes(tree, NodeKind.Refinement));
    }

    [Fact]
    public void Semicolons_are_optional_separators()
    {
        SyntaxTree tree = Parse("contract C { fields { require private; require readonly; forbid static } }");
        Assert.Empty(tree.Diagnostics);
        Assert.Equal(3, CountNodes(tree, NodeKind.Rule));
    }

    [Fact]
    public void Conditions_chain_with_and_or()
    {
        SyntaxTree tree = Parse("""
            contract C {
                match: class in N.** where name starts with "A" and name ends with "B" or implements IThing
            }
            """);

        Assert.Empty(tree.Diagnostics);
        Assert.Equal(1, CountNodes(tree, NodeKind.CondOr));
        Assert.Equal(1, CountNodes(tree, NodeKind.CondAnd));
    }

    [Fact]
    public void Bind_and_ignore()
    {
        SyntaxTree tree = Parse("""
            bind N.T to A, B except [Attr], "A/type/modifier#0"
            ignore N.U from A reason "because"
            ignore N.V reason "all of it"
            """);

        Assert.Empty(tree.Diagnostics);
        Assert.Equal(1, CountNodes(tree, NodeKind.Bind));
        Assert.Equal(2, CountNodes(tree, NodeKind.Ignore));
        Assert.Equal(1, CountNodes(tree, NodeKind.ExceptAttribute));
        Assert.Equal(1, CountNodes(tree, NodeKind.ExceptRule));
    }

    [Fact]
    public void Recovery_keeps_the_rest_of_the_contract()
    {
        SyntaxTree tree = Parse("""
            contract C {
                match: class in N.**
                frobnicate
                sealed
                forbid public fields
            }
            """);

        Assert.Single(tree.Diagnostics);
        Assert.Equal(2, CountNodes(tree, NodeKind.Rule));
    }

    [Fact]
    public void Recovery_keeps_the_next_declaration()
    {
        SyntaxTree tree = Parse("""
            contract A { match: class in N.** ??? }
            contract B { sealed }
            """);

        Assert.NotEmpty(tree.Diagnostics);
        Assert.Equal(2, CountNodes(tree, NodeKind.Contract));
    }

    [Fact]
    public void Missing_block_brace_names_the_block()
    {
        SyntaxTree tree = Parse("contract C {\n properties where public\n forbid set\n}");
        Assert.Single(tree.Diagnostics);
        Assert.Contains("expected '{' after 'properties'", tree.Diagnostics[0].Message);
    }

    [Fact]
    public void Parser_never_throws_on_random_punctuation()
    {
        foreach (string source in new[] { "{", "}", "<<<", "contract", "contract {", "project", "bind", "ignore", "]]", "|" })
        {
            SyntaxTree tree = Parse(source);
            Assert.NotNull(tree);
            Assert.True(tree.Nodes.Count >= 1);
        }
    }

    [Fact]
    public void Printer_output_is_stable()
    {
        SyntaxTree tree = Parse("contract C { match: class in N.** \n sealed }");
        string first = SyntaxTreePrinter.Print(tree);
        string second = SyntaxTreePrinter.Print(tree);
        Assert.Equal(first, second);
        Assert.Contains("== tokens ==", first);
        Assert.Contains("== ast ==", first);
    }
}

/// <summary>The diagnostic catalogue is the single source of truth shared by every component.</summary>
public sealed class DiagnosticCatalogTests
{
    [Fact]
    public void Contains_every_documented_code()
    {
        string[] expected =
        {
            "TNG0001", "TNG0002",
            "TNG0101", "TNG0102", "TNG0103", "TNG0104", "TNG0105", "TNG0106", "TNG0107", "TNG0108", "TNG0109",
            "TNG0110", "TNG0111", "TNG0112", "TNG0113",
            "TNG0201", "TNG0202", "TNG0203", "TNG0204", "TNG0205",
            "TNG0301", "TNG0302", "TNG0303",
            "TNG1000", "TNG1001", "TNG1002", "TNG1003", "TNG1004", "TNG1005", "TNG1006",
            "TNG1010", "TNG1011", "TNG1020", "TNG1030",
            "TNG9000",
        };

        foreach (string code in expected)
        {
            Assert.True(DiagnosticCatalog.TryGet(code, out DiagnosticDescriptor descriptor), code + " is missing");
            Assert.Equal(code, descriptor.Id);
            Assert.False(string.IsNullOrWhiteSpace(descriptor.MessageTemplate));
        }

        Assert.Equal(expected.Length, DiagnosticCatalog.All.Length);
    }

    [Fact]
    public void Severities_follow_the_documented_ranges()
    {
        Assert.Equal(DiagnosticSeverity.Error, DiagnosticCatalog.UnknownContractAttribute.DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Warning, DiagnosticCatalog.ModifierViolation.DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Warning, DiagnosticCatalog.LayerCycle.DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Error, DiagnosticCatalog.ConflictingRules.DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Hidden, DiagnosticCatalog.ViolationIgnored.DefaultSeverity);
        Assert.Equal(RuleTier.Flow, DiagnosticCatalog.NotStateless.Tier);
    }

    [Fact]
    public void Formatting_never_throws_on_bad_arguments()
    {
        Assert.Equal(
            DiagnosticCatalog.ConflictingRules.MessageTemplate,
            DiagnosticCatalog.ConflictingRules.Format());
        Assert.Equal("Duplicate declaration 'X'", DiagnosticCatalog.DuplicateDeclaration.Format("X"));
    }
}
