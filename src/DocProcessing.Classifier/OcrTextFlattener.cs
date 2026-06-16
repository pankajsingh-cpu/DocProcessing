using System.Text;
using System.Text.Json;

namespace DocProcessing.Classifier;

// Flattens a Document Intelligence (or stub) JSON response to plain text,
// page-tagged so the agent can return page-indexed intents.
//
// Property lookups are case-insensitive because the stub fixtures use
// camelCase (matching JsonOpts.StrictCamelCase) while the real
// Document Intelligence SDK's BinaryData.FromObjectAsJson(AnalyzeResult)
// emits PascalCase property names (Pages, PageNumber, Content, Lines, Words).
//
// Resolution order, per page:
//   1. page.content (string) — stub shape
//   2. page.lines[].content   — DI prebuilt-layout shape when lines present
//   3. page.words[].content   — DI fallback when only word-level present
// Falls back to the top-level "content" field (DI populates this with the
// full concatenated document text) when no per-page text could be found.
public static class OcrTextFlattener
{
    public static string Flatten(BinaryData json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!TryGetIgnoreCase(root, "pages", out var pages) ||
            pages.ValueKind != JsonValueKind.Array)
        {
            return GetTopLevelContent(root);
        }

        var sb = new StringBuilder();
        var index = 0;
        var anyPageText = false;

        foreach (var page in pages.EnumerateArray())
        {
            index++;
            var pageNumber = index;
            if (TryGetIgnoreCase(page, "pageNumber", out var pn) && pn.ValueKind == JsonValueKind.Number)
            {
                pageNumber = pn.GetInt32();
            }

            sb.Append("--- PAGE ").Append(pageNumber).AppendLine(" ---");

            if (TryGetIgnoreCase(page, "content", out var content) &&
                content.ValueKind == JsonValueKind.String)
            {
                var s = content.GetString();
                if (!string.IsNullOrEmpty(s))
                {
                    sb.AppendLine(s);
                    anyPageText = true;
                    continue;
                }
            }

            if (TryGetIgnoreCase(page, "lines", out var lines) &&
                lines.ValueKind == JsonValueKind.Array && lines.GetArrayLength() > 0)
            {
                foreach (var line in lines.EnumerateArray())
                {
                    if (TryGetIgnoreCase(line, "content", out var lc) &&
                        lc.ValueKind == JsonValueKind.String)
                    {
                        var s = lc.GetString();
                        if (!string.IsNullOrEmpty(s))
                        {
                            sb.AppendLine(s);
                            anyPageText = true;
                        }
                    }
                }
                continue;
            }

            if (TryGetIgnoreCase(page, "words", out var words) &&
                words.ValueKind == JsonValueKind.Array && words.GetArrayLength() > 0)
            {
                var first = true;
                foreach (var word in words.EnumerateArray())
                {
                    if (TryGetIgnoreCase(word, "content", out var wc) &&
                        wc.ValueKind == JsonValueKind.String)
                    {
                        var s = wc.GetString();
                        if (string.IsNullOrEmpty(s)) continue;
                        if (!first) sb.Append(' ');
                        sb.Append(s);
                        first = false;
                        anyPageText = true;
                    }
                }
                if (!first) sb.AppendLine();
            }
        }

        // If pages existed but yielded no text (e.g. an unfamiliar shape), fall
        // back to the document-level content field.
        if (!anyPageText)
        {
            var top = GetTopLevelContent(root);
            if (!string.IsNullOrWhiteSpace(top)) return top;
        }

        return sb.ToString();
    }

    private static string GetTopLevelContent(JsonElement root) =>
        TryGetIgnoreCase(root, "content", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString() ?? string.Empty
            : string.Empty;

    private static bool TryGetIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object) return false;
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }
        return false;
    }
}
