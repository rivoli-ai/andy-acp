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
        private const string BaseUriV2 = "https://agentclientprotocol.dev/schema/v2";
        private static readonly EvaluationOptions Options;

        static AcpSchema()
        {
            Options = new EvaluationOptions { OutputFormat = OutputFormat.List };
            Register(BaseUri, "acp-v1-schema.json");
            Register(BaseUriV2, "acp-v2-schema.json");
        }

        private static void Register(string uri, string fileName)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Schema", fileName);
            Options.SchemaRegistry.Register(new Uri(uri), JsonSchema.FromText(File.ReadAllText(path)));
        }

        /// <summary>Evaluates <paramref name="instanceJson"/> against v1 <c>#/$defs/{defName}</c>.</summary>
        public static EvaluationResults Validate(string defName, string instanceJson)
            => ValidateAgainst(BaseUri, defName, instanceJson);

        /// <summary>Evaluates <paramref name="instanceJson"/> against v2 (alpha) <c>#/$defs/{defName}</c>.</summary>
        public static EvaluationResults ValidateV2(string defName, string instanceJson)
            => ValidateAgainst(BaseUriV2, defName, instanceJson);

        private static EvaluationResults ValidateAgainst(string baseUri, string defName, string instanceJson)
        {
            var refSchema = new JsonSchemaBuilder()
                .Ref($"{baseUri}#/$defs/{defName}")
                .Build();
            var node = JsonNode.Parse(instanceJson);
            return refSchema.Evaluate(node, Options);
        }

        /// <summary>Asserts that the instance validates against the named v1 schema definition.</summary>
        public static void AssertValid(string defName, string instanceJson)
        {
            var result = Validate(defName, instanceJson);
            Assert.True(result.IsValid, $"Instance is not valid against v1 {defName}:\n{instanceJson}\n{Describe(result)}");
        }

        /// <summary>Asserts that the instance validates against the named v2 (alpha) schema definition.</summary>
        public static void AssertValidV2(string defName, string instanceJson)
        {
            var result = ValidateV2(defName, instanceJson);
            Assert.True(result.IsValid, $"Instance is not valid against v2 {defName}:\n{instanceJson}\n{Describe(result)}");
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
