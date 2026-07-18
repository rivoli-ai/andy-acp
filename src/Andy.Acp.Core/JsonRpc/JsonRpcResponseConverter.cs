using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Andy.Acp.Core.JsonRpc
{
    /// <summary>
    /// Custom converter for <see cref="JsonRpcResponse"/> that enforces the JSON-RPC 2.0
    /// response contract independently of nullable values:
    /// <list type="bullet">
    /// <item>A successful response always writes a <c>result</c> member, even when the
    /// result value is <c>null</c>.</item>
    /// <item>An error response writes only an <c>error</c> member and never a <c>result</c>.</item>
    /// <item><c>result</c> and <c>error</c> are mutually exclusive.</item>
    /// <item>The <c>id</c> member is always written, including an explicit <c>null</c>.</item>
    /// </list>
    /// Success versus error is determined solely by whether <see cref="JsonRpcResponse.Error"/>
    /// is set, so a successful <c>null</c> result round-trips correctly.
    /// </summary>
    public sealed class JsonRpcResponseConverter : JsonConverter<JsonRpcResponse>
    {
        public override JsonRpcResponse Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;

            object? id = null;
            if (root.TryGetProperty("id", out var idElement))
            {
                id = ReadId(idElement);
            }

            JsonRpcError? error = null;
            object? result = null;

            if (root.TryGetProperty("error", out var errorElement) &&
                errorElement.ValueKind != JsonValueKind.Null)
            {
                error = errorElement.Deserialize<JsonRpcError>(options);
            }
            else if (root.TryGetProperty("result", out var resultElement))
            {
                // Preserve the raw result payload so callers can deserialize it to a
                // concrete type. A JsonValueKind.Null result is retained as null.
                result = resultElement.ValueKind == JsonValueKind.Null
                    ? null
                    : resultElement.Clone();
            }

            return new JsonRpcResponse
            {
                Id = id,
                Result = result,
                Error = error
            };
        }

        public override void Write(Utf8JsonWriter writer, JsonRpcResponse value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            writer.WriteString("jsonrpc", value.JsonRpc);

            writer.WritePropertyName("id");
            WriteId(writer, value.Id);

            if (value.Error != null)
            {
                writer.WritePropertyName("error");
                JsonSerializer.Serialize(writer, value.Error, options);
            }
            else
            {
                // Success: always emit result, even when null.
                writer.WritePropertyName("result");
                if (value.Result == null)
                {
                    writer.WriteNullValue();
                }
                else
                {
                    JsonSerializer.Serialize(writer, value.Result, value.Result.GetType(), options);
                }
            }

            writer.WriteEndObject();
        }

        private static object? ReadId(JsonElement idElement)
        {
            return idElement.ValueKind switch
            {
                JsonValueKind.String => idElement.GetString(),
                JsonValueKind.Number => idElement.TryGetInt64(out var l) ? l : idElement.GetDouble(),
                JsonValueKind.Null => null,
                _ => idElement.Clone()
            };
        }

        private static void WriteId(Utf8JsonWriter writer, object? id)
        {
            switch (id)
            {
                case null:
                    writer.WriteNullValue();
                    break;
                case string s:
                    writer.WriteStringValue(s);
                    break;
                case int i:
                    writer.WriteNumberValue(i);
                    break;
                case long l:
                    writer.WriteNumberValue(l);
                    break;
                case double d:
                    writer.WriteNumberValue(d);
                    break;
                case JsonElement el:
                    el.WriteTo(writer);
                    break;
                default:
                    JsonSerializer.Serialize(writer, id, id.GetType());
                    break;
            }
        }
    }
}
