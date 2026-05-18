using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocProcessing.Classifier.Tests;

public sealed class IntentClassifierTests
{
    private const string ValidJson =
        """
        {"intents":[{"intent":"drip_ocp","confidence":0.92,"payload":{"transaction_type":"drip_ocp","holder":{"holder_id":"1234567890","holder_name":"P. Patel"},"extracted":{"amount_number":"250.00","check_number":"003421","signature":true}}}]}
        """;

    [Fact]
    public async Task ClassifyAsync_Parses_Valid_Json_From_Agent()
    {
        var chat = FakeChatClient.WithTexts(ValidJson);
        var classifier = BuildClassifier(chat);

        var result = await classifier.ClassifyAsync("Some OCR text");

        result.Intents.Should().HaveCount(1);
        result.Intents[0].Intent.Should().Be("drip_ocp");
        result.Intents[0].Confidence.Should().BeApproximately(0.92, 0.001);

        var payload = result.Intents[0].Payload;
        payload.GetProperty("transaction_type").GetString().Should().Be("drip_ocp");
        payload.GetProperty("holder").GetProperty("holder_id").GetString().Should().Be("1234567890");
        payload.GetProperty("extracted").GetProperty("signature").GetBoolean().Should().BeTrue();

        chat.Calls.Should().HaveCount(1);
    }

    [Fact]
    public async Task ClassifyAsync_Strips_Code_Fences_From_Agent_Response()
    {
        // The system prompt forbids code fences, but models sometimes ignore that —
        // we defensively strip ```json ... ``` before parsing.
        var fenced = $"```json\n{ValidJson}\n```";
        var chat = FakeChatClient.WithTexts(fenced);
        var classifier = BuildClassifier(chat);

        var result = await classifier.ClassifyAsync("Some OCR text");

        result.Intents[0].Intent.Should().Be("drip_ocp");
    }

    [Fact]
    public async Task ClassifyAsync_Retries_With_Reformat_Prompt_On_Non_Json()
    {
        var chat = FakeChatClient.WithTexts(
            "Sure, here you go — this looks like a DRIP payment.",  // prose, no JSON
            ValidJson);
        var classifier = BuildClassifier(chat);

        var result = await classifier.ClassifyAsync("Some OCR text");

        result.Intents[0].Intent.Should().Be("drip_ocp");
        chat.Calls.Should().HaveCount(2);

        var secondTurn = chat.Calls[1].Last();
        secondTurn.Role.Should().Be(ChatRole.User);
        secondTurn.Text.Should().Contain("Reformat");
    }

    [Fact]
    public async Task ClassifyAsync_Throws_Json_Exception_When_Reformat_Also_Fails()
    {
        var chat = FakeChatClient.WithTexts(
            "I think this is a sell stock instruction",
            "Still prose, no JSON here");
        var classifier = BuildClassifier(chat);

        var act = () => classifier.ClassifyAsync("Some OCR text");

        await act.Should().ThrowAsync<ClassifierJsonException>()
            .Where(ex => ex.FirstResponse!.Contains("I think")
                      && ex.SecondResponse!.Contains("Still prose"));
        chat.Calls.Should().HaveCount(2);
    }

    [Fact]
    public async Task ClassifyAsync_Surfaces_429_When_Retries_Disabled()
    {
        // With MaxRetries=0 we verify the rate-limit exception type makes it out
        // of the agent loop unchanged, so the consumer can translate it to
        // DocumentFailedEvent(stage="rate-limited").
        var chat = FakeChatClient.WithFactories(() => throw RateLimited(retryAfterSeconds: 1));
        var classifier = BuildClassifier(chat, maxRetries: 0);

        var act = () => classifier.ClassifyAsync("Some OCR text");

        await act.Should().ThrowAsync<ClientResultException>()
            .Where(ex => ex.Status == 429);
    }

    private static IntentClassifier BuildClassifier(FakeChatClient chat, int maxRetries = 3)
    {
        var agent = ClassifierAgentFactory.Create(chat, new FakeIntentKnowledgeBase());
        var options = Options.Create(new ClassifierOptions { RateLimitMaxRetries = maxRetries });
        return new IntentClassifier(agent, options, NullLogger<IntentClassifier>.Instance);
    }

    private static ClientResultException RateLimited(int retryAfterSeconds)
    {
        var response = new FakePipelineResponse(status: 429, retryAfterSeconds);
        return new ClientResultException(response);
    }

    // Minimal PipelineResponse that surfaces the Retry-After header so our
    // delay generator can read it.
    private sealed class FakePipelineResponse(int status, int retryAfterSeconds) : PipelineResponse
    {
        private readonly FakeHeaders _headers = new(("Retry-After", retryAfterSeconds.ToString()));

        public override int Status => status;
        public override string ReasonPhrase => "Too Many Requests";
        public override Stream? ContentStream { get => null; set { } }
        public override BinaryData Content => BinaryData.FromString(string.Empty);
        protected override PipelineResponseHeaders HeadersCore => _headers;

        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;

        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Content);

        public override void Dispose() { }

        private sealed class FakeHeaders((string Name, string Value) header) : PipelineResponseHeaders
        {
            private readonly KeyValuePair<string, string> _header = new(header.Name, header.Value);

            public override bool TryGetValue(string name, out string? value)
            {
                if (string.Equals(name, _header.Key, StringComparison.OrdinalIgnoreCase))
                {
                    value = _header.Value;
                    return true;
                }
                value = null;
                return false;
            }

            public override bool TryGetValues(string name, out IEnumerable<string>? values)
            {
                if (TryGetValue(name, out var single) && single is not null)
                {
                    values = new[] { single };
                    return true;
                }
                values = null;
                return false;
            }

            public override IEnumerator<KeyValuePair<string, string>> GetEnumerator()
            {
                yield return _header;
            }
        }
    }
}
