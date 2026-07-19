using System;
using System.Text;
using Tangent.Core.Matching;

namespace Tangent.Core.Ir;

/// <summary>Renders IR elements back to DSL-like text, for diagnostics, JSON dumps and the CLI.</summary>
public static class IrText
{
    /// <summary>The slug used for a member kind inside a rule id, for example <c>methods</c>.</summary>
    public static string MemberKindSlug(MemberKind kind)
    {
        switch (kind)
        {
            case MemberKind.Ctor: return "ctor";
            case MemberKind.Method: return "methods";
            case MemberKind.Property: return "properties";
            case MemberKind.Field: return "fields";
            case MemberKind.Event: return "events";
            case MemberKind.Nested: return "nested";
            default: return "type";
        }
    }

    /// <summary>The slug used for a predicate kind inside a rule id, for example <c>injects-only</c>.</summary>
    public static string PredicateSlug(PredicateKind kind)
    {
        switch (kind)
        {
            case PredicateKind.Modifier: return "modifier";
            case PredicateKind.Inherits: return "inherits";
            case PredicateKind.Implements: return "implements";
            case PredicateKind.HasAttributes: return "attributes";
            case PredicateKind.Returns: return "returns";
            case PredicateKind.Injects: return "injects";
            case PredicateKind.InjectsOnly: return "injects-only";
            case PredicateKind.UsingNamespace: return "using";
            case PredicateKind.NewExpression: return "new";
            case PredicateKind.Accessor: return "accessor";
            case PredicateKind.InterfaceLivesIn: return "interface-lives-in";
            case PredicateKind.HasMemberKind: return "member-kind";
            case PredicateKind.EachParamAssignedToField: return "each-param-assigned-to-field";
            case PredicateKind.EachParamNullChecked: return "each-param-null-checked";
            case PredicateKind.EachParamNotStored: return "each-param-not-stored";
            case PredicateKind.Stateless: return "stateless";
            default: return "predicate";
        }
    }

    /// <summary>The lower-case name of a tier.</summary>
    public static string TierName(RuleTier tier)
    {
        switch (tier)
        {
            case RuleTier.Reference: return "reference";
            case RuleTier.Flow: return "flow";
            default: return "symbol";
        }
    }

    /// <summary>The lower-case name of a polarity.</summary>
    public static string PolarityName(Polarity polarity) => polarity == Polarity.Forbid ? "forbid" : "require";

    /// <summary>The lower-case name of a type kind.</summary>
    public static string TypeKindName(TypeKind kind)
    {
        switch (kind)
        {
            case TypeKind.Class: return "class";
            case TypeKind.Interface: return "interface";
            case TypeKind.Record: return "record";
            case TypeKind.Struct: return "struct";
            case TypeKind.Enum: return "enum";
            default: return "any";
        }
    }

    /// <summary>The lower-case name of an accessor kind.</summary>
    public static string AccessorName(AccessorKind kind)
    {
        switch (kind)
        {
            case AccessorKind.Set: return "set";
            case AccessorKind.Init: return "init";
            default: return "get";
        }
    }

    /// <summary>Renders a modifier mask in canonical order, for example <c>private readonly</c>.</summary>
    public static string ModifierText(Modifiers modifiers)
    {
        if (modifiers == Modifiers.None) return string.Empty;
        var sb = new StringBuilder();
        AppendModifier(sb, modifiers, Modifiers.Public, "public");
        AppendModifier(sb, modifiers, Modifiers.Private, "private");
        AppendModifier(sb, modifiers, Modifiers.Protected, "protected");
        AppendModifier(sb, modifiers, Modifiers.Internal, "internal");
        AppendModifier(sb, modifiers, Modifiers.Static, "static");
        AppendModifier(sb, modifiers, Modifiers.Abstract, "abstract");
        AppendModifier(sb, modifiers, Modifiers.Sealed, "sealed");
        AppendModifier(sb, modifiers, Modifiers.Readonly, "readonly");
        AppendModifier(sb, modifiers, Modifiers.Partial, "partial");
        AppendModifier(sb, modifiers, Modifiers.Class, "class");
        AppendModifier(sb, modifiers, Modifiers.Record, "record");
        AppendModifier(sb, modifiers, Modifiers.Struct, "struct");
        AppendModifier(sb, modifiers, Modifiers.Interface, "interface");
        return sb.ToString();
    }

    private static void AppendModifier(StringBuilder sb, Modifiers value, Modifiers flag, string text)
    {
        if ((value & flag) == 0) return;
        if (sb.Length > 0) sb.Append(' ');
        sb.Append(text);
    }

    /// <summary>Renders a selector, for example <c>methods where public</c>.</summary>
    public static string DescribeSelector(ArchitectureModel model, in Selector selector)
    {
        string kind = MemberKindSlug(selector.Kind);
        if (selector.ConditionIndex < 0) return kind;
        return kind + " where " + DescribeCondition(model, selector.ConditionIndex);
    }

    /// <summary>Renders a condition subtree.</summary>
    public static string DescribeCondition(ArchitectureModel model, int index)
    {
        var sb = new StringBuilder();
        AppendCondition(model, index, sb);
        return sb.ToString();
    }

    private static void AppendCondition(ArchitectureModel model, int index, StringBuilder sb)
    {
        if (index < 0 || index >= model.Conditions.Length)
        {
            sb.Append("<none>");
            return;
        }

        Condition condition = model.Conditions[index];
        switch (condition.Kind)
        {
            case ConditionKind.And:
            case ConditionKind.Or:
                sb.Append('(');
                AppendCondition(model, condition.Left, sb);
                sb.Append(condition.Kind == ConditionKind.And ? " and " : " or ");
                AppendCondition(model, condition.Right, sb);
                sb.Append(')');
                return;
            case ConditionKind.NameStartsWith:
                sb.Append("name starts with \"").Append(model.Text(new Text.StringId(condition.Payload))).Append('"');
                return;
            case ConditionKind.NameEndsWith:
                sb.Append("name ends with \"").Append(model.Text(new Text.StringId(condition.Payload))).Append('"');
                return;
            case ConditionKind.NameContains:
                sb.Append("name contains \"").Append(model.Text(new Text.StringId(condition.Payload))).Append('"');
                return;
            case ConditionKind.NameMatches:
                sb.Append("name matches \"").Append(model.Text(new Text.StringId(condition.Payload))).Append('"');
                return;
            case ConditionKind.Inherits:
                sb.Append("inherits ").Append(model.TypePatterns.DescribeGroup(condition.Payload));
                return;
            case ConditionKind.Implements:
                sb.Append("implements ").Append(model.TypePatterns.DescribeGroup(condition.Payload));
                return;
            case ConditionKind.HasAttributes:
                sb.Append("attributes ").Append(model.AttributeGroups[condition.Payload].Describe());
                return;
            case ConditionKind.Modifier:
                sb.Append(ModifierText((Modifiers)condition.Payload));
                return;
            default:
                sb.Append("<condition>");
                return;
        }
    }

    /// <summary>Renders a predicate, for example <c>injects only IMediator, ILogger&lt;*&gt;</c>.</summary>
    public static string DescribePredicate(ArchitectureModel model, in Predicate predicate)
    {
        switch (predicate.Kind)
        {
            case PredicateKind.Modifier:
                return ModifierText((Modifiers)predicate.Payload);
            case PredicateKind.Inherits:
                return "inherits " + model.TypePatterns.DescribeGroup(predicate.Payload);
            case PredicateKind.Implements:
                return "implements " + model.TypePatterns.DescribeGroup(predicate.Payload);
            case PredicateKind.HasAttributes:
                return "attributes " + model.AttributeGroups[predicate.Payload].Describe();
            case PredicateKind.Returns:
                return "returns " + model.TypePatterns.DescribeGroup(predicate.Payload);
            case PredicateKind.Injects:
                return "injects " + model.TypePatterns.DescribeGroup(predicate.Payload);
            case PredicateKind.InjectsOnly:
                return "injects only " + DescribeGroupAsList(model, predicate.Payload);
            case PredicateKind.UsingNamespace:
                return "using " + model.Globs[predicate.Payload].Text;
            case PredicateKind.NewExpression:
                return "new " + model.TypePatterns.DescribeGroup(predicate.Payload);
            case PredicateKind.Accessor:
            {
                string modifiers = ModifierText((Modifiers)predicate.Payload2);
                string accessor = AccessorName((AccessorKind)predicate.Payload);
                return modifiers.Length == 0 ? accessor : modifiers + " " + accessor;
            }

            case PredicateKind.InterfaceLivesIn:
            {
                string template = model.Templates[predicate.Payload].Text;
                string layer = predicate.Payload2 >= 0 && predicate.Payload2 < model.Layers.Length
                    ? model.Text(model.Layers[predicate.Payload2].Name)
                    : "<unknown layer>";
                return "interface " + template + " lives in " + layer;
            }

            case PredicateKind.HasMemberKind:
            {
                string modifiers = ModifierText((Modifiers)predicate.Payload2);
                string kind = MemberKindSlug((MemberKind)predicate.Payload);
                return modifiers.Length == 0 ? kind : modifiers + " " + kind;
            }

            case PredicateKind.EachParamAssignedToField:
            {
                string modifiers = ModifierText((Modifiers)predicate.Payload);
                return modifiers.Length == 0
                    ? "each parameter assigned to field"
                    : "each parameter assigned to " + modifiers + " field";
            }

            case PredicateKind.EachParamNullChecked:
                return "each parameter null-checked";
            case PredicateKind.EachParamNotStored:
                return "each parameter not stored";
            case PredicateKind.Stateless:
                return "stateless";
            default:
                return PredicateSlug(predicate.Kind);
        }
    }

    private static string DescribeGroupAsList(ArchitectureModel model, int groupIndex)
    {
        var sb = new StringBuilder();
        var group = model.TypePatterns.Group(groupIndex);
        for (int i = 0; i < group.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(model.TypePatterns.Describe(group[i]));
        }

        return sb.ToString();
    }

    /// <summary>Renders a whole rule, for example <c>forbid public set</c>.</summary>
    public static string DescribeRule(ArchitectureModel model, in Rule rule) =>
        PolarityName(rule.Polarity) + " " + DescribePredicate(model, rule.Predicate);

    /// <summary>Renders a match clause, for example <c>class in Api where name ends with "Controller"</c>.</summary>
    public static string DescribeMatch(ArchitectureModel model, in MatchClause match)
    {
        var sb = new StringBuilder();
        sb.Append(TypeKindName(match.Kind)).Append(" in ");
        if (match.LayerIndex >= 0 && match.LayerIndex < model.Layers.Length)
        {
            sb.Append(model.Text(model.Layers[match.LayerIndex].Name));
        }
        else if (match.GlobIndex >= 0 && match.GlobIndex < model.Globs.Length)
        {
            sb.Append(model.Globs[match.GlobIndex].Text);
        }
        else
        {
            sb.Append("<unknown>");
        }

        if (match.ConditionIndex >= 0)
        {
            sb.Append(" where ");
            AppendCondition(model, match.ConditionIndex, sb);
        }

        return sb.ToString();
    }
}
