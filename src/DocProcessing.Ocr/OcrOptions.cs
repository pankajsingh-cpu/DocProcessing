namespace DocProcessing.Ocr;

public sealed class OcrOptions
{
    public const string SectionName = "Ocr";

    // "real" (Azure Document Intelligence) or "stub" (fixture file).
    public string Mode { get; set; } = "real";

    public string ContainerName { get; set; } = "blobs";
    public string OcrPrefix { get; set; } = "ocr";

    public string FixturePath { get; set; } = "../../samples/ocr-fixtures";
    public string GenericFixtureName { get; set; } = "_generic.json";
}

public sealed class DocumentIntelligenceOptions
{
    public const string SectionName = "AzureAI:DocumentIntelligence";

    public string Endpoint { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string ModelId { get; set; } = "prebuilt-layout";
}
