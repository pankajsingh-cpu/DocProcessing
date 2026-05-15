namespace DocProcessing.Classifier;

// Thrown when the agent's output cannot be parsed into ClassifierOutput, even
// after one reformat attempt. The ClassifyConsumer translates this into a
// DocumentFailedEvent with stage="classifier-bad-json".
public sealed class ClassifierJsonException(string message, string? firstResponse = null, string? secondResponse = null)
    : Exception(message)
{
    public string? FirstResponse { get; } = firstResponse;
    public string? SecondResponse { get; } = secondResponse;
}
