using System.Text;
using System.Text.Json;

namespace DocProcessing.Classifier;

// Flattens a Document Intelligence (or stub) JSON response to plain text,
// page-tagged so the agent can return page-indexed intents.
//
// Handles two shapes:
//   - pages[].content      (concatenated page text — stub + real DI when set)
//   - pages[].lines[].content  (real DI fallback when page-level content is absent)
public static class OcrTextFlattener
{
    public static string Flatten(BinaryData json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("pages", out var pages) ||
            pages.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        var index = 0;
        foreach (var page in pages.EnumerateArray())
        {
            index++;
            var pageNumber = page.TryGetProperty("pageNumber", out var pn) && pn.ValueKind == JsonValueKind.Number
                ? pn.GetInt32()
                : index;

            sb.Append("--- PAGE ").Append(pageNumber).AppendLine(" ---");

            if (page.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            {
                sb.AppendLine(content.GetString());
            }
            else if (page.TryGetProperty("lines", out var lines) && lines.ValueKind == JsonValueKind.Array)
            {
                foreach (var line in lines.EnumerateArray())
                {
                    if (line.TryGetProperty("content", out var lc) && lc.ValueKind == JsonValueKind.String)
                    {
                        sb.AppendLine(lc.GetString());
                    }
                }
            }
        }

        return sb.ToString();
    }
}
