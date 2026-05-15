namespace DocProcessing.Classifier;

public interface IIntentClassifier
{
    Task<ClassifierOutput> ClassifyAsync(string ocrText, CancellationToken cancellationToken = default);
}
