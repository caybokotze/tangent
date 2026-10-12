using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Tangent.Core.Ir;
using Tangent.Core.Text;
using IrBinding = Tangent.Core.Ir.Binding;

namespace Tangent.Core.Serialization;

/// <summary>
/// Serialises an <see cref="ArchitectureModel"/> to readable, deterministic JSON. Hand written: System.Text.Json
/// is not available on netstandard2.0 and Core must stay dependency free.
/// </summary>
public static class IrJsonWriter
{
    /// <summary>Writes the model as indented JSON with LF line endings.</summary>
    public static string Write(ArchitectureModel? model)
    {
        var sb = new StringBuilder(4096);
        if (model is null)
        {
            sb.Append("null\n");
            return sb.ToString();
        }

        sb.Append("{\n");
        WriteProperty(sb, 1, "project", model.ProjectName.IsValid ? model.Text(model.ProjectName) : null, true);
        WriteProperty(sb, 1, "root", model.Root.IsValid ? model.Text(model.Root) : null, true);
        WriteLayers(sb, model);
        WriteFrameworkGlobs(sb, model);
        WriteContracts(sb, model);
        WriteBindings(sb, model);
        WriteIgnores(sb, model);
        TrimTrailingComma(sb);
        sb.Append("}\n");
        return sb.ToString();
    }

    private static void WriteLayers(StringBuilder sb, ArchitectureModel model)
    {
        Indent(sb, 1);
        sb.Append("\"layers\": [");
        if (model.Layers.Length == 0)
        {
            sb.Append("],\n");
            return;
        }

        sb.Append('\n');
        for (int i = 0; i < model.Layers.Length; i++)
        {
            Layer layer = model.Layers[i];
            Indent(sb, 2);
            sb.Append("{ \"name\": ");
            WriteString(sb, model.Text(layer.Name));
            sb.Append(", \"namespace\": ");
            WriteString(sb, model.Globs[layer.GlobIndex].Text);
            sb.Append(", \"allowedDependencies\": [");
            ImmutableArray<int> targets = model.Dependencies.AllowedTargets(i);
            bool first = true;
            for (int t = 0; t < targets.Length; t++)
            {
                if (targets[t] == i) continue;
                if (!first) sb.Append(", ");
                WriteString(sb, model.Text(model.Layers[targets[t]].Name));
                first = false;
            }

            sb.Append("] }");
            if (i + 1 < model.Layers.Length) sb.Append(',');
            sb.Append('\n');
        }

        Indent(sb, 1);
        sb.Append("],\n");
    }

    private static void WriteFrameworkGlobs(StringBuilder sb, ArchitectureModel model)
    {
        Indent(sb, 1);
        sb.Append("\"allowedFrameworkNamespaces\": [");
        for (int i = 0; i < model.AllowedFrameworkGlobs.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            WriteString(sb, model.Globs[model.AllowedFrameworkGlobs[i]].Text);
        }

        sb.Append("],\n");
    }

    private static void WriteContracts(StringBuilder sb, ArchitectureModel model)
    {
        Indent(sb, 1);
        sb.Append("\"contracts\": [");
        if (model.Contracts.Length == 0)
        {
            sb.Append("],\n");
            return;
        }

        sb.Append('\n');
        for (int c = 0; c < model.Contracts.Length; c++)
        {
            Contract contract = model.Contracts[c];
            Indent(sb, 2);
            sb.Append("{\n");

            Indent(sb, 3);
            sb.Append("\"name\": ");
            WriteString(sb, model.Text(contract.Name));
            sb.Append(",\n");

            Indent(sb, 3);
            sb.Append("\"extends\": ");
            if (contract.ExtendsIndex >= 0) WriteString(sb, model.Text(model.Contracts[contract.ExtendsIndex].Name));
            else sb.Append("null");
            sb.Append(",\n");

            Indent(sb, 3);
            sb.Append("\"file\": ");
            WriteString(sb, model.FilePathOf(contract.DeclarationSpan));
            sb.Append(", \"line\": ").Append(contract.DeclarationSpan.Line.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\n");

            Indent(sb, 3);
            sb.Append("\"match\": ");
            if (contract.Match.HasValue)
            {
                MatchClause match = contract.Match.Value;
                WriteString(sb, IrText.DescribeMatch(model, match));
            }
            else
            {
                sb.Append("null");
            }

            sb.Append(",\n");

            Indent(sb, 3);
            sb.Append("\"rules\": [");
            if (contract.Rules.Length == 0)
            {
                sb.Append("]\n");
            }
            else
            {
                sb.Append('\n');
                for (int r = 0; r < contract.Rules.Length; r++)
                {
                    WriteRule(sb, model, contract.Rules[r], r + 1 < contract.Rules.Length);
                }

                Indent(sb, 3);
                sb.Append("]\n");
            }

            Indent(sb, 2);
            sb.Append('}');
            if (c + 1 < model.Contracts.Length) sb.Append(',');
            sb.Append('\n');
        }

        Indent(sb, 1);
        sb.Append("],\n");
    }

    private static void WriteRule(StringBuilder sb, ArchitectureModel model, in Rule rule, bool comma)
    {
        Indent(sb, 4);
        sb.Append("{ \"id\": ");
        WriteString(sb, rule.Id.Value);
        sb.Append(", \"origin\": ");
        WriteString(sb, rule.OriginContract >= 0 && rule.OriginContract < model.Contracts.Length
            ? model.Text(model.Contracts[rule.OriginContract].Name)
            : null);
        sb.Append(", \"polarity\": ");
        WriteString(sb, IrText.PolarityName(rule.Polarity));
        sb.Append(", \"selector\": ");
        WriteString(sb, IrText.DescribeSelector(model, rule.Selector));
        sb.Append(", \"predicateKind\": ");
        WriteString(sb, IrText.PredicateSlug(rule.Predicate.Kind));
        sb.Append(", \"predicate\": ");
        WriteString(sb, IrText.DescribePredicate(model, rule.Predicate));
        sb.Append(", \"tier\": ");
        WriteString(sb, IrText.TierName(rule.Tier));
        sb.Append(", \"specificity\": ").Append(rule.Specificity.ToString(CultureInfo.InvariantCulture));
        sb.Append(" }");
        if (comma) sb.Append(',');
        sb.Append('\n');
    }

    private static void WriteBindings(StringBuilder sb, ArchitectureModel model)
    {
        Indent(sb, 1);
        sb.Append("\"bindings\": [");
        if (model.Bindings.Length == 0)
        {
            sb.Append("],\n");
            return;
        }

        sb.Append('\n');
        for (int i = 0; i < model.Bindings.Length; i++)
        {
            IrBinding binding = model.Bindings[i];
            Indent(sb, 2);
            sb.Append("{ \"type\": ");
            WriteString(sb, model.Text(binding.TypeName));
            sb.Append(", \"contracts\": [");
            for (int c = 0; c < binding.ContractIndices.Length; c++)
            {
                if (c > 0) sb.Append(", ");
                WriteString(sb, model.Text(model.Contracts[binding.ContractIndices[c]].Name));
            }

            sb.Append("], \"exceptAttributes\": [");
            WriteStringIdList(sb, model, binding.ExcludedAttributes);
            sb.Append("], \"exceptRules\": [");
            WriteStringIdList(sb, model, binding.ExcludedRules);
            sb.Append("] }");
            if (i + 1 < model.Bindings.Length) sb.Append(',');
            sb.Append('\n');
        }

        Indent(sb, 1);
        sb.Append("],\n");
    }

    private static void WriteIgnores(StringBuilder sb, ArchitectureModel model)
    {
        Indent(sb, 1);
        sb.Append("\"ignores\": [");
        if (model.Ignores.Length == 0)
        {
            sb.Append("],\n");
            return;
        }

        sb.Append('\n');
        for (int i = 0; i < model.Ignores.Length; i++)
        {
            Ignore ignore = model.Ignores[i];
            Indent(sb, 2);
            sb.Append("{ \"type\": ");
            WriteString(sb, model.Text(ignore.TypeName));
            sb.Append(", \"contracts\": [");
            for (int c = 0; c < ignore.ContractIndices.Length; c++)
            {
                if (c > 0) sb.Append(", ");
                WriteString(sb, model.Text(model.Contracts[ignore.ContractIndices[c]].Name));
            }

            sb.Append("], \"allContracts\": ").Append(ignore.AllContracts ? "true" : "false");
            sb.Append(", \"reason\": ");
            WriteString(sb, model.Text(ignore.Reason));
            sb.Append(" }");
            if (i + 1 < model.Ignores.Length) sb.Append(',');
            sb.Append('\n');
        }

        Indent(sb, 1);
        sb.Append("],\n");
    }

    private static void WriteStringIdList(StringBuilder sb, ArchitectureModel model, ImmutableArray<StringId> values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            WriteString(sb, model.Text(values[i]));
        }
    }

    private static void WriteProperty(StringBuilder sb, int depth, string name, string? value, bool comma)
    {
        Indent(sb, depth);
        sb.Append('"').Append(name).Append("\": ");
        WriteString(sb, value);
        if (comma) sb.Append(',');
        sb.Append('\n');
    }

    private static void Indent(StringBuilder sb, int depth)
    {
        for (int i = 0; i < depth; i++) sb.Append("  ");
    }

    private static void TrimTrailingComma(StringBuilder sb)
    {
        int index = sb.Length - 1;
        while (index >= 0 && (sb[index] == '\n' || sb[index] == '\r')) index--;
        if (index >= 0 && sb[index] == ',') sb.Remove(index, 1);
    }

    /// <summary>Writes a JSON string literal, or <c>null</c>.</summary>
    public static void WriteString(StringBuilder sb, string? value)
    {
        if (value is null)
        {
            sb.Append("null");
            return;
        }

        sb.Append('"');
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ')
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        sb.Append('"');
    }
}
