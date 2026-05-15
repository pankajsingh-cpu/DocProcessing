using System.Text.Json;
using System.Text.Json.Serialization;

namespace DocProcessing.Contracts;

public static class JsonOpts
{
    public static readonly JsonSerializerOptions StrictCamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.Strict,
        WriteIndented = false,
    };
}
