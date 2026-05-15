namespace DocProcessing.Classifier;

public sealed class ClassifierOptions
{
    public const string SectionName = "Classifier";

    public string ContainerName { get; set; } = "blobs";

    // Polly retry budget on HTTP 429 from GitHub Models / Azure OpenAI.
    public int RateLimitMaxRetries { get; set; } = 3;

    // Truncation cap on bad-JSON snippets included in DocumentFailedEvent.
    public int FailureSnippetMaxLength { get; set; } = 500;
}
