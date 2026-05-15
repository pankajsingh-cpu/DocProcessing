using Microsoft.Extensions.AI;

namespace DocProcessing.Classifier.Tests;

// Minimal IChatClient stub that returns canned responses (or throws canned
// exceptions) in order. Lets unit tests drive the agent without hitting
// GitHub Models.
internal sealed class FakeChatClient : IChatClient
{
    private readonly Queue<Func<ChatResponse>> _factories;

    public List<List<ChatMessage>> Calls { get; } = new();

    public FakeChatClient(IEnumerable<Func<ChatResponse>> factories)
    {
        _factories = new Queue<Func<ChatResponse>>(factories);
    }

    public static FakeChatClient WithTexts(params string[] texts) =>
        new(texts.Select<string, Func<ChatResponse>>(
            t => () => new ChatResponse(new ChatMessage(ChatRole.Assistant, t))));

    public static FakeChatClient WithFactories(params Func<ChatResponse>[] factories) =>
        new(factories);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(messages.ToList());
        if (_factories.Count == 0)
        {
            throw new InvalidOperationException("FakeChatClient exhausted — no more canned responses queued.");
        }
        return Task.FromResult(_factories.Dequeue()());
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Streaming is not used by IntentClassifier.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
