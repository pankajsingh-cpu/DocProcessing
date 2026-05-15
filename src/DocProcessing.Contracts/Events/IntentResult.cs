namespace DocProcessing.Contracts.Events;

public sealed record IntentResult(
    string IntentName,
    int[] Pages,
    double Confidence,
    IDictionary<string, string?> ExtractedFields);
