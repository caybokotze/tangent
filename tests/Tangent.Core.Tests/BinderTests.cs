using System.Collections.Immutable;
using Tangent.Core.Binding;
using Tangent.Core.Diagnostics;
using Tangent.Core.Ir;
using Tangent.Core.Serialization;
using Tangent.Core.Text;
using Xunit;

namespace Tangent.Core.Tests;

/// <summary>Tests for the binder, the model it produces and the diagnostics it raises.</summary>
public sealed class BinderTests
{
    private static BindResult Bind(params string[] texts)
    {
        var files = new SourceFile[texts.Length];
        for (int i = 0; i < texts.Length; i++)
        {
            files[i] = new SourceFile("file" + i + ".tan", texts[i].Replace("\r\n", "\n"));
        }

        return ModelLoader.Load(files);
    }

    private static bool Has(BindResult result, string code)
    {
        foreach (TangentDiagnostic diagnostic in result.Diagnostics)
        {
            if (diagnostic.Code == code) return true;
        }

        return false;
    }

    private static ImmutableArray<string> RuleIds(ArchitectureModel model, string contractName)
    {
        int index = model.FindContract(contractName.AsSpan());
        Assert.True(index >= 0, "contract '" + contractName + "' not found");
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (Rule rule in model.Contracts[index].Rules) builder.Add(rule.Id.Value);
        return builder.ToImmutable();
    }

    private const string TerseController = """
        contract Controller {
            match: class in Orders.Api.** where name ends with "Controller"
            sealed
            attributes [ApiController], [Route(*)]
            ctor { require public
            injects only IMediator }
            methods where public { returns Task<*> }
        }
        """;

    private const string VerboseController = """
        // A controller in the API layer.
        contract Controller {

            /* convention-based binding */
            match:    class   in   Orders.Api.**   where   name   ends   with   "Controller"

            sealed

            attributes [ApiController] ,
                       [Route(*)]

            ctor {
                require public

                // one dependency only
                injects only IMediator
            }

            methods where public {
                returns Task<*>
            }
        }
        """;

    [Fact]
    public void Rule_ids_are_stable_under_reformatting()
    {
        BindResult terse = Bind(TerseController);
        BindResult verbose = Bind(VerboseController);

        Assert.NotNull(terse.Model);
        Assert.NotNull(verbose.Model);
        Assert.Equal(
            string.Join("\n", RuleIds(terse.Model!, "Controller")),
            string.Join("\n", RuleIds(verbose.Model!, "Controller")));
    }

    [Fact]
    public void Rule_ids_look_like_the_documented_shape()
    {
        BindResult result = Bind(TerseController);
        ImmutableArray<string> ids = RuleIds(result.Model!, "Controller");

        Assert.Contains("Controller/type/modifier#0", ids);
        Assert.Contains("Controller/type/attributes#0", ids);
        Assert.Contains("Controller/type/attributes#1", ids);
        Assert.Contains("Controller/ctor/injects-only#0", ids);
        Assert.Contains("Controller/methods/returns#0", ids);
    }

    [Fact]
    public void Comma_separated_attributes_become_separate_rules()
    {
        BindResult result = Bind("contract C { match: any in N.** \n attributes [A], [B] }");
        ArchitectureModel model = result.Model!;
        int count = 0;
        foreach (Rule rule in model.Contracts[0].Rules)
        {
            if (rule.Predicate.Kind == PredicateKind.HasAttributes) count++;
        }

        Assert.Equal(2, count);
    }

    [Fact]
    public void Alternation_stays_one_rule_with_a_group()
    {
        BindResult result = Bind("contract C { match: any in N.** \n returns Task<*> | ValueTask<*> }");
        ArchitectureModel model = result.Model!;
        Rule rule = Assert.Single(model.Contracts[0].Rules, r => r.Predicate.Kind == PredicateKind.Returns);
        Assert.Equal(2, model.TypePatterns.Group(rule.Predicate.Payload).Length);
    }

    [Fact]
    public void Selectors_and_specificity_follow_the_block_nesting()
    {
        BindResult result = Bind("""
            contract C {
                match: class in N.**
                sealed
                properties { require public }
                methods where public {
                    returns Task<*>
                    where static { forbid public }
                }
            }
            """);

        ArchitectureModel model = result.Model!;
        Contract contract = model.Contracts[0];

        Rule typeRule = Assert.Single(contract.Rules, r => r.Selector.Kind == MemberKind.Type);
        Assert.Equal(0, typeRule.Specificity);
        Assert.Equal(-1, typeRule.Selector.ConditionIndex);

        Rule propertyRule = Assert.Single(contract.Rules, r => r.Selector.Kind == MemberKind.Property);
        Assert.Equal(1, propertyRule.Specificity);
        Assert.Equal(-1, propertyRule.Selector.ConditionIndex);

        Rule returnsRule = Assert.Single(contract.Rules, r => r.Predicate.Kind == PredicateKind.Returns);
        Assert.Equal(2, returnsRule.Specificity);
        Assert.True(returnsRule.Selector.ConditionIndex >= 0);

        Rule refined = Assert.Single(
            contract.Rules,
            r => r.Selector.Kind == MemberKind.Method && r.Predicate.Kind == PredicateKind.Modifier);
        Assert.Equal(3, refined.Specificity);
        Assert.Equal(ConditionKind.And, model.Conditions[refined.Selector.ConditionIndex].Kind);
    }

    [Fact]
    public void Tiers_follow_the_predicate_kind()
    {
        BindResult result = Bind("""
            contract C {
                match: class in N.**
                sealed
                forbid using Other.**
                forbid new DbContext
                stateless
                ctor { require each parameter assigned to private readonly field }
            }
            """);

        ArchitectureModel model = result.Model!;
        foreach (Rule rule in model.Contracts[0].Rules)
        {
            RuleTier expected = rule.Predicate.Kind switch
            {
                PredicateKind.UsingNamespace => RuleTier.Reference,
                PredicateKind.NewExpression => RuleTier.Reference,
                PredicateKind.Stateless => RuleTier.Flow,
                PredicateKind.EachParamAssignedToField => RuleTier.Flow,
                _ => RuleTier.Symbol,
            };

            Assert.Equal(expected, rule.Tier);
        }
    }

    [Fact]
    public void Extends_flattens_parent_rules_with_origin()
    {
        BindResult result = Bind("""
            contract Base {
                match: class in N.Base.**
                sealed
            }

            contract Child extends Base {
                match: class in N.Child.**
                forbid public fields
            }
            """);

        ArchitectureModel model = result.Model!;
        int baseIndex = model.FindContract("Base".AsSpan());
        int childIndex = model.FindContract("Child".AsSpan());
        Contract child = model.Contracts[childIndex];

        Assert.Equal(baseIndex, model.Contracts[childIndex].ExtendsIndex);
        Assert.Equal(2, child.Rules.Length);
        Assert.Equal(baseIndex, child.Rules[0].OriginContract);
        Assert.Equal(childIndex, child.Rules[1].OriginContract);
        Assert.Equal("Base/type/modifier#0", child.Rules[0].Id.Value);
    }

    [Fact]
    public void Top_level_refinement_narrows_the_type_selector()
    {
        BindResult result = Bind("""
            contract Entity {
                match: class in N.**
                sealed
                where name ends with "Aggregate" {
                    forbid public fields
                }
            }
            """);

        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        ArchitectureModel model = result.Model!;
        Contract contract = model.Contracts[model.FindContract("Entity".AsSpan())];
        Assert.Equal(2, contract.Rules.Length);

        Rule plain = contract.Rules[0];
        Assert.Equal(MemberKind.Type, plain.Selector.Kind);
        Assert.Equal(-1, plain.Selector.ConditionIndex);
        Assert.Equal(0, plain.Specificity);
        Assert.Equal("Entity/type/modifier#0", plain.Id.Value);

        Rule refined = contract.Rules[1];
        Assert.Equal(MemberKind.Type, refined.Selector.Kind);
        Assert.True(refined.Selector.ConditionIndex >= 0);
        Assert.Equal(1, refined.Specificity);
        Assert.Equal("Entity/type-where/member-kind#0", refined.Id.Value);
        Assert.Equal(
            "name ends with \"Aggregate\"",
            IrText.DescribeCondition(model, refined.Selector.ConditionIndex));
    }

    [Fact]
    public void Nested_refinement_in_a_member_block_adds_specificity()
    {
        BindResult result = Bind("""
            contract Service {
                match: class in N.**
                properties where public {
                    where readonly { forbid set }
                }
            }
            """);

        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        ArchitectureModel model = result.Model!;
        Contract contract = model.Contracts[model.FindContract("Service".AsSpan())];
        Rule rule = Assert.Single(contract.Rules);

        Assert.Equal(MemberKind.Property, rule.Selector.Kind);
        Assert.Equal(3, rule.Specificity);
        Assert.Equal("Service/properties-where/accessor#0", rule.Id.Value);
        Assert.Equal(
            "(public and readonly)",
            IrText.DescribeCondition(model, rule.Selector.ConditionIndex));
    }

    [Fact]
    public void Layer_matrix_allows_declared_targets_and_self()
    {
        BindResult result = Bind("""
            project "P" {
                root: "src/"
                layer Api = P.Api.**
                layer App = P.App.**
                layer Domain = P.Domain.**
                Api -> App, Domain
                App -> Domain
                Domain -> nothing
            }
            """);

        ArchitectureModel model = result.Model!;
        int api = model.FindLayer("Api".AsSpan());
        int app = model.FindLayer("App".AsSpan());
        int domain = model.FindLayer("Domain".AsSpan());

        Assert.True(model.Dependencies.IsAllowed(api, app));
        Assert.True(model.Dependencies.IsAllowed(api, domain));
        Assert.True(model.Dependencies.IsAllowed(api, api));
        Assert.False(model.Dependencies.IsAllowed(domain, api));
        Assert.False(model.Dependencies.IsAllowed(app, api));
        Assert.Equal(new[] { api, app, domain }, model.Dependencies.AllowedTargets(api));
        Assert.Equal(new[] { domain }, model.Dependencies.AllowedTargets(domain));
    }

    [Fact]
    public void Layer_membership_prefers_the_most_specific_glob()
    {
        BindResult result = Bind("""
            project "P" {
                layer Domain = P.Domain.**
                layer Entities = P.Domain.Entities.**
                Domain -> nothing
                Entities -> nothing
            }
            """);

        ArchitectureModel model = result.Model!;
        Assert.Equal(model.FindLayer("Domain".AsSpan()), model.LayerOf("P.Domain.Services".AsSpan()));
        Assert.Equal(model.FindLayer("Entities".AsSpan()), model.LayerOf("P.Domain.Entities.Orders".AsSpan()));
        Assert.Equal(-1, model.LayerOf("Elsewhere".AsSpan()));
    }

    [Fact]
    public void System_is_always_allowed_and_allow_adds_more()
    {
        BindResult result = Bind("""
            project "P" {
                layer Domain = P.Domain.**
                allow Microsoft.Extensions.**
                Domain -> nothing
            }
            """);

        ArchitectureModel model = result.Model!;
        int domain = model.FindLayer("Domain".AsSpan());

        Assert.Equal(2, model.AllowedFrameworkGlobs.Length);
        Assert.Equal("System.**", model.Globs[model.AllowedFrameworkGlobs[0]].Text);
        Assert.True(model.IsReferenceAllowed(domain, "System.Collections.Generic".AsSpan()));
        Assert.True(model.IsReferenceAllowed(domain, "Microsoft.Extensions.Logging".AsSpan()));
        Assert.True(model.IsReferenceAllowed(domain, "P.Domain.Entities".AsSpan()));
    }

    [Fact]
    public void References_outside_every_layer_are_not_this_models_business()
    {
        BindResult result = Bind("""
            project "P" {
                layer Api = P.Api.**
                layer Domain = P.Domain.**
                Api -> Domain
                Domain -> nothing
            }
            """);

        ArchitectureModel model = result.Model!;
        int domain = model.FindLayer("Domain".AsSpan());
        Assert.False(model.IsReferenceAllowed(domain, "P.Api.Controllers".AsSpan()));
        Assert.True(model.IsReferenceAllowed(domain, "Newtonsoft.Json".AsSpan()));
    }

    [Fact]
    public void Bindings_and_exemptions_are_resolved()
    {
        BindResult result = Bind("""
            contract Controller { match: class in P.Api.** }
            contract Entity { match: class in P.Domain.** }
            bind P.Api.HealthController to Controller except [ProducesResponseType], "Controller/type/modifier#0"
            exempt P.Infra.Legacy reason "migrating"
            exempt P.Domain.AuditEntry from Entity reason "append-only"
            """);

        ArchitectureModel model = result.Model!;
        Assert.False(result.HasErrors, GoldenCases.RenderDiagnostics(result.Diagnostics));

        Ir.Binding binding = model.Bindings[0];
        Assert.Equal("P.Api.HealthController", model.Text(binding.TypeName));
        Assert.Equal(model.FindContract("Controller".AsSpan()), binding.ContractIndices[0]);
        Assert.Equal("ProducesResponseType", model.Text(binding.ExcludedAttributes[0]));
        Assert.Equal("Controller/type/modifier#0", model.Text(binding.ExcludedRules[0]));

        Assert.True(model.Exemptions[0].AllContracts);
        Assert.Equal("migrating", model.Text(model.Exemptions[0].Reason));
        Assert.False(model.Exemptions[1].AllContracts);
        Assert.Equal(model.FindContract("Entity".AsSpan()), model.Exemptions[1].ContractIndices[0]);
    }

    [Fact]
    public void Names_are_global_across_files()
    {
        BindResult result = Bind(
            "project \"P\" {\n layer Api = P.Api.**\n Api -> nothing\n}",
            "contract Controller { match: class in Api where name ends with \"Controller\" }",
            "bind P.Api.HealthController to Controller");

        Assert.False(result.HasErrors, GoldenCases.RenderDiagnostics(result.Diagnostics));
        ArchitectureModel model = result.Model!;
        Assert.Equal(model.FindLayer("Api".AsSpan()), model.Contracts[0].Match!.Value.LayerIndex);
        Assert.Equal(3, model.Files.Length);
    }

    [Theory]
    [InlineData("TNG1001", "bind P.T to Missing")]
    [InlineData("TNG1001", "contract A extends Missing { match: class in N.** }")]
    [InlineData("TNG1003", "contract A { match: class in N.** }\ncontract A { match: class in M.** }")]
    [InlineData("TNG1004", "contract A extends B { match: class in N.** }\ncontract B extends A { match: class in M.** }")]
    [InlineData("TNG1005", "project \"P\" {\n layer Api = P.Api.**\n Api -> Ghost\n}")]
    [InlineData("TNG1006", "project \"P\" {\n layer A = P.A.**\n layer B = P.B.**\n A -> B\n B -> A\n}")]
    [InlineData("TNG1010", "contract A { match: class in N.**\n require sealed\n forbid sealed }")]
    [InlineData("TNG1010", "contract A { match: class in N.**\n require public\n require private }")]
    [InlineData("TNG1030", "contract Orphan { sealed }")]
    public void Reports(string code, string source)
    {
        BindResult result = Bind(source);
        Assert.True(Has(result, code), GoldenCases.RenderDiagnostics(result.Diagnostics));
    }

    [Fact]
    public void Child_loosening_an_inherited_rule_is_TNG1011()
    {
        BindResult result = Bind("""
            contract Base { match: class in N.Base.**
                require sealed }
            contract Child extends Base { match: class in N.Child.**
                forbid sealed }
            """);

        Assert.True(Has(result, "TNG1011"), GoldenCases.RenderDiagnostics(result.Diagnostics));
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void Child_adding_rules_is_fine()
    {
        BindResult result = Bind("""
            contract Base { match: class in N.Base.**
                require sealed }
            contract Child extends Base { match: class in N.Child.**
                forbid public fields
                methods where public { forbid returns void } }
            """);

        Assert.False(result.HasErrors, GoldenCases.RenderDiagnostics(result.Diagnostics));
    }

    [Fact]
    public void A_contract_extended_by_a_matching_child_is_not_reported_as_unbound()
    {
        BindResult result = Bind("""
            contract Base { sealed }
            contract Child extends Base { match: class in N.** }
            """);

        Assert.False(Has(result, "TNG1030"), GoldenCases.RenderDiagnostics(result.Diagnostics));
    }

    [Fact]
    public void Parse_errors_never_throw_and_still_produce_a_model()
    {
        BindResult result = Bind("contract Broken { match: class in N.** \n frobnicate \n sealed");
        Assert.NotNull(result.Model);
        Assert.True(Has(result, "TNG1000"));
    }

    [Fact]
    public void Json_output_is_deterministic()
    {
        string first = IrJsonWriter.Write(Bind(TerseController).Model);
        string second = IrJsonWriter.Write(Bind(TerseController).Model);
        Assert.Equal(first, second);
        Assert.StartsWith("{\n", first);
        Assert.EndsWith("}\n", first);
    }

    [Fact]
    public void Json_of_a_null_model_is_null()
    {
        Assert.Equal("null\n", IrJsonWriter.Write(null));
    }
}

/// <summary>Tests for the model cache.</summary>
public sealed class ModelCacheTests
{
    [Fact]
    public void GetOrAdd_calls_the_factory_once_per_key()
    {
        ModelCache.Clear();
        int calls = 0;
        BindResult Factory()
        {
            calls++;
            return ModelLoader.Load(new[] { new SourceFile("a.tan", "contract A { match: class in N.** }") });
        }

        string key = "test-key-" + Guid.NewGuid();
        BindResult first = ModelCache.GetOrAdd(key, Factory);
        BindResult second = ModelCache.GetOrAdd(key, Factory);

        Assert.Same(first, second);
        Assert.Equal(1, calls);
        Assert.True(ModelCache.TryGet(key, out BindResult found));
        Assert.Same(first, found);

        ModelCache.Clear();
        Assert.False(ModelCache.TryGet(key, out _));
    }
}
