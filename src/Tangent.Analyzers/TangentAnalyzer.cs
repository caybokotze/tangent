using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Tangent.Analyzers.Internal;
using Tangent.Core.Binding;
using Tangent.Core.Diagnostics;
using Tangent.Core.Ir;
using Tangent.Core.Matching;
using CoreDiagnostic = Tangent.Core.Diagnostics.TangentDiagnostic;
using RoslynDescriptor = Microsoft.CodeAnalysis.DiagnosticDescriptor;

namespace Tangent.Analyzers;

/// <summary>
/// Enforces the Tangent architectural contracts found in the compilation's <c>.tan</c> additional files.
/// The model is parsed once per unique contract content and shared across compilations; a compilation with
/// no contract files registers nothing at all.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TangentAnalyzer : DiagnosticAnalyzer
{
    private readonly IReferenceSink? _sink;
    private readonly bool _includeUnbound;

    /// <summary>Creates the analyzer as the compiler and the IDE use it.</summary>
    public TangentAnalyzer()
        : this(null, false)
    {
    }

    /// <summary>
    /// Creates the analyzer with a reference sink, as <c>tangent graph</c> uses it. Every reference the
    /// Tier 1 walk observes is handed to <paramref name="referenceSink"/>.
    /// </summary>
    /// <param name="referenceSink">The sink, or null for the normal path.</param>
    /// <param name="includeUnbound">When true, types bound to no contract are reported to the sink too.</param>
    public TangentAnalyzer(IReferenceSink? referenceSink, bool includeUnbound)
    {
        _sink = referenceSink;
        _includeUnbound = includeUnbound;
    }

    /// <inheritdoc/>
    public override ImmutableArray<RoslynDescriptor> SupportedDiagnostics => TangentDiagnostics.All;

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        ImmutableArray<AdditionalText> files = CollectContractFiles(context.Options);
        if (files.IsEmpty) return;

        BindResult result = ContractFiles.LoadModel(files);
        var analysis = new CompilationAnalysis(result, files, _sink, _includeUnbound);

        context.RegisterCompilationEndAction(analysis.OnCompilationEnd);

        if (result.Model is null || result.HasErrors) return;

        analysis.Start(context.Compilation);
        context.RegisterSymbolAction(analysis.AnalyzeNamedType, SymbolKind.NamedType);
        if (analysis.State!.NeedsOperationWalk)
        {
            context.RegisterOperationBlockStartAction(analysis.OnOperationBlockStart);
        }
    }

    private static ImmutableArray<AdditionalText> CollectContractFiles(AnalyzerOptions options)
    {
        ImmutableArray<AdditionalText> all = options.AdditionalFiles;
        if (all.IsDefaultOrEmpty) return ImmutableArray<AdditionalText>.Empty;

        ImmutableArray<AdditionalText>.Builder? builder = null;
        for (int i = 0; i < all.Length; i++)
        {
            if (!ContractFiles.IsContractFile(all[i], options.AnalyzerConfigOptionsProvider)) continue;
            builder ??= ImmutableArray.CreateBuilder<AdditionalText>();
            builder.Add(all[i]);
        }

        return builder is null ? ImmutableArray<AdditionalText>.Empty : builder.ToImmutable();
    }

    /// <summary>All per-compilation work, so that the registered callbacks are method groups, not closures.</summary>
    private sealed class CompilationAnalysis
    {
        private readonly BindResult _result;
        private readonly ImmutableArray<AdditionalText> _files;
        private readonly IReferenceSink? _sink;
        private readonly bool _includeUnbound;

        internal CompilationAnalysis(
            BindResult result,
            ImmutableArray<AdditionalText> files,
            IReferenceSink? sink,
            bool includeUnbound)
        {
            _result = result;
            _files = files;
            _sink = sink;
            _includeUnbound = includeUnbound;
        }

        internal CompilationState? State { get; private set; }

        internal void Start(Compilation compilation)
        {
            if (_result.Model is null) return;
            State = new CompilationState(compilation, _result.Model, _sink, _includeUnbound);
        }

        // --------------------------------------------------------------
        // symbols
        // --------------------------------------------------------------

        internal void AnalyzeNamedType(SymbolAnalysisContext context)
        {
            CompilationState? state = State;
            if (state is null) return;
            if (context.Symbol is not INamedTypeSymbol type) return;
            if (type.IsImplicitlyDeclared) return;

            TypeContext? typeContext = state.GetContext(type);
            if (typeContext is null) return;

            ImmutableArray<BindingDiagnostic> bindingDiagnostics = typeContext.BindingDiagnostics;
            for (int i = 0; i < bindingDiagnostics.Length; i++)
            {
                context.ReportDiagnostic(MessageFactory.Create(
                    bindingDiagnostics[i].Code,
                    bindingDiagnostics[i].Location,
                    bindingDiagnostics[i].Message,
                    MessageFactory.Properties(string.Empty, string.Empty, string.Empty, "binding")));
            }

            ImmutableArray<IgnoreNote> ignores = typeContext.Ignores;
            for (int i = 0; i < ignores.Length; i++)
            {
                context.ReportDiagnostic(MessageFactory.Create(
                    DiagnosticCatalog.ViolationIgnored.Id,
                    ignores[i].Location,
                    DiagnosticCatalog.ViolationIgnored.Format(ignores[i].Reason),
                    MessageFactory.Properties(ignores[i].Contract, ignores[i].RuleId, type.Name, "ignore")));
            }

            if (_sink is not null && (typeContext.IsBound || _includeUnbound))
            {
                _sink.OnTypeBound(type, ContractNames(state, typeContext), LayerIndexOf(typeContext), LayerNameOf(state, typeContext));
            }

            if (typeContext.IsBound)
            {
                var evaluator = new RuleEvaluator(state, typeContext, context);
                evaluator.Evaluate();
                AnalyzePrimaryConstructors(state, typeContext, context);
            }

            if (typeContext.LayerIndex >= 0 || !typeContext.TypeUsingRules.IsEmpty || _sink is not null)
            {
                var reporter = new DiagnosticReporter(context);
                ReferenceScanner.ScanSymbol(state, typeContext, typeContext.TypeUsingRules, in reporter);
            }
        }

        private static void AnalyzePrimaryConstructors(CompilationState state, TypeContext typeContext, SymbolAnalysisContext context)
        {
            if (typeContext.FlowRules.IsEmpty) return;

            ImmutableArray<IMethodSymbol> constructors = typeContext.Type.InstanceConstructors;
            for (int i = 0; i < constructors.Length; i++)
            {
                IMethodSymbol constructor = constructors[i];
                if (constructor.IsImplicitlyDeclared) continue;
                if (!SymbolFacts.IsPrimaryConstructor(constructor)) continue;

                ImmutableArray<AppliedRule> rules = FlowRulesFor(state, typeContext, constructor);
                if (rules.IsEmpty) continue;
                var reporter = new DiagnosticReporter(context);
                FlowAnalysis.AnalyzePrimaryConstructor(state, typeContext, constructor, rules, in reporter);
            }
        }

        private static ImmutableArray<AppliedRule> FlowRulesFor(CompilationState state, TypeContext typeContext, IMethodSymbol constructor)
        {
            ImmutableArray<AppliedRule>.Builder? builder = null;
            ImmutableArray<AppliedRule> rules = typeContext.FlowRules;
            for (int i = 0; i < rules.Length; i++)
            {
                Rule rule = state.Model.Contracts[rules[i].ContractIndex].Rules[rules[i].RuleIndex];
                if (rule.Predicate.Kind == PredicateKind.Stateless) continue;
                if (rule.Selector.Kind != MemberKind.Ctor) continue;
                if (!state.EvaluateCondition(rule.Selector.ConditionIndex, constructor, typeContext.Type)) continue;
                builder ??= ImmutableArray.CreateBuilder<AppliedRule>();
                builder.Add(rules[i]);
            }

            return builder is null ? ImmutableArray<AppliedRule>.Empty : builder.ToImmutable();
        }

        private static ImmutableArray<string> ContractNames(CompilationState state, TypeContext typeContext)
        {
            ImmutableArray<int> contracts = typeContext.Contracts;
            if (contracts.IsDefaultOrEmpty) return ImmutableArray<string>.Empty;
            var builder = ImmutableArray.CreateBuilder<string>(contracts.Length);
            for (int i = 0; i < contracts.Length; i++)
            {
                builder.Add(state.Model.Text(state.Model.Contracts[contracts[i]].Name));
            }

            return builder.MoveToImmutable();
        }

        private static int? LayerIndexOf(TypeContext typeContext) =>
            typeContext.LayerIndex >= 0 ? typeContext.LayerIndex : (int?)null;

        private static string? LayerNameOf(CompilationState state, TypeContext typeContext) =>
            typeContext.LayerIndex >= 0 ? state.LayerName(typeContext.LayerIndex) : null;

        // --------------------------------------------------------------
        // operation blocks
        // --------------------------------------------------------------

        internal void OnOperationBlockStart(OperationBlockStartAnalysisContext context)
        {
            CompilationState? state = State;
            if (state is null) return;

            INamedTypeSymbol? containing = context.OwningSymbol.ContainingType;
            if (containing is null) return;

            TypeContext? typeContext = state.GetContext(containing);
            if (typeContext is null) return;

            OperationAnalyzer? analyzer = OperationAnalyzer.Create(state, typeContext, context.OwningSymbol);
            if (analyzer is null) return;

            if (analyzer.NeedsOperations)
            {
                context.RegisterOperationAction(
                    analyzer.AnalyzeOperation,
                    OperationKind.Invocation,
                    OperationKind.ObjectCreation,
                    OperationKind.FieldReference,
                    OperationKind.PropertyReference,
                    OperationKind.EventReference,
                    OperationKind.MethodReference,
                    OperationKind.TypeOf,

                    // "Any symbol reference anywhere in the type": a type named in a body is a reference
                    // whether it is invoked, tested against, cast to, declared or passed as a type argument.
                    OperationKind.IsType,
                    OperationKind.DeclarationPattern,
                    OperationKind.TypePattern,
                    OperationKind.RecursivePattern,
                    OperationKind.Conversion,
                    OperationKind.VariableDeclarator,
                    OperationKind.ArrayCreation,
                    OperationKind.DefaultValue,
                    OperationKind.SizeOf);
            }

            if (analyzer.NeedsBlockEnd)
            {
                context.RegisterOperationBlockEndAction(analyzer.AnalyzeBlockEnd);
            }
        }

        // --------------------------------------------------------------
        // compilation end: model diagnostics
        // --------------------------------------------------------------

        internal void OnCompilationEnd(CompilationAnalysisContext context)
        {
            ReportModelDiagnostics(context);

            ArchitectureModel? model = _result.Model;
            if (model is null) return;

            ReportUnknownBindTargets(context, model);
            ReportUnresolvedTypeNames(context, model);
        }

        private void ReportModelDiagnostics(CompilationAnalysisContext context)
        {
            ImmutableArray<CoreDiagnostic> diagnostics = _result.Diagnostics;
            for (int i = 0; i < diagnostics.Length; i++)
            {
                CoreDiagnostic diagnostic = diagnostics[i];
                RoslynDescriptor? descriptor = TangentDiagnostics.TryGet(diagnostic.Code);
                if (descriptor is null) continue;

                context.ReportDiagnostic(Diagnostic.Create(
                    descriptor,
                    ContractLocation(diagnostic.FilePath, diagnostic.Span.Start, diagnostic.Span.Length),
                    MessageFactory.Properties(string.Empty, string.Empty, string.Empty, "model"),
                    diagnostic.Message));
            }
        }

        private void ReportUnknownBindTargets(CompilationAnalysisContext context, ArchitectureModel model)
        {
            if (State is null) return;

            ImmutableArray<Binding> bindings = model.Bindings;
            for (int i = 0; i < bindings.Length; i++)
            {
                CheckBindTarget(context, model, model.Text(bindings[i].TypeName), bindings[i].Span);
            }

            ImmutableArray<Ignore> ignores = model.Ignores;
            for (int i = 0; i < ignores.Length; i++)
            {
                CheckBindTarget(context, model, model.Text(ignores[i].TypeName), ignores[i].Span);
            }
        }

        private void CheckBindTarget(CompilationAnalysisContext context, ArchitectureModel model, string name, Core.Text.SourceSpan span)
        {
            if (name.Length == 0) return;
            if (context.Compilation.GetTypeByMetadataName(name) is not null) return;

            // One .tangent folder is shared by projects that see different assemblies. Only complain when the
            // namespace the type should live in exists here, which means it really is missing.
            if (State is null || !State.ContainingNamespaceExists(name)) return;

            context.ReportDiagnostic(Diagnostic.Create(
                TangentDiagnostics.Get(DiagnosticCatalog.UnknownType.Id),
                ContractLocation(model.FilePathOf(span), span.Start, span.Length),
                MessageFactory.Properties(string.Empty, string.Empty, string.Empty, "model"),
                DiagnosticCatalog.UnknownType.Format(name)));
        }

        private void ReportUnresolvedTypeNames(CompilationAnalysisContext context, ArchitectureModel model)
        {
            CompilationState? state = State;
            if (state is null) return;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            ImmutableArray<Contract> contracts = model.Contracts;
            for (int c = 0; c < contracts.Length; c++)
            {
                if (!state.IsContractUsed(c)) continue;
                string contractName = model.Text(contracts[c].Name);
                ImmutableArray<Rule> rules = contracts[c].Rules;
                for (int r = 0; r < rules.Length; r++)
                {
                    Rule rule = rules[r];
                    if (rule.OriginContract != c) continue;
                    if (!state.IsContractUsed(c)) continue;
                    if (!UsesTypePatterns(rule.Predicate.Kind)) continue;
                    CheckGroup(context, state, model, rule.Predicate.Payload, contractName, rule.Span, seen);
                }
            }
        }

        private static bool UsesTypePatterns(PredicateKind kind)
        {
            switch (kind)
            {
                case PredicateKind.Inherits:
                case PredicateKind.Implements:
                case PredicateKind.Returns:
                case PredicateKind.Injects:
                case PredicateKind.InjectsOnly:
                case PredicateKind.NewExpression:
                    return true;
                default:
                    return false;
            }
        }

        private void CheckGroup(
            CompilationAnalysisContext context,
            CompilationState state,
            ArchitectureModel model,
            int groupIndex,
            string contractName,
            Core.Text.SourceSpan span,
            HashSet<string> seen)
        {
            if (groupIndex < 0 || groupIndex >= model.TypePatterns.Groups.Length) return;
            ImmutableArray<int> group = model.TypePatterns.Group(groupIndex);
            for (int i = 0; i < group.Length; i++)
            {
                CheckPattern(context, state, model, group[i], contractName, span, seen);
            }
        }

        private void CheckPattern(
            CompilationAnalysisContext context,
            CompilationState state,
            ArchitectureModel model,
            int patternIndex,
            string contractName,
            Core.Text.SourceSpan span,
            HashSet<string> seen)
        {
            TypePattern pattern = model.TypePatterns.Patterns[patternIndex];
            for (int i = 0; i < pattern.Arity; i++)
            {
                CheckPattern(context, state, model, model.TypePatterns.Argument(pattern, i), contractName, span, seen);
            }

            if (pattern.IsWildcard || pattern.TemplateIndex >= 0) return;

            string name = model.Text(pattern.Name);
            if (name.Length == 0 || IsPrimitiveName(name)) return;
            if (!seen.Add(contractName + "/" + name)) return;
            if (state.TypeNameExists(name, pattern.Arity)) return;

            context.ReportDiagnostic(Diagnostic.Create(
                TangentDiagnostics.Get(DiagnosticCatalog.UnresolvedTypeName.Id),
                ContractLocation(model.FilePathOf(span), span.Start, span.Length),
                MessageFactory.Properties(contractName, string.Empty, string.Empty, "model"),
                DiagnosticCatalog.UnresolvedTypeName.Format(name, contractName)));
        }

        private static bool IsPrimitiveName(string name)
        {
            switch (name)
            {
                case "void":
                case "object":
                case "string":
                case "bool":
                case "byte":
                case "sbyte":
                case "char":
                case "short":
                case "ushort":
                case "int":
                case "uint":
                case "long":
                case "ulong":
                case "float":
                case "double":
                case "decimal":
                case "nint":
                case "nuint":
                case "dynamic":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Builds a location inside a <c>.tan</c> additional file so the IDE can navigate to it.</summary>
        private Location ContractLocation(string? path, int start, int length)
        {
            if (path is null) return Location.None;

            for (int i = 0; i < _files.Length; i++)
            {
                if (!string.Equals(_files[i].Path, path, StringComparison.Ordinal)) continue;
                SourceText? text = _files[i].GetText();
                if (text is null) break;

                int safeStart = start < 0 ? 0 : (start > text.Length ? text.Length : start);
                int safeEnd = safeStart + (length < 0 ? 0 : length);
                if (safeEnd > text.Length) safeEnd = text.Length;

                var span = TextSpan.FromBounds(safeStart, safeEnd);
                return Location.Create(path, span, text.Lines.GetLinePositionSpan(span));
            }

            return Location.None;
        }
    }
}
