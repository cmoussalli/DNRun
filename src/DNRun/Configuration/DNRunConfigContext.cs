using System.Text.Json;
using System.Text.Json.Serialization;

namespace DNRun.Configuration;

/// <summary>
/// Source-generated serialization context. Required rather than optional: reflection-based
/// serialization is unsupported under Native AOT (plan D5).
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DNRunConfig))]
internal sealed partial class DNRunConfigContext : JsonSerializerContext;
