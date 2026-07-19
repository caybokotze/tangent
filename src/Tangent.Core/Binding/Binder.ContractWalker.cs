using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Tangent.Core.Diagnostics;
using Tangent.Core.Ir;
using Tangent.Core.Matching;
using Tangent.Core.Syntax;
using Tangent.Core.Text;

namespace Tangent.Core.Binding;

public static partial class Binder
{
    /// <summary>Walks one contract body and turns it into flat rules, compiling patterns as it goes.</summary>
    private sealed class ContractWalker
    {
        private readonly State _state;
        private readonly SyntaxTree _tree;
        private readonly NodeList _nodes;
        private readonly int _contractIndex;
        private readonly ContractRecord _record;
        private readonly List<Rule> _rules;

        /// <summary>True while emitting the rules of a <c>where { }</c> refinement block.</summary>
        private bool _refined;

        public ContractWalker(State state, SyntaxTree tree, int contractIndex, ContractRecord record, List<Rule> rules)
        {
            _state = state;
            _tree = tree;
            _nodes = tree.Nodes;
            _contractIndex = contractIndex;
            _record = record;
            _rules = rules;
        }

        public void Walk(ref MatchClause? match)
        {
            foreach (int child in _nodes.Children(_record.Node))
            {
                switch (_nodes[child].Kind)
                {
                    case NodeKind.Match:
                        if (!match.HasValue) match = CompileMatch(child);
                        break;
                    case NodeKind.Rule:
                        EmitRules(child, new Selector(MemberKind.Type, -1), 0, refined: false);
                        break;
                    case NodeKind.MemberBlock:
                        WalkMemberBlock(child);
                        break;
                    case NodeKind.Refinement:
                        WalkRefinement(child, MemberKind.Type, -1, 0);
                        break;
                }
            }
        }

        // --------------------------------------------------------------

        private void WalkMemberBlock(int node)
        {
            var kind = (MemberKind)_nodes[node].Payload;
            int condition = -1;
            int specificity = 1;

            int child = _nodes.FirstChild(node);
            if (child >= 0 && IsCondition(_nodes[child].Kind))
            {
                condition = CompileCondition(child);
                specificity = 2;
                child = _nodes.NextSibling(child);
            }

            var selector = new Selector(kind, condition);
            while (child >= 0)
            {
                if (_nodes[child].Kind == NodeKind.Rule) EmitRules(child, selector, specificity, refined: false);
                else if (_nodes[child].Kind == NodeKind.Refinement) WalkRefinement(child, kind, condition, specificity);
                child = _nodes.NextSibling(child);
            }
        }

        private void WalkRefinement(int node, MemberKind kind, int outerCondition, int outerSpecificity)
        {
            int child = _nodes.FirstChild(node);
            int condition = -1;
            if (child >= 0 && IsCondition(_nodes[child].Kind))
            {
                condition = CompileCondition(child);
                child = _nodes.NextSibling(child);
            }

            int combined;
            if (outerCondition < 0) combined = condition;
            else if (condition < 0) combined = outerCondition;
            else combined = _state.AddCondition(ConditionKind.And, outerCondition, condition, 0);

            int specificity = outerSpecificity + 1;
            var selector = new Selector(kind, combined);
            while (child >= 0)
            {
                if (_nodes[child].Kind == NodeKind.Rule) EmitRules(child, selector, specificity, refined: true);
                else if (_nodes[child].Kind == NodeKind.Refinement) WalkRefinement(child, kind, combined, specificity);
                child = _nodes.NextSibling(child);
            }
        }

        // --------------------------------------------------------------

        private void EmitRules(int ruleNode, Selector selector, int specificity, bool refined)
        {
            // Emit() is only ever reached from here, so the flag can ride on the walker instead of being
            // threaded through every predicate arm below.
            _refined = refined;
            var polarity = (Polarity)_nodes[ruleNode].Payload;
            int predicate = _nodes.FirstChild(ruleNode);
            if (predicate < 0) return;
            SourceSpan span = _nodes[ruleNode].Span;

            switch (_nodes[predicate].Kind)
            {
                case NodeKind.PredModifier:
                    Emit(polarity, selector, new Predicate(PredicateKind.Modifier, _nodes[predicate].Payload), specificity, span);
                    return;

                case NodeKind.PredInherits:
                    EmitPerTypeAlt(predicate, PredicateKind.Inherits, polarity, selector, specificity, span);
                    return;

                case NodeKind.PredImplements:
                    EmitPerTypeAlt(predicate, PredicateKind.Implements, polarity, selector, specificity, span);
                    return;

                case NodeKind.PredReturns:
                    EmitPerTypeAlt(predicate, PredicateKind.Returns, polarity, selector, specificity, span);
                    return;

                case NodeKind.PredInjects:
                    EmitPerTypeAlt(predicate, PredicateKind.Injects, polarity, selector, specificity, span);
                    return;

                case NodeKind.PredNew:
                    EmitPerTypeAlt(predicate, PredicateKind.NewExpression, polarity, selector, specificity, span);
                    return;

                case NodeKind.PredInjectsOnly:
                {
                    int alt = _nodes.FirstChild(predicate);
                    if (alt < 0) return;
                    Emit(polarity, selector, new Predicate(PredicateKind.InjectsOnly, CompileTypeAlt(alt)), specificity, span);
                    return;
                }

                case NodeKind.PredAttributes:
                    foreach (int group in _nodes.Children(predicate))
                    {
                        if (_nodes[group].Kind != NodeKind.AttrGroup) continue;
                        Emit(polarity, selector, new Predicate(PredicateKind.HasAttributes, CompileAttributeGroup(group)), specificity, span);
                    }

                    return;

                case NodeKind.PredUsing:
                    foreach (int glob in _nodes.Children(predicate))
                    {
                        if (_nodes[glob].Kind != NodeKind.GlobRef) continue;
                        int index = _state.AddGlob(new StringId(_nodes[glob].Payload));
                        Emit(polarity, selector, new Predicate(PredicateKind.UsingNamespace, index), specificity, span);
                    }

                    return;

                case NodeKind.PredAccessor:
                    Emit(polarity, selector,
                        new Predicate(PredicateKind.Accessor, _nodes[predicate].Payload, _nodes[predicate].Payload2),
                        specificity, span);
                    return;

                case NodeKind.PredMemberKind:
                    Emit(polarity, selector,
                        new Predicate(PredicateKind.HasMemberKind, _nodes[predicate].Payload, _nodes[predicate].Payload2),
                        specificity, span);
                    return;

                case NodeKind.PredInterfaceLivesIn:
                {
                    var templateText = new StringId(_nodes[predicate].Payload);
                    var layerName = new StringId(_nodes[predicate].Payload2);
                    int template = templateText.IsValid ? _state.AddTemplate(templateText) : -1;
                    int layer = _state.FindLayer(layerName);
                    if (layer < 0 && layerName.IsValid)
                    {
                        _state.Report(DiagnosticCatalog.UndeclaredLayer, _nodes[predicate].Span, _tree,
                            _state.Strings.GetString(layerName));
                    }

                    Emit(polarity, selector, new Predicate(PredicateKind.InterfaceLivesIn, template, layer), specificity, span);
                    return;
                }

                case NodeKind.PredEachParamAssignedToField:
                    Emit(polarity, selector,
                        new Predicate(PredicateKind.EachParamAssignedToField, _nodes[predicate].Payload),
                        specificity, span);
                    return;

                case NodeKind.PredEachParamNullChecked:
                    Emit(polarity, selector, new Predicate(PredicateKind.EachParamNullChecked, 0), specificity, span);
                    return;

                case NodeKind.PredEachParamNotStored:
                    Emit(polarity, selector, new Predicate(PredicateKind.EachParamNotStored, 0), specificity, span);
                    return;

                case NodeKind.PredStateless:
                    Emit(polarity, selector, new Predicate(PredicateKind.Stateless, 0), specificity, span);
                    return;
            }
        }

        private void EmitPerTypeAlt(int predicate, PredicateKind kind, Polarity polarity, Selector selector, int specificity, SourceSpan span)
        {
            foreach (int alt in _nodes.Children(predicate))
            {
                if (_nodes[alt].Kind != NodeKind.TypeAlt) continue;
                Emit(polarity, selector, new Predicate(kind, CompileTypeAlt(alt)), specificity, span);
            }
        }

        private void Emit(Polarity polarity, Selector selector, Predicate predicate, int specificity, SourceSpan span)
        {
            RuleId id = _state.NextRuleId(_record.Name, selector.Kind, predicate.Kind, _refined);
            _rules.Add(new Rule(id, polarity, selector, predicate, TierOf(predicate.Kind), span, specificity, _contractIndex));
        }

        private static RuleTier TierOf(PredicateKind kind)
        {
            switch (kind)
            {
                case PredicateKind.UsingNamespace:
                case PredicateKind.NewExpression:
                    return RuleTier.Reference;
                case PredicateKind.EachParamAssignedToField:
                case PredicateKind.EachParamNullChecked:
                case PredicateKind.EachParamNotStored:
                case PredicateKind.Stateless:
                    return RuleTier.Flow;
                default:
                    return RuleTier.Symbol;
            }
        }

        // --------------------------------------------------------------

        private MatchClause CompileMatch(int node)
        {
            var kind = (TypeKind)_nodes[node].Payload;
            int layerIndex = -1;
            int globIndex = -1;
            int condition = -1;

            foreach (int child in _nodes.Children(node))
            {
                NodeKind childKind = _nodes[child].Kind;
                if (childKind == NodeKind.GlobRef)
                {
                    globIndex = _state.AddGlob(new StringId(_nodes[child].Payload));
                }
                else if (childKind == NodeKind.LayerRef)
                {
                    var name = new StringId(_nodes[child].Payload);
                    layerIndex = _state.FindLayer(name);
                    if (layerIndex >= 0)
                    {
                        globIndex = _state.LayerGlob(layerIndex);
                    }
                    else
                    {
                        ReadOnlySpan<char> text = _state.Strings.GetSpan(name);
                        if (text.IndexOf('.') >= 0)
                        {
                            globIndex = _state.AddGlob(name);
                        }
                        else
                        {
                            _state.Report(DiagnosticCatalog.UndeclaredLayer, _nodes[child].Span, _tree, text.ToString());
                        }
                    }
                }
                else if (IsCondition(childKind))
                {
                    condition = CompileCondition(child);
                }
            }

            return new MatchClause(kind, layerIndex, globIndex, condition, _nodes[node].Span);
        }

        // --------------------------------------------------------------

        private static bool IsCondition(NodeKind kind) =>
            kind >= NodeKind.CondAnd && kind <= NodeKind.CondModifier;

        private int CompileCondition(int node)
        {
            switch (_nodes[node].Kind)
            {
                case NodeKind.CondAnd:
                case NodeKind.CondOr:
                {
                    int left = _nodes.FirstChild(node);
                    int right = left < 0 ? -1 : _nodes.NextSibling(left);
                    int l = left < 0 ? -1 : CompileCondition(left);
                    int r = right < 0 ? -1 : CompileCondition(right);
                    if (l < 0) return r;
                    if (r < 0) return l;
                    return _state.AddCondition(_nodes[node].Kind == NodeKind.CondAnd ? ConditionKind.And : ConditionKind.Or, l, r, 0);
                }

                case NodeKind.CondNameStartsWith:
                    return _state.AddCondition(ConditionKind.NameStartsWith, -1, -1, _nodes[node].Payload);
                case NodeKind.CondNameEndsWith:
                    return _state.AddCondition(ConditionKind.NameEndsWith, -1, -1, _nodes[node].Payload);
                case NodeKind.CondNameContains:
                    return _state.AddCondition(ConditionKind.NameContains, -1, -1, _nodes[node].Payload);
                case NodeKind.CondNameMatches:
                    return _state.AddCondition(ConditionKind.NameMatches, -1, -1, _nodes[node].Payload);
                case NodeKind.CondModifier:
                    return _state.AddCondition(ConditionKind.Modifier, -1, -1, _nodes[node].Payload);

                case NodeKind.CondInherits:
                case NodeKind.CondImplements:
                {
                    int alt = _nodes.FirstChild(node);
                    if (alt < 0) return -1;
                    ConditionKind kind = _nodes[node].Kind == NodeKind.CondInherits ? ConditionKind.Inherits : ConditionKind.Implements;
                    return _state.AddCondition(kind, -1, -1, CompileTypeAlt(alt));
                }

                case NodeKind.CondAttributes:
                {
                    int result = -1;
                    foreach (int group in _nodes.Children(node))
                    {
                        if (_nodes[group].Kind != NodeKind.AttrGroup) continue;
                        int one = _state.AddCondition(ConditionKind.HasAttributes, -1, -1, CompileAttributeGroup(group));
                        result = result < 0 ? one : _state.AddCondition(ConditionKind.And, result, one, 0);
                    }

                    return result;
                }

                default:
                    return -1;
            }
        }

        // --------------------------------------------------------------

        private int CompileTypeAlt(int node)
        {
            int count = _nodes.ChildCount(node);
            int[] indices = count == 0 ? Array.Empty<int>() : new int[count];
            int written = 0;
            foreach (int child in _nodes.Children(node))
            {
                int pattern = CompileTypePattern(child);
                if (pattern >= 0) indices[written++] = pattern;
            }

            return _state.Patterns.AddGroup(new ReadOnlySpan<int>(indices, 0, written));
        }

        private int CompileTypePattern(int node)
        {
            NodeKind kind = _nodes[node].Kind;
            if (kind == NodeKind.TypeWildcard) return _state.Patterns.AddWildcard();
            if (kind != NodeKind.TypePattern && kind != NodeKind.TypeTemplate) return -1;

            int count = _nodes.ChildCount(node);
            int[] arguments = count == 0 ? Array.Empty<int>() : new int[count];
            int written = 0;
            foreach (int child in _nodes.Children(node))
            {
                int argument = CompileTypePattern(child);
                if (argument >= 0) arguments[written++] = argument;
            }

            var name = new StringId(_nodes[node].Payload);
            int template = kind == NodeKind.TypeTemplate && name.IsValid ? _state.AddTemplate(name) : -1;
            return _state.Patterns.AddNamed(name, template, new ReadOnlySpan<int>(arguments, 0, written));
        }

        private int CompileAttributeGroup(int node)
        {
            var alternatives = ImmutableArray.CreateBuilder<AttributeAlternative>();
            foreach (int child in _nodes.Children(node))
            {
                if (_nodes[child].Kind != NodeKind.AttrRef) continue;
                alternatives.Add(new AttributeAlternative(new StringId(_nodes[child].Payload), _nodes[child].Payload2 != 0));
            }

            return _state.AddAttributeGroup(AttributeGroup.Create(_state.Strings, alternatives.ToImmutable()));
        }
    }
}
