using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Tangent.Analyzers.Internal;

namespace Tangent.Analyzers;

/// <summary>
/// The mechanical subset of Tangent's fixes: add a missing attribute, base type or interface, add or
/// remove a modifier, and turn a public setter into <c>init</c>. Which fix applies is read from the
/// diagnostic's <c>"fix"</c> property, never from its message. Reference (Tier 1) and flow (Tier 2)
/// violations have no mechanical answer and carry no fix.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(TangentCodeFixProvider))]
[Shared]
public sealed class TangentCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(
        "TNG0101", "TNG0102", "TNG0103", "TNG0104", "TNG0107", "TNG0108", "TNG0201", "TNG0303");

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        foreach (Diagnostic diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue(MessageFactory.FixProperty, out string? fix) || fix is null) continue;
            diagnostic.Properties.TryGetValue(MessageFactory.FixArgProperty, out string? argument);

            SyntaxNode node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            CodeAction? action = CreateAction(context.Document, node, fix, argument);
            if (action is not null) context.RegisterCodeFix(action, diagnostic);
        }
    }

    private static CodeAction? CreateAction(Document document, SyntaxNode node, string fix, string? argument)
    {
        switch (fix)
        {
            case FixKinds.AddModifier when argument is not null:
                return CodeAction.Create(
                    "Add '" + argument + "'",
                    ct => AddModifierAsync(document, node, argument, ct),
                    FixKinds.AddModifier + ":" + argument);

            case FixKinds.RemoveModifier when argument is not null:
                return CodeAction.Create(
                    "Remove '" + argument + "'",
                    ct => RemoveModifierAsync(document, node, argument, ct),
                    FixKinds.RemoveModifier + ":" + argument);

            case FixKinds.AddAttribute when argument is not null:
                return CodeAction.Create(
                    "Add attribute '" + StripArguments(argument) + "'",
                    ct => AddAttributeAsync(document, node, argument, ct),
                    FixKinds.AddAttribute + ":" + argument);

            case FixKinds.AddBaseType when argument is not null:
                return CodeAction.Create(
                    "Inherit '" + argument + "'",
                    ct => AddBaseTypeAsync(document, node, argument, true, ct),
                    FixKinds.AddBaseType + ":" + argument);

            case FixKinds.AddInterface when argument is not null:
                return CodeAction.Create(
                    "Implement '" + argument + "'",
                    ct => AddBaseTypeAsync(document, node, argument, false, ct),
                    FixKinds.AddInterface + ":" + argument);

            case FixKinds.SetterToInit:
                return CodeAction.Create(
                    "Make the setter init-only",
                    ct => FixSetterAsync(document, node, ct),
                    FixKinds.SetterToInit);

            case FixKinds.RemoveUsing:
                return CodeAction.Create(
                    "Remove the forbidden using directive",
                    ct => RemoveUsingAsync(document, node, ct),
                    FixKinds.RemoveUsing);

            default:
                return null;
        }
    }

    // ------------------------------------------------------------------
    // modifiers
    // ------------------------------------------------------------------

    private static async Task<Document> AddModifierAsync(
        Document document,
        SyntaxNode node,
        string modifiers,
        CancellationToken cancellationToken)
    {
        MemberDeclarationSyntax? declaration = node.FirstAncestorOrSelf<MemberDeclarationSyntax>();
        if (declaration is null) return document;

        SyntaxTokenList list = declaration.Modifiers;
        SyntaxTriviaList leading = declaration.GetLeadingTrivia();
        bool hadModifiers = list.Count > 0;

        foreach (string part in modifiers.Split(' '))
        {
            SyntaxKind kind = KeywordKind(part);
            if (kind == SyntaxKind.None || list.Any(kind)) continue;
            if (IsAccessibility(kind)) list = RemoveAccessibility(list);
            list = Insert(list, SyntaxFactory.Token(kind).WithTrailingTrivia(SyntaxFactory.Space), kind);
        }

        MemberDeclarationSyntax replacement = declaration.WithModifiers(list);
        if (!hadModifiers) replacement = replacement.WithLeadingTrivia(leading);

        return await ReplaceAsync(document, declaration, replacement, cancellationToken).ConfigureAwait(false);
    }

    private static SyntaxTokenList Insert(SyntaxTokenList list, SyntaxToken token, SyntaxKind kind)
    {
        if (list.Count == 0) return SyntaxFactory.TokenList(token);
        if (IsAccessibility(kind)) return list.Insert(0, token.WithLeadingTrivia(list[0].LeadingTrivia));

        int index = 0;
        while (index < list.Count && IsAccessibility(list[index].Kind())) index++;
        return index == 0
            ? list.Insert(0, token.WithLeadingTrivia(list[0].LeadingTrivia))
            : list.Insert(index, token);
    }

    private static async Task<Document> RemoveModifierAsync(
        Document document,
        SyntaxNode node,
        string modifiers,
        CancellationToken cancellationToken)
    {
        MemberDeclarationSyntax? declaration = node.FirstAncestorOrSelf<MemberDeclarationSyntax>();
        if (declaration is null) return document;

        SyntaxTokenList list = declaration.Modifiers;
        SyntaxTriviaList leading = list.Count > 0 ? list[0].LeadingTrivia : declaration.GetLeadingTrivia();

        foreach (string part in modifiers.Split(' '))
        {
            SyntaxKind kind = KeywordKind(part);
            if (kind == SyntaxKind.None) continue;
            int index = IndexOf(list, kind);
            if (index < 0) continue;

            SyntaxToken token = list[index];
            if (IsAccessibility(kind))
            {
                list = list.Replace(token, SyntaxFactory.Token(SyntaxKind.PrivateKeyword).WithTriviaFrom(token));
            }
            else
            {
                list = list.RemoveAt(index);
            }
        }

        MemberDeclarationSyntax replacement = list.Count == 0
            ? declaration.WithModifiers(list).WithLeadingTrivia(leading)
            : declaration.WithModifiers(list.Replace(list[0], list[0].WithLeadingTrivia(leading)));

        return await ReplaceAsync(document, declaration, replacement, cancellationToken).ConfigureAwait(false);
    }

    private static int IndexOf(SyntaxTokenList list, SyntaxKind kind)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].IsKind(kind)) return i;
        }

        return -1;
    }

    private static SyntaxTokenList RemoveAccessibility(SyntaxTokenList list)
    {
        SyntaxTokenList result = list;
        for (int i = result.Count - 1; i >= 0; i--)
        {
            if (IsAccessibility(result[i].Kind())) result = result.RemoveAt(i);
        }

        return result;
    }

    private static bool IsAccessibility(SyntaxKind kind) =>
        kind == SyntaxKind.PublicKeyword ||
        kind == SyntaxKind.PrivateKeyword ||
        kind == SyntaxKind.ProtectedKeyword ||
        kind == SyntaxKind.InternalKeyword;

    private static SyntaxKind KeywordKind(string modifier)
    {
        switch (modifier)
        {
            case "public": return SyntaxKind.PublicKeyword;
            case "private": return SyntaxKind.PrivateKeyword;
            case "protected": return SyntaxKind.ProtectedKeyword;
            case "internal": return SyntaxKind.InternalKeyword;
            case "static": return SyntaxKind.StaticKeyword;
            case "sealed": return SyntaxKind.SealedKeyword;
            case "abstract": return SyntaxKind.AbstractKeyword;
            case "readonly": return SyntaxKind.ReadOnlyKeyword;
            case "partial": return SyntaxKind.PartialKeyword;
            default: return SyntaxKind.None;
        }
    }

    // ------------------------------------------------------------------
    // attributes
    // ------------------------------------------------------------------

    private static async Task<Document> AddAttributeAsync(
        Document document,
        SyntaxNode node,
        string specification,
        CancellationToken cancellationToken)
    {
        MemberDeclarationSyntax? declaration = node.FirstAncestorOrSelf<MemberDeclarationSyntax>();
        if (declaration is null) return document;

        SemanticModel? model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        string name = StripArguments(specification);
        bool needsArguments = specification.EndsWith("(*)", StringComparison.Ordinal);

        AttributeSyntax attribute = SyntaxFactory.Attribute(SyntaxFactory.ParseName(name));
        if (needsArguments)
        {
            attribute = attribute.WithArgumentList(
                SyntaxFactory.ParseAttributeArgumentList("(" + DefaultArgument(name, model?.Compilation) + ")"));
        }

        SyntaxTriviaList leading = declaration.GetLeadingTrivia();
        AttributeListSyntax list = SyntaxFactory
            .AttributeList(SyntaxFactory.SingletonSeparatedList(attribute))
            .WithLeadingTrivia(leading)
            .WithTrailingTrivia(EndOfLineThen(node, Indentation(leading)));

        MemberDeclarationSyntax stripped = declaration.WithLeadingTrivia(SyntaxFactory.TriviaList());
        MemberDeclarationSyntax replacement = stripped.WithAttributeLists(stripped.AttributeLists.Insert(0, list));

        Document updated = await ReplaceAsync(document, declaration, replacement, cancellationToken).ConfigureAwait(false);
        return await AddUsingIfNeededAsync(updated, name, model, cancellationToken).ConfigureAwait(false);
    }

    private static string DefaultArgument(string name, Compilation? compilation)
    {
        if (!string.Equals(name, "ProducesResponseType", StringComparison.Ordinal)) return "\"\"";
        return compilation?.GetTypeByMetadataName("Microsoft.AspNetCore.Http.StatusCodes") is not null
            ? "StatusCodes.Status200OK"
            : "200";
    }

    private static string StripArguments(string specification)
    {
        int parenthesis = specification.IndexOf('(');
        return parenthesis < 0 ? specification : specification.Substring(0, parenthesis);
    }

    // ------------------------------------------------------------------
    // base list
    // ------------------------------------------------------------------

    private static async Task<Document> AddBaseTypeAsync(
        Document document,
        SyntaxNode node,
        string typeName,
        bool isBaseClass,
        CancellationToken cancellationToken)
    {
        TypeDeclarationSyntax? declaration = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (declaration is null) return document;

        SemanticModel? model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        SimpleBaseTypeSyntax entry = SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName(typeName));

        TypeDeclarationSyntax replacement;
        if (declaration.BaseList is null)
        {
            replacement = AttachBaseList(
                declaration,
                SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(entry)));
        }
        else if (isBaseClass && HasExplicitBaseClass(declaration, model))
        {
            SeparatedSyntaxList<BaseTypeSyntax> types = declaration.BaseList.Types;
            replacement = declaration.WithBaseList(
                declaration.BaseList.WithTypes(types.Replace(types[0], entry.WithTriviaFrom(types[0]))));
        }
        else
        {
            SeparatedSyntaxList<BaseTypeSyntax> types = declaration.BaseList.Types;
            SeparatedSyntaxList<BaseTypeSyntax> updated = isBaseClass
                ? types.Insert(0, entry)
                : types.Add(entry.WithTriviaFrom(types[types.Count - 1]));
            if (!isBaseClass)
            {
                updated = updated.Replace(
                    updated[types.Count - 1],
                    updated[types.Count - 1].WithTrailingTrivia(SyntaxFactory.TriviaList()));
            }

            replacement = declaration.WithBaseList(declaration.BaseList.WithTypes(updated));
        }

        Document updatedDocument = await ReplaceAsync(document, declaration, replacement, cancellationToken).ConfigureAwait(false);
        return await AddUsingIfNeededAsync(updatedDocument, typeName, model, cancellationToken).ConfigureAwait(false);
    }

    private static TypeDeclarationSyntax AttachBaseList(TypeDeclarationSyntax declaration, BaseListSyntax baseList)
    {
        if (declaration.ParameterList is { } parameters)
        {
            SyntaxToken close = parameters.CloseParenToken;
            return declaration
                .WithParameterList(parameters.WithCloseParenToken(close.WithTrailingTrivia(SyntaxFactory.Space)))
                .WithBaseList(baseList.WithTrailingTrivia(close.TrailingTrivia));
        }

        if (declaration.TypeParameterList is { } typeParameters)
        {
            SyntaxToken greaterThan = typeParameters.GreaterThanToken;
            return declaration
                .WithTypeParameterList(typeParameters.WithGreaterThanToken(greaterThan.WithTrailingTrivia(SyntaxFactory.Space)))
                .WithBaseList(baseList.WithTrailingTrivia(greaterThan.TrailingTrivia));
        }

        SyntaxToken identifier = declaration.Identifier;
        return declaration
            .WithIdentifier(identifier.WithTrailingTrivia(SyntaxFactory.Space))
            .WithBaseList(baseList.WithTrailingTrivia(identifier.TrailingTrivia));
    }

    private static bool HasExplicitBaseClass(TypeDeclarationSyntax declaration, SemanticModel? model)
    {
        if (model is null || declaration.BaseList is null || declaration.BaseList.Types.Count == 0) return false;
        ISymbol? symbol = model.GetSymbolInfo(declaration.BaseList.Types[0].Type).Symbol;
        return symbol is INamedTypeSymbol { TypeKind: Microsoft.CodeAnalysis.TypeKind.Class };
    }

    private static async Task<Document> AddUsingIfNeededAsync(
        Document document,
        string typeName,
        SemanticModel? model,
        CancellationToken cancellationToken)
    {
        if (model is null) return document;

        INamedTypeSymbol? target = FindType(model, typeName, cancellationToken)
            ?? FindType(model, typeName + "Attribute", cancellationToken);

        if (target?.ContainingNamespace is null || target.ContainingNamespace.IsGlobalNamespace) return document;

        string namespaceName = target.ContainingNamespace.ToDisplayString();
        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is not CompilationUnitSyntax unit) return document;

        foreach (UsingDirectiveSyntax existing in unit.Usings)
        {
            if (string.Equals(existing.Name?.ToString(), namespaceName, StringComparison.Ordinal)) return document;
        }

        BaseNamespaceDeclarationSyntax? enclosing = unit.Members.OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
        if (enclosing is not null && string.Equals(enclosing.Name.ToString(), namespaceName, StringComparison.Ordinal)) return document;

        SyntaxTriviaList trailing = SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine(NewLine(root)));

        // When the file had no usings at all, separate the new block from what follows.
        if (unit.Usings.Count == 0) trailing = trailing.Add(SyntaxFactory.EndOfLine(NewLine(root)));

        UsingDirectiveSyntax directive = SyntaxFactory
            .UsingDirective(SyntaxFactory.ParseName(namespaceName))
            .WithTrailingTrivia(trailing);

        return document.WithSyntaxRoot(unit.WithUsings(unit.Usings.Add(directive)));
    }

    private static INamedTypeSymbol? FindType(SemanticModel model, string name, CancellationToken cancellationToken)
    {
        foreach (ISymbol symbol in model.Compilation.GetSymbolsWithName(name, SymbolFilter.Type, cancellationToken))
        {
            if (symbol is INamedTypeSymbol named) return named;
        }

        return null;
    }

    // ------------------------------------------------------------------
    // accessors and usings
    // ------------------------------------------------------------------

    private static async Task<Document> FixSetterAsync(Document document, SyntaxNode node, CancellationToken cancellationToken)
    {
        AccessorDeclarationSyntax? accessor = node.FirstAncestorOrSelf<AccessorDeclarationSyntax>();
        if (accessor is null)
        {
            PropertyDeclarationSyntax? property = node.FirstAncestorOrSelf<PropertyDeclarationSyntax>();
            accessor = property?.AccessorList?.Accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.SetAccessorDeclaration));
        }

        if (accessor is null || !accessor.IsKind(SyntaxKind.SetAccessorDeclaration)) return document;

        AccessorDeclarationSyntax replacement;
        if (SupportsInit(accessor))
        {
            replacement = SyntaxFactory.AccessorDeclaration(SyntaxKind.InitAccessorDeclaration)
                .WithAttributeLists(accessor.AttributeLists)
                .WithModifiers(accessor.Modifiers)
                .WithKeyword(SyntaxFactory.Token(SyntaxKind.InitKeyword).WithTriviaFrom(accessor.Keyword))
                .WithBody(accessor.Body)
                .WithExpressionBody(accessor.ExpressionBody)
                .WithSemicolonToken(accessor.SemicolonToken);
        }
        else
        {
            replacement = accessor.WithModifiers(
                accessor.Modifiers.Insert(
                    0,
                    SyntaxFactory.Token(SyntaxKind.PrivateKeyword)
                        .WithLeadingTrivia(accessor.Keyword.LeadingTrivia)
                        .WithTrailingTrivia(SyntaxFactory.Space)))
                .WithKeyword(accessor.Keyword.WithLeadingTrivia(SyntaxFactory.TriviaList()));
        }

        return await ReplaceAsync(document, accessor, replacement, cancellationToken).ConfigureAwait(false);
    }

    private static bool SupportsInit(SyntaxNode node) =>
        node.SyntaxTree.Options is CSharpParseOptions options && options.LanguageVersion >= LanguageVersion.CSharp9;

    private static async Task<Document> RemoveUsingAsync(Document document, SyntaxNode node, CancellationToken cancellationToken)
    {
        UsingDirectiveSyntax? directive = node.FirstAncestorOrSelf<UsingDirectiveSyntax>();
        if (directive is null) return document;

        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return document;

        SyntaxNode? updated = root.RemoveNode(directive, SyntaxRemoveOptions.KeepNoTrivia);
        return updated is null ? document : document.WithSyntaxRoot(updated);
    }

    private static async Task<Document> ReplaceAsync(
        Document document,
        SyntaxNode original,
        SyntaxNode replacement,
        CancellationToken cancellationToken)
    {
        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return document;
        return document.WithSyntaxRoot(root.ReplaceNode(original, replacement));
    }

    // ------------------------------------------------------------------
    // trivia
    // ------------------------------------------------------------------

    private static SyntaxTriviaList EndOfLineThen(SyntaxNode node, SyntaxTriviaList indentation)
    {
        SyntaxTriviaList result = SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine(NewLine(node)));
        foreach (SyntaxTrivia trivia in indentation) result = result.Add(trivia);
        return result;
    }

    private static string NewLine(SyntaxNode node)
    {
        string text = node.SyntaxTree.GetText().ToString();
        return text.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
    }

    private static SyntaxTriviaList Indentation(SyntaxTriviaList leading)
    {
        SyntaxTriviaList result = SyntaxFactory.TriviaList();
        for (int i = leading.Count - 1; i >= 0; i--)
        {
            if (!leading[i].IsKind(SyntaxKind.WhitespaceTrivia)) break;
            result = result.Insert(0, leading[i]);
        }

        return result;
    }
}
