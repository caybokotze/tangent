using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Tangent.Core.Diagnostics;
using Tangent.Core.Ir;
using Tangent.Core.Matching;
using Tangent.Core.Syntax;
using Tangent.Core.Text;
using IrBinding = Tangent.Core.Ir.Binding;

namespace Tangent.Core.Binding;

/// <summary>Turns parsed <c>.tan</c> files into one immutable <see cref="ArchitectureModel"/>, checking internal consistency.</summary>
public static partial class Binder
{
    /// <summary>Namespaces every layer may always reach: the BCL, and Tangent's own attributes.</summary>
    private static readonly string[] ImplicitAllowedGlobs = { "System.**", "Tangent.**" };

    /// <summary>Binds every tree as one compilation unit. Names are global across files and order is irrelevant.</summary>
    public static BindResult Bind(ImmutableArray<SyntaxTree> trees)
    {
        if (trees.IsDefault) trees = ImmutableArray<SyntaxTree>.Empty;
        for (int i = 1; i < trees.Length; i++)
        {
            if (!ReferenceEquals(trees[i].Strings, trees[0].Strings))
            {
                throw new ArgumentException(
                    "All syntax trees must share one StringTable; parse them with ModelLoader.ParseAll.",
                    nameof(trees));
            }
        }

        var state = new State(trees);
        state.Run();
        return new BindResult(state.Model, state.Diagnostics.ToImmutableArray());
    }

    // ------------------------------------------------------------------

    private struct LayerRecord
    {
        public StringId Name;
        public StringId GlobText;
        public SourceSpan Span;
        public int Tree;
    }

    private struct ContractRecord
    {
        public StringId Name;
        public StringId ExtendsName;
        public SourceSpan Span;
        public int Tree;
        public int Node;
        public int ExtendsIndex;
    }

    private struct DependencyRecord
    {
        public StringId From;
        public SourceSpan Span;
        public int Tree;
        public int Node;
    }

    private struct StatementRecord
    {
        public int Tree;
        public int Node;
    }

    private sealed class State
    {
        private readonly ImmutableArray<SyntaxTree> _trees;
        private readonly StringTable _strings;
        private readonly GlobTable _globs;
        private readonly TypePatternBuilder _patterns;
        private readonly List<AttributeGroup> _attributeGroups = new List<AttributeGroup>();
        private readonly Dictionary<string, int> _attributeGroupCache = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<Template> _templates = new List<Template>();
        private readonly Dictionary<int, int> _templateCache = new Dictionary<int, int>();
        private readonly List<Condition> _conditions = new List<Condition>();
        private readonly Dictionary<Condition, int> _conditionCache = new Dictionary<Condition, int>();
        private readonly List<int> _allowedFrameworkGlobs = new List<int>();
        private bool _hasAllowList;

        private readonly List<LayerRecord> _layerRecords = new List<LayerRecord>();
        private readonly List<ContractRecord> _contractRecords = new List<ContractRecord>();
        private readonly List<DependencyRecord> _dependencyRecords = new List<DependencyRecord>();
        private readonly List<StatementRecord> _bindRecords = new List<StatementRecord>();
        private readonly List<StatementRecord> _exemptRecords = new List<StatementRecord>();

        private readonly Dictionary<int, int> _layerByName = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _contractByName = new Dictionary<int, int>();
        private readonly Dictionary<string, int> _ruleOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);

        private StringId _projectName = StringId.Invalid;
        private StringId _projectRoot = StringId.Invalid;
        private bool _hasProject;

        private ImmutableArray<Layer> _layers = ImmutableArray<Layer>.Empty;
        private Contract[] _contracts = Array.Empty<Contract>();

        public State(ImmutableArray<SyntaxTree> trees)
        {
            _trees = trees;
            _strings = trees.Length > 0 ? trees[0].Strings : new StringTable();
            _globs = new GlobTable(_strings);
            _patterns = new TypePatternBuilder(_strings);
            Diagnostics = new List<TangentDiagnostic>();
            for (int i = 0; i < trees.Length; i++)
            {
                var tree = trees[i];
                for (int d = 0; d < tree.Diagnostics.Length; d++) Diagnostics.Add(tree.Diagnostics[d]);
            }
        }

        public List<TangentDiagnostic> Diagnostics { get; }

        public ArchitectureModel? Model { get; private set; }

        public void Run()
        {
            CollectDeclarations();
            BuildLayers();
            LayerDependencyMatrix matrix = BuildDependencyMatrix();
            ResolveExtends();
            BuildContracts();
            ImmutableArray<IrBinding> bindings = BuildBindings();
            ImmutableArray<Exemption> exemptions = BuildExemptions();

            var files = ImmutableArray.CreateBuilder<SourceFile>(_trees.Length);
            for (int i = 0; i < _trees.Length; i++) files.Add(_trees[i].Source);

            var model = new ArchitectureModel(
                _projectName,
                _projectRoot,
                _layers,
                ImmutableArray.Create(_contracts),
                bindings,
                exemptions,
                matrix,
                _strings,
                _patterns.ToTable(_templates.ToImmutableArray()),
                _globs.ToImmutable(),
                _attributeGroups.ToImmutableArray(),
                _templates.ToImmutableArray(),
                _conditions.ToImmutableArray(),
                _allowedFrameworkGlobs.ToImmutableArray(),
                files.ToImmutable(),
                _hasAllowList);

            Model = model;

            CheckTightening(model);
            CheckConflicts(model);
            CheckNeverBound(model, bindings);
            SortDiagnostics();
        }

        // --------------------------------------------------------------
        // step 1: collect declarations
        // --------------------------------------------------------------

        private void CollectDeclarations()
        {
            for (int t = 0; t < _trees.Length; t++)
            {
                SyntaxTree tree = _trees[t];
                NodeList nodes = tree.Nodes;
                foreach (int child in nodes.Children(tree.Root))
                {
                    switch (nodes[child].Kind)
                    {
                        case NodeKind.Project:
                            CollectProject(t, tree, child);
                            break;
                        case NodeKind.Contract:
                            CollectContract(t, tree, child);
                            break;
                        case NodeKind.Bind:
                            _bindRecords.Add(new StatementRecord { Tree = t, Node = child });
                            break;
                        case NodeKind.Exempt:
                            _exemptRecords.Add(new StatementRecord { Tree = t, Node = child });
                            break;
                    }
                }
            }
        }

        private void CollectProject(int treeIndex, SyntaxTree tree, int node)
        {
            NodeList nodes = tree.Nodes;
            if (_hasProject)
            {
                Report(DiagnosticCatalog.DuplicateDeclaration, nodes[node].Span, tree, "project");
            }
            else
            {
                _hasProject = true;
                _projectName = new StringId(nodes[node].Payload);
            }

            foreach (int item in nodes.Children(node))
            {
                switch (nodes[item].Kind)
                {
                    case NodeKind.ProjectRoot:
                        if (!_projectRoot.IsValid) _projectRoot = new StringId(nodes[item].Payload);
                        break;

                    case NodeKind.LayerDecl:
                    {
                        var record = new LayerRecord
                        {
                            Name = new StringId(nodes[item].Payload),
                            GlobText = new StringId(nodes[item].Payload2),
                            Span = nodes[item].Span,
                            Tree = treeIndex,
                        };

                        if (!record.Name.IsValid) break;
                        if (_layerByName.ContainsKey(record.Name.Value))
                        {
                            Report(DiagnosticCatalog.DuplicateDeclaration, record.Span, tree, _strings.GetString(record.Name));
                            break;
                        }

                        _layerByName[record.Name.Value] = _layerRecords.Count;
                        _layerRecords.Add(record);
                        break;
                    }

                    case NodeKind.Dependency:
                        _dependencyRecords.Add(new DependencyRecord
                        {
                            From = new StringId(nodes[item].Payload),
                            Span = nodes[item].Span,
                            Tree = treeIndex,
                            Node = item,
                        });
                        break;

                    case NodeKind.Allow:
                        _hasAllowList = true;
                        foreach (int glob in nodes.Children(item))
                        {
                            if (nodes[glob].Kind != NodeKind.GlobRef) continue;
                            int index = _globs.GetOrAdd(new StringId(nodes[glob].Payload));
                            if (!_allowedFrameworkGlobs.Contains(index)) _allowedFrameworkGlobs.Add(index);
                        }

                        break;
                }
            }
        }

        private void CollectContract(int treeIndex, SyntaxTree tree, int node)
        {
            NodeList nodes = tree.Nodes;
            var record = new ContractRecord
            {
                Name = new StringId(nodes[node].Payload),
                ExtendsName = new StringId(nodes[node].Payload2),
                Span = nodes[node].Span,
                Tree = treeIndex,
                Node = node,
                ExtendsIndex = -1,
            };

            if (!record.Name.IsValid) return;
            if (_contractByName.ContainsKey(record.Name.Value))
            {
                Report(DiagnosticCatalog.DuplicateDeclaration, record.Span, tree, _strings.GetString(record.Name));
                return;
            }

            _contractByName[record.Name.Value] = _contractRecords.Count;
            _contractRecords.Add(record);
        }

        // --------------------------------------------------------------
        // step 2: layers and the dependency matrix
        // --------------------------------------------------------------

        private void BuildLayers()
        {
            var builder = ImmutableArray.CreateBuilder<Layer>(_layerRecords.Count);
            for (int i = 0; i < _layerRecords.Count; i++)
            {
                LayerRecord record = _layerRecords[i];
                int glob = record.GlobText.IsValid ? _globs.GetOrAdd(record.GlobText) : _globs.GetOrAdd(string.Empty.AsSpan());
                builder.Add(new Layer(record.Name, glob, record.Span));
            }

            _layers = builder.ToImmutable();

            for (int i = ImplicitAllowedGlobs.Length - 1; i >= 0; i--)
            {
                int implicitGlob = _globs.GetOrAdd(ImplicitAllowedGlobs[i].AsSpan());
                if (!_allowedFrameworkGlobs.Contains(implicitGlob)) _allowedFrameworkGlobs.Insert(0, implicitGlob);
            }
        }

        private LayerDependencyMatrix BuildDependencyMatrix()
        {
            int count = _layers.Length;
            int wordsPerRow = count == 0 ? 0 : (count + 63) / 64;
            var bits = new ulong[count * wordsPerRow];

            for (int i = 0; i < _dependencyRecords.Count; i++)
            {
                DependencyRecord record = _dependencyRecords[i];
                SyntaxTree tree = _trees[record.Tree];
                NodeList nodes = tree.Nodes;

                int from = FindLayer(record.From);
                if (from < 0)
                {
                    Report(DiagnosticCatalog.UndeclaredLayer, record.Span, tree, _strings.GetString(record.From));
                    continue;
                }

                foreach (int target in nodes.Children(record.Node))
                {
                    if (nodes[target].Kind != NodeKind.LayerRef) continue;
                    var name = new StringId(nodes[target].Payload);
                    int to = FindLayer(name);
                    if (to < 0)
                    {
                        Report(DiagnosticCatalog.UndeclaredLayer, nodes[target].Span, tree, _strings.GetString(name));
                        continue;
                    }

                    bits[from * wordsPerRow + (to >> 6)] |= 1UL << (to & 63);
                }
            }

            var matrix = new LayerDependencyMatrix(count, bits, _allowedFrameworkGlobs.ToImmutableArray());
            DetectLayerCycle(matrix);
            return matrix;
        }

        private void DetectLayerCycle(LayerDependencyMatrix matrix)
        {
            int count = _layers.Length;
            if (count == 0) return;
            var color = new byte[count]; // 0 white, 1 grey, 2 black
            var stack = new List<int>();
            for (int i = 0; i < count; i++)
            {
                if (color[i] == 0 && Visit(i, matrix, color, stack)) return;
            }
        }

        private bool Visit(int node, LayerDependencyMatrix matrix, byte[] color, List<int> stack)
        {
            color[node] = 1;
            stack.Add(node);
            ImmutableArray<int> targets = matrix.AllowedTargets(node);
            for (int i = 0; i < targets.Length; i++)
            {
                int next = targets[i];
                if (next == node) continue;
                if (color[next] == 1)
                {
                    ReportLayerCycle(stack, next);
                    return true;
                }

                if (color[next] == 0 && Visit(next, matrix, color, stack)) return true;
            }

            stack.RemoveAt(stack.Count - 1);
            color[node] = 2;
            return false;
        }

        private void ReportLayerCycle(List<int> stack, int start)
        {
            int begin = stack.IndexOf(start);
            if (begin < 0) begin = 0;
            var chain = new System.Text.StringBuilder();
            for (int i = begin; i < stack.Count; i++)
            {
                if (i > begin) chain.Append(" -> ");
                chain.Append(_strings.GetString(_layers[stack[i]].Name));
            }

            chain.Append(" -> ").Append(_strings.GetString(_layers[start].Name));
            SourceSpan span = _layers[start].DeclarationSpan;
            Diagnostics.Add(TangentDiagnostic.Create(
                DiagnosticCatalog.LayerCycle, span, PathOf(span), chain.ToString()));
        }

        // --------------------------------------------------------------
        // step 3: extends resolution
        // --------------------------------------------------------------

        private void ResolveExtends()
        {
            for (int i = 0; i < _contractRecords.Count; i++)
            {
                ContractRecord record = _contractRecords[i];
                if (!record.ExtendsName.IsValid) continue;
                if (_contractByName.TryGetValue(record.ExtendsName.Value, out int parent))
                {
                    record.ExtendsIndex = parent;
                }
                else
                {
                    Report(DiagnosticCatalog.UnknownContract, record.Span, _trees[record.Tree], _strings.GetString(record.ExtendsName));
                }

                _contractRecords[i] = record;
            }

            DetectContractCycles();
        }

        private void DetectContractCycles()
        {
            int count = _contractRecords.Count;
            var color = new byte[count];
            var path = new List<int>();
            for (int i = 0; i < count; i++)
            {
                if (color[i] != 0) continue;
                path.Clear();
                int current = i;
                while (current >= 0 && color[current] == 0)
                {
                    color[current] = 1;
                    path.Add(current);
                    current = _contractRecords[current].ExtendsIndex;
                }

                if (current >= 0 && color[current] == 1)
                {
                    ReportContractCycle(path, current);
                    ContractRecord broken = _contractRecords[path[path.Count - 1]];
                    broken.ExtendsIndex = -1;
                    _contractRecords[path[path.Count - 1]] = broken;
                }

                for (int p = 0; p < path.Count; p++) color[path[p]] = 2;
            }
        }

        private void ReportContractCycle(List<int> path, int start)
        {
            int begin = path.IndexOf(start);
            if (begin < 0) begin = 0;
            var chain = new System.Text.StringBuilder();
            for (int i = begin; i < path.Count; i++)
            {
                if (i > begin) chain.Append(" -> ");
                chain.Append(_strings.GetString(_contractRecords[path[i]].Name));
            }

            chain.Append(" -> ").Append(_strings.GetString(_contractRecords[start].Name));
            ContractRecord record = _contractRecords[start];
            Report(DiagnosticCatalog.ContractCycle, record.Span, _trees[record.Tree], chain.ToString());
        }

        // --------------------------------------------------------------
        // step 4: rules
        // --------------------------------------------------------------

        private void BuildContracts()
        {
            int count = _contractRecords.Count;
            _contracts = new Contract[count];
            var order = TopologicalOrder();
            var flattened = new ImmutableArray<Rule>[count];

            for (int o = 0; o < order.Count; o++)
            {
                int index = order[o];
                ContractRecord record = _contractRecords[index];
                SyntaxTree tree = _trees[record.Tree];
                var own = new List<Rule>();
                MatchClause? match = null;

                var walker = new ContractWalker(this, tree, index, record, own);
                walker.Walk(ref match);

                var rules = ImmutableArray.CreateBuilder<Rule>();
                if (record.ExtendsIndex >= 0 && !flattened[record.ExtendsIndex].IsDefault)
                {
                    rules.AddRange(flattened[record.ExtendsIndex]);
                }

                rules.AddRange(own);
                ImmutableArray<Rule> all = rules.ToImmutable();
                flattened[index] = all;
                _contracts[index] = new Contract(record.Name, record.ExtendsIndex, match, all, record.Span);
            }

            for (int i = 0; i < count; i++)
            {
                if (_contracts[i] is null)
                {
                    ContractRecord record = _contractRecords[i];
                    _contracts[i] = new Contract(record.Name, record.ExtendsIndex, null, ImmutableArray<Rule>.Empty, record.Span);
                }
            }
        }

        private List<int> TopologicalOrder()
        {
            int count = _contractRecords.Count;
            var order = new List<int>(count);
            var state = new byte[count];
            for (int i = 0; i < count; i++) VisitContract(i, state, order);
            return order;
        }

        private void VisitContract(int index, byte[] state, List<int> order)
        {
            if (index < 0 || state[index] != 0) return;
            state[index] = 1;
            int parent = _contractRecords[index].ExtendsIndex;
            if (parent >= 0 && state[parent] == 0) VisitContract(parent, state, order);
            state[index] = 2;
            order.Add(index);
        }

        // --------------------------------------------------------------
        // step 5: bind / exempt
        // --------------------------------------------------------------

        private ImmutableArray<IrBinding> BuildBindings()
        {
            var result = ImmutableArray.CreateBuilder<IrBinding>(_bindRecords.Count);
            for (int i = 0; i < _bindRecords.Count; i++)
            {
                StatementRecord record = _bindRecords[i];
                SyntaxTree tree = _trees[record.Tree];
                NodeList nodes = tree.Nodes;
                var typeName = new StringId(nodes[record.Node].Payload);

                var contracts = ImmutableArray.CreateBuilder<int>();
                var attributes = ImmutableArray.CreateBuilder<StringId>();
                var rules = ImmutableArray.CreateBuilder<StringId>();

                foreach (int child in nodes.Children(record.Node))
                {
                    switch (nodes[child].Kind)
                    {
                        case NodeKind.ContractRef:
                        {
                            var name = new StringId(nodes[child].Payload);
                            int contract = FindContract(name);
                            if (contract < 0)
                            {
                                Report(DiagnosticCatalog.UnknownContract, nodes[child].Span, tree, _strings.GetString(name));
                            }
                            else if (!contracts.Contains(contract))
                            {
                                contracts.Add(contract);
                            }

                            break;
                        }

                        case NodeKind.ExceptAttribute:
                            attributes.Add(new StringId(nodes[child].Payload));
                            break;

                        case NodeKind.ExceptRule:
                            rules.Add(new StringId(nodes[child].Payload));
                            break;
                    }
                }

                result.Add(new IrBinding(
                    typeName,
                    contracts.ToImmutable(),
                    attributes.ToImmutable(),
                    rules.ToImmutable(),
                    nodes[record.Node].Span));
            }

            return result.ToImmutable();
        }

        private ImmutableArray<Exemption> BuildExemptions()
        {
            var result = ImmutableArray.CreateBuilder<Exemption>(_exemptRecords.Count);
            for (int i = 0; i < _exemptRecords.Count; i++)
            {
                StatementRecord record = _exemptRecords[i];
                SyntaxTree tree = _trees[record.Tree];
                NodeList nodes = tree.Nodes;
                var typeName = new StringId(nodes[record.Node].Payload);
                var reason = new StringId(nodes[record.Node].Payload2);

                var contracts = ImmutableArray.CreateBuilder<int>();
                foreach (int child in nodes.Children(record.Node))
                {
                    if (nodes[child].Kind != NodeKind.ContractRef) continue;
                    var name = new StringId(nodes[child].Payload);
                    int contract = FindContract(name);
                    if (contract < 0)
                    {
                        Report(DiagnosticCatalog.UnknownContract, nodes[child].Span, tree, _strings.GetString(name));
                    }
                    else if (!contracts.Contains(contract))
                    {
                        contracts.Add(contract);
                    }
                }

                result.Add(new Exemption(typeName, contracts.ToImmutable(), reason, nodes[record.Node].Span));
            }

            return result.ToImmutable();
        }

        // --------------------------------------------------------------
        // step 7: validation over the finished model
        // --------------------------------------------------------------

        private void CheckTightening(ArchitectureModel model)
        {
            for (int c = 0; c < model.Contracts.Length; c++)
            {
                Contract contract = model.Contracts[c];
                if (contract.ExtendsIndex < 0) continue;
                ImmutableArray<Rule> rules = contract.Rules;
                for (int i = 0; i < rules.Length; i++)
                {
                    if (rules[i].OriginContract != c) continue;
                    for (int j = 0; j < rules.Length; j++)
                    {
                        if (rules[j].OriginContract == c) continue;
                        if (!Loosens(rules[i], rules[j])) continue;

                        Diagnostics.Add(TangentDiagnostic.Create(
                            DiagnosticCatalog.LoosensInheritedRule,
                            rules[i].Span,
                            PathOf(rules[i].Span),
                            _strings.GetString(contract.Name),
                            rules[j].Id.Value,
                            _strings.GetString(model.Contracts[rules[j].OriginContract].Name)));
                        break;
                    }
                }
            }
        }

        private static bool Loosens(in Rule child, in Rule inherited)
        {
            if (child.Selector != inherited.Selector) return false;
            if (child.Predicate == inherited.Predicate) return child.Polarity != inherited.Polarity;
            if (child.Predicate.Kind != PredicateKind.Modifier || inherited.Predicate.Kind != PredicateKind.Modifier) return false;
            if (child.Polarity != Polarity.Require || inherited.Polarity != Polarity.Require) return false;
            return ModifiersContradict((Modifiers)child.Predicate.Payload, (Modifiers)inherited.Predicate.Payload);
        }

        private void CheckConflicts(ArchitectureModel model)
        {
            for (int c = 0; c < model.Contracts.Length; c++)
            {
                ImmutableArray<Rule> rules = model.Contracts[c].Rules;
                for (int i = 0; i < rules.Length; i++)
                {
                    for (int j = i + 1; j < rules.Length; j++)
                    {
                        if (rules[i].OriginContract != rules[j].OriginContract) continue;
                        if (rules[i].OriginContract != c) continue;
                        if (rules[i].Selector != rules[j].Selector) continue;
                        if (rules[i].Specificity != rules[j].Specificity) continue;
                        if (!Contradicts(rules[i], rules[j])) continue;

                        Diagnostics.Add(TangentDiagnostic.Create(
                            DiagnosticCatalog.ConflictingRules,
                            rules[j].Span,
                            PathOf(rules[j].Span),
                            rules[i].Id.Value + " (" + IrText.DescribeRule(model, rules[i]) + ")",
                            rules[j].Id.Value + " (" + IrText.DescribeRule(model, rules[j]) + ")"));
                    }
                }
            }
        }

        private static bool Contradicts(in Rule a, in Rule b)
        {
            if (a.Predicate == b.Predicate) return a.Polarity != b.Polarity;
            if (a.Polarity != Polarity.Require || b.Polarity != Polarity.Require) return false;
            if (a.Predicate.Kind != b.Predicate.Kind) return false;

            if (a.Predicate.Kind == PredicateKind.Modifier)
            {
                return ModifiersContradict((Modifiers)a.Predicate.Payload, (Modifiers)b.Predicate.Payload);
            }

            if (a.Predicate.Kind == PredicateKind.Accessor && a.Predicate.Payload == b.Predicate.Payload)
            {
                return ModifiersContradict((Modifiers)a.Predicate.Payload2, (Modifiers)b.Predicate.Payload2);
            }

            return false;
        }

        private static bool ModifiersContradict(Modifiers a, Modifiers b)
        {
            Modifiers accessA = a & Modifiers.AccessibilityMask;
            Modifiers accessB = b & Modifiers.AccessibilityMask;
            if (accessA != Modifiers.None && accessB != Modifiers.None && accessA != accessB) return true;

            if ((a & Modifiers.Sealed) != 0 && (b & Modifiers.Abstract) != 0) return true;
            if ((a & Modifiers.Abstract) != 0 && (b & Modifiers.Sealed) != 0) return true;

            Modifiers kindA = a & Modifiers.TypeKindMask;
            Modifiers kindB = b & Modifiers.TypeKindMask;
            if (kindA != Modifiers.None && kindB != Modifiers.None && kindA != kindB) return true;

            return false;
        }

        private void CheckNeverBound(ArchitectureModel model, ImmutableArray<IrBinding> bindings)
        {
            for (int c = 0; c < model.Contracts.Length; c++)
            {
                Contract contract = model.Contracts[c];
                if (contract.Match.HasValue) continue;

                bool bound = false;
                for (int b = 0; b < bindings.Length && !bound; b++)
                {
                    ImmutableArray<int> indices = bindings[b].ContractIndices;
                    for (int k = 0; k < indices.Length; k++)
                    {
                        if (indices[k] == c) { bound = true; break; }
                    }
                }

                if (bound) continue;

                // A contract with children that match is reachable through them.
                bool hasMatchingChild = false;
                for (int other = 0; other < model.Contracts.Length && !hasMatchingChild; other++)
                {
                    if (model.Contracts[other].ExtendsIndex == c && model.Contracts[other].Match.HasValue) hasMatchingChild = true;
                }

                if (hasMatchingChild) continue;

                Diagnostics.Add(TangentDiagnostic.Create(
                    DiagnosticCatalog.ContractNeverBound,
                    contract.DeclarationSpan,
                    PathOf(contract.DeclarationSpan),
                    _strings.GetString(contract.Name)));
            }
        }

        // --------------------------------------------------------------
        // shared helpers used by the contract walker
        // --------------------------------------------------------------

        internal StringTable Strings => _strings;

        internal int FindLayer(StringId name) =>
            name.IsValid && _layerByName.TryGetValue(name.Value, out int index) ? index : -1;

        internal int FindContract(StringId name) =>
            name.IsValid && _contractByName.TryGetValue(name.Value, out int index) ? index : -1;

        internal int AddGlob(StringId text) => _globs.GetOrAdd(text);

        internal int AddTemplate(StringId text)
        {
            if (_templateCache.TryGetValue(text.Value, out int index)) return index;
            index = _templates.Count;
            _templates.Add(Template.Parse(_strings.GetSpan(text), _strings));
            _templateCache[text.Value] = index;
            return index;
        }

        internal int AddCondition(ConditionKind kind, int left, int right, int payload)
        {
            var condition = new Condition(kind, left, right, payload);
            if (_conditionCache.TryGetValue(condition, out int index)) return index;
            index = _conditions.Count;
            _conditions.Add(condition);
            _conditionCache[condition] = index;
            return index;
        }

        internal int AddAttributeGroup(AttributeGroup group)
        {
            string key = group.Describe();
            if (_attributeGroupCache.TryGetValue(key, out int index)) return index;
            index = _attributeGroups.Count;
            _attributeGroups.Add(group);
            _attributeGroupCache[key] = index;
            return index;
        }

        internal TypePatternBuilder Patterns => _patterns;

        internal int LayerGlob(int layerIndex) =>
            layerIndex >= 0 && layerIndex < _layers.Length ? _layers[layerIndex].GlobIndex : -1;

        internal RuleId NextRuleId(StringId contract, MemberKind selectorKind, PredicateKind predicateKind, bool refined = false)
        {
            string contractName = _strings.GetString(contract);
            string selector = IrText.MemberKindSlug(selectorKind);

            // Rules written inside a `where { }` refinement get their own id namespace so a refined rule
            // never shares an id with the plain rule it narrows.
            if (refined) selector += "-where";

            string predicate = IrText.PredicateSlug(predicateKind);
            string prefix = contractName + "/" + selector + "/" + predicate;
            _ruleOrdinals.TryGetValue(prefix, out int ordinal);
            _ruleOrdinals[prefix] = ordinal + 1;
            return new RuleId(prefix + "#" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        internal void Report(DiagnosticDescriptor descriptor, SourceSpan span, SyntaxTree tree, params object?[] arguments) =>
            Diagnostics.Add(TangentDiagnostic.Create(descriptor, span, tree.Source.Path, arguments));

        internal string? PathOf(SourceSpan span) =>
            span.FileIndex >= 0 && span.FileIndex < _trees.Length ? _trees[span.FileIndex].Source.Path : null;

        private void SortDiagnostics()
        {
            Diagnostics.Sort(CompareDiagnostics);
        }

        private static int CompareDiagnostics(TangentDiagnostic a, TangentDiagnostic b)
        {
            int byFile = a.Span.FileIndex.CompareTo(b.Span.FileIndex);
            if (byFile != 0) return byFile;
            int byStart = a.Span.Start.CompareTo(b.Span.Start);
            if (byStart != 0) return byStart;
            int byCode = string.CompareOrdinal(a.Code, b.Code);
            if (byCode != 0) return byCode;
            return string.CompareOrdinal(a.Message, b.Message);
        }
    }
}
