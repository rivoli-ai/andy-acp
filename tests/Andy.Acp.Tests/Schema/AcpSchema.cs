using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Json.Schema;
using Xunit;

namespace Andy.Acp.Tests.Schema
{
    /// <summary>
    /// Validates JSON instances against the pinned, vendored stable ACP v1 JSON Schema
    /// (<c>Schema/acp-v1-schema.json</c>, draft 2020-12). Tests use this to fail CI on any
    /// schema-invalid wire output.
    /// </summary>
    public static class AcpSchema
    {
        private const string BaseUri = "https://agentclientprotocol.dev/schema/v1";
        private static readonly EvaluationOptions Options;

        static AcpSchema()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Schema", "acp-v1-schema.json");
            var schema = JsonSchema.FromText(File.ReadAllText(path));
            Options = new EvaluationOptions { OutputFormat = OutputFormat.List };
            Options.SchemaRegistry.Register(new Uri(BaseUri), schema);
        }

        /// <summary>Evaluates <paramref name="instanceJson"/> against <c>#/$defs/{defName}</c>.</summary>
        public static EvaluationResults Validate(string defName, string instanceJson)
        {
            var refSchema = new JsonSchemaBuilder()
                .Ref($"{BaseUri}#/$defs/{defName}")
                .Build();
            var node = JsonNode.Parse(instanceJson);
            return refSchema.Evaluate(node, Options);
        }

        /// <summary>Asserts that the instance validates against the named schema definition.</summary>
        public static void AssertValid(string defName, string instanceJson)
        {
            var result = Validate(defName, instanceJson);
            Assert.True(result.IsValid, $"Instance is not valid against {defName}:\n{instanceJson}\n{Describe(result)}");
        }

        private static string Describe(EvaluationResults result)
        {
            var errors = result.Details
                .Where(d => d.HasErrors)
                .SelectMany(d => d.Errors!.Select(e => $"  {d.InstanceLocation}: {e.Value}"));
            return string.Join("\n", errors);
        }
    }
}
