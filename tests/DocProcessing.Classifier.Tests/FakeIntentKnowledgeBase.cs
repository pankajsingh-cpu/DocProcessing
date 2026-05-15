using DocProcessing.Embeddings;

namespace DocProcessing.Classifier.Tests;

// Stub KB so the classifier doesn't pull real embeddings during unit tests.
// IntentClassifier doesn't call the KB itself — the agent invokes the tools
// only when the model asks for them. The fake chat client we pair this with
// returns canned text and never asks for tool calls, so this stub usually
// never sees a call.
internal sealed class FakeIntentKnowledgeBase : IIntentKnowledgeBase
{
    public Task<IReadOnlyList<IntentMatch>> SearchAsync(
        string query, int top = 3, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<IntentMatch>>(Array.Empty<IntentMatch>());

    public Task<IntentSchema?> GetSchemaAsync(
        string intentName, CancellationToken cancellationToken = default) =>
        Task.FromResult<IntentSchema?>(null);
}
