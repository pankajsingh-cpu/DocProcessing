using System.ClientModel;
using System.Text.Json;
using DocProcessing.Contracts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace DocProcessing.Classifier;

// Drives the MAF agent loop with two cross-cutting concerns layered on top:
//   1. Polly retry on HTTP 429 (GitHub Models rate limits) honoring Retry-After.
//   2. Single JSON reformat attempt when the agent's output isn't parseable.
//
// On exhaustion of either policy, exceptions surface to the caller:
//   - ClientResultException (Status=429) after 3 retries  -> stage="rate-limited"
//   - ClassifierJsonException after 1 reformat            -> stage="classifier-bad-json"
public sealed class IntentClassifier : IIntentClassifier
{
    private readonly AIAgent _agent;
    private readonly ILogger<IntentClassifier> _logger;
    private readonly ResiliencePipeline _rateLimitPipeline;
    private readonly ClassifierOptions _options;

    public IntentClassifier(
        AIAgent agent,
        IOptions<ClassifierOptions> options,
        ILogger<IntentClassifier> logger)
    {
        _agent = agent;
        _logger = logger;
        _options = options.Value;
        _rateLimitPipeline = BuildRateLimitPipeline(_options.RateLimitMaxRetries, logger);
    }

    public async Task<ClassifierOutput> ClassifyAsync(string ocrText, CancellationToken cancellationToken = default)
    {
        var session = await _agent.CreateSessionAsync(cancellationToken);
        var prompt = $"Classify this document and extract required fields:\n\n{ocrText}";

        var first = await RunWithRateLimit(prompt, session, cancellationToken);
        if (TryParse(first, out var output))
        {
            return output;
        }

        _logger.LogWarning("Classifier returned non-JSON; requesting one reformat attempt.");
        const string reformatPrompt =
            "Your previous response was not valid JSON. " +
            "Reformat it as valid JSON only — no prose, no markdown, no code fences, no preamble.";

        var second = await RunWithRateLimit(reformatPrompt, session, cancellationToken);
        if (TryParse(second, out output))
        {
            return output;
        }

        throw new ClassifierJsonException(
            "Classifier did not produce valid JSON after one reformat attempt.",
            firstResponse: Truncate(first, _options.FailureSnippetMaxLength),
            secondResponse: Truncate(second, _options.FailureSnippetMaxLength));
    }

    private async Task<string> RunWithRateLimit(string prompt, AgentSession session, CancellationToken ct)
    {
        var response = await _rateLimitPipeline.ExecuteAsync(
            async token => await _agent.RunAsync(prompt, session, options: null, cancellationToken: token),
            ct);
        return response.Text ?? string.Empty;
    }

    private static bool TryParse(string text, out ClassifierOutput output)
    {
        output = default!;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var stripped = StripCodeFences(text.Trim());
        try
        {
            var parsed = JsonSerializer.Deserialize<ClassifierOutput>(stripped, JsonOpts.StrictCamelCase);
            if (parsed is null || parsed.Intents is null) return false;
            output = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string StripCodeFences(string s)
    {
        if (s.StartsWith("```"))
        {
            var idx = s.IndexOf('\n');
            if (idx > 0) s = s[(idx + 1)..];
        }
        if (s.EndsWith("```")) s = s[..^3];
        return s.Trim();
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...";

    private static ResiliencePipeline BuildRateLimitPipeline(int maxRetries, ILogger logger)
    {
        var builder = new ResiliencePipelineBuilder();
        if (maxRetries <= 0)
        {
            // Pass-through pipeline — surface 429s directly so the consumer
            // translates them to DocumentFailedEvent(stage="rate-limited") on
            // the first hit.
            return builder.Build();
        }

        return builder
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<ClientResultException>(IsRateLimited),
                MaxRetryAttempts = maxRetries,
                DelayGenerator = args =>
                {
                    var exception = args.Outcome.Exception as ClientResultException;
                    var retryAfter = ExtractRetryAfter(exception);
                    var delay = retryAfter ?? TimeSpan.FromSeconds(Math.Pow(2, args.AttemptNumber));
                    logger.LogWarning(
                        "GitHub Models 429 — sleeping {Seconds:0.#}s before retry {Attempt}/{Max} ({Source})",
                        delay.TotalSeconds,
                        args.AttemptNumber + 1,
                        maxRetries,
                        retryAfter is null ? "exponential backoff" : "Retry-After");
                    return ValueTask.FromResult<TimeSpan?>(delay);
                },
            })
            .Build();
    }

    private static bool IsRateLimited(ClientResultException ex) => ex.Status == 429;

    private static TimeSpan? ExtractRetryAfter(ClientResultException? exception)
    {
        if (exception is null) return null;

        var response = exception.GetRawResponse();
        if (response is null) return null;

        if (!response.Headers.TryGetValue("Retry-After", out var value) || string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (int.TryParse(value, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        if (DateTimeOffset.TryParse(value, out var date))
        {
            var delta = date - DateTimeOffset.UtcNow;
            return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
        }

        return null;
    }
}
