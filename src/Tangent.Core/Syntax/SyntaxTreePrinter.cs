using System;
using System.Globalization;
using System.Text;
using Tangent.Core.Ir;
using Tangent.Core.Text;

namespace Tangent.Core.Syntax;

/// <summary>Dumps tokens and the AST as deterministic text, for golden tests and <c>tangent lint --dump-ast</c>.</summary>
public static class SyntaxTreePrinter
{
    /// <summary>Prints the token stream, one token per line.</summary>
    public static string PrintTokens(SyntaxTree tree)
    {
        if (tree is null) throw new ArgumentNullException(nameof(tree));
        var sb = new StringBuilder();
        Token[] tokens = tree.Tokens;
        for (int i = 0; i < tokens.Length; i++)
        {
            Token token = tokens[i];
            sb.Append(token.Line.ToString(CultureInfo.InvariantCulture).PadLeft(5));
            sb.Append(':');
            sb.Append(token.Column.ToString(CultureInfo.InvariantCulture).PadRight(4));
            sb.Append(' ');
            sb.Append(token.Kind.ToString().PadRight(16));
            if (token.Kind != TokenKind.EndOfFile)
            {
                sb.Append(' ');
                AppendEscaped(sb, tree.Source.Text, token.Start, token.Length);
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>Prints the AST as an indented tree.</summary>
    public static string PrintAst(SyntaxTree tree)
    {
        if (tree is null) throw new ArgumentNullException(nameof(tree));
        var sb = new StringBuilder();
        PrintNode(tree, tree.Root, 0, sb);
        return sb.ToString();
    }

    /// <summary>Prints tokens and the AST, separated by headers.</summary>
    public static string Print(SyntaxTree tree)
    {
        var sb = new StringBuilder();
        sb.Append("== tokens ==\n");
        sb.Append(PrintTokens(tree));
        sb.Append("== ast ==\n");
        sb.Append(PrintAst(tree));
        return sb.ToString();
    }

    private static void PrintNode(SyntaxTree tree, int index, int depth, StringBuilder sb)
    {
        if (index < 0) return;
        ref readonly Node node = ref tree.Nodes[index];
        for (int i = 0; i < depth; i++) sb.Append("  ");
        sb.Append(node.Kind.ToString());
        AppendPayload(tree, index, sb);
        sb.Append(" @").Append(node.Span.Line.ToString(CultureInfo.InvariantCulture));
        sb.Append(':').Append(node.Span.Column.ToString(CultureInfo.InvariantCulture));
        sb.Append('\n');

        foreach (int child in tree.Nodes.Children(index))
        {
            PrintNode(tree, child, depth + 1, sb);
        }
    }

    private static void AppendPayload(SyntaxTree tree, int index, StringBuilder sb)
    {
        ref readonly Node node = ref tree.Nodes[index];
        StringTable strings = tree.Strings;
        switch (node.Kind)
        {
            case NodeKind.Project:
            case NodeKind.ProjectRoot:
            case NodeKind.LayerRef:
            case NodeKind.GlobRef:
            case NodeKind.ContractRef:
            case NodeKind.ExceptAttribute:
            case NodeKind.ExceptRule:
            case NodeKind.TypePattern:
            case NodeKind.TypeTemplate:
            case NodeKind.CondNameStartsWith:
            case NodeKind.CondNameEndsWith:
            case NodeKind.CondNameContains:
            case NodeKind.CondNameMatches:
                AppendName(sb, strings, node.Payload);
                return;

            case NodeKind.LayerDecl:
            case NodeKind.Bind:
            case NodeKind.Ignore:
                AppendName(sb, strings, node.Payload);
                AppendName(sb, strings, node.Payload2);
                return;

            case NodeKind.Contract:
                AppendName(sb, strings, node.Payload);
                if (node.Payload2 >= 0)
                {
                    sb.Append(" extends ");
                    sb.Append(strings.GetString(new StringId(node.Payload2)));
                }

                return;

            case NodeKind.Dependency:
                AppendName(sb, strings, node.Payload);
                return;

            case NodeKind.AttrRef:
                AppendName(sb, strings, node.Payload);
                if (node.Payload2 != 0) sb.Append(" (*)");
                return;

            case NodeKind.Match:
                sb.Append(' ').Append(IrText.TypeKindName((TypeKind)node.Payload));
                return;

            case NodeKind.MemberBlock:
                sb.Append(' ').Append(IrText.MemberKindSlug((MemberKind)node.Payload));
                return;

            case NodeKind.Rule:
                sb.Append(' ').Append(IrText.PolarityName((Polarity)node.Payload));
                return;

            case NodeKind.PredModifier:
            case NodeKind.CondModifier:
            case NodeKind.PredEachParamAssignedToField:
            {
                string text = IrText.ModifierText((Modifiers)node.Payload);
                if (text.Length > 0) sb.Append(' ').Append(text);
                return;
            }

            case NodeKind.PredAccessor:
            {
                sb.Append(' ').Append(IrText.AccessorName((AccessorKind)node.Payload));
                string text = IrText.ModifierText((Modifiers)node.Payload2);
                if (text.Length > 0) sb.Append(" [").Append(text).Append(']');
                return;
            }

            case NodeKind.PredMemberKind:
            {
                sb.Append(' ').Append(IrText.MemberKindSlug((MemberKind)node.Payload));
                string text = IrText.ModifierText((Modifiers)node.Payload2);
                if (text.Length > 0) sb.Append(" [").Append(text).Append(']');
                return;
            }

            case NodeKind.PredInterfaceLivesIn:
                AppendName(sb, strings, node.Payload);
                sb.Append(" lives in");
                AppendName(sb, strings, node.Payload2);
                return;

            default:
                return;
        }
    }

    private static void AppendName(StringBuilder sb, StringTable strings, int payload)
    {
        if (payload < 0) return;
        sb.Append(" '").Append(strings.GetString(new StringId(payload))).Append('\'');
    }

    private static void AppendEscaped(StringBuilder sb, string text, int start, int length)
    {
        int end = start + length;
        if (end > text.Length) end = text.Length;
        for (int i = start; i < end; i++)
        {
            char c = text[i];
            switch (c)
            {
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
    }
}
