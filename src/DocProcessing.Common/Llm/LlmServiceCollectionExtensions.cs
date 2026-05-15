using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;

namespace DocProcessing.Common.Llm;

// Single entry point for wiring LLM clients across modules. Module code must
// consume only IChatClient / IEmbeddingGenerator<string, Embedding<float>> from
// DI and never reach for ChatClient/EmbeddingClient or the GitHub Models URL
// directly (CLAUDE.md invariant #3).
public static class LlmServiceCollectionExtensions
{
    public const string GithubModelsProvider = "github-models";
    public const string AzureOpenAIProvider = "azure-openai";

    public const string DefaultEndpoint = "https://models.github.ai/inference";
    public const string DefaultChatModel = "openai/gpt-4.1";
    public const string DefaultEmbeddingModel = "openai/text-embedding-3-small";

    public static IServiceCollection AddDocProcEmbeddings(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration["Llm:Provider"] ?? GithubModelsProvider;

        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(_ =>
            provider switch
            {
                GithubModelsProvider => BuildGithubModelsEmbeddings(configuration),
                AzureOpenAIProvider => throw new NotSupportedException(
                    "Llm:Provider=azure-openai is wired in Prompt 12. " +
                    "Use 'github-models' for local dev."),
                _ => throw new InvalidOperationException(
                    $"Unknown Llm:Provider '{provider}'. Expected '{GithubModelsProvider}' or '{AzureOpenAIProvider}'.")
            });

        return services;
    }

    private static IEmbeddingGenerator<string, Embedding<float>> BuildGithubModelsEmbeddings(
        IConfiguration configuration)
    {
        var endpoint = new Uri(configuration["Llm:Endpoint"] ?? DefaultEndpoint);
        var model = configuration["Llm:EmbeddingModel"] ?? DefaultEmbeddingModel;
        var pat = RequireGithubToken(configuration);

        var options = new OpenAIClientOptions { Endpoint = endpoint };

        return new EmbeddingClient(model, new ApiKeyCredential(pat), options)
            .AsIEmbeddingGenerator();
    }

    public static IServiceCollection AddDocProcChatClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration["Llm:Provider"] ?? GithubModelsProvider;

        services.AddSingleton<IChatClient>(sp =>
        {
            var loggerFactory = sp.GetService<ILoggerFactory>();

            return provider switch
            {
                GithubModelsProvider => BuildGithubModelsChat(configuration, loggerFactory),
                AzureOpenAIProvider => throw new NotSupportedException(
                    "Llm:Provider=azure-openai is wired in Prompt 12. " +
                    "Use 'github-models' for local dev."),
                _ => throw new InvalidOperationException(
                    $"Unknown Llm:Provider '{provider}'. Expected '{GithubModelsProvider}' or '{AzureOpenAIProvider}'.")
            };
        });

        return services;
    }

    private static IChatClient BuildGithubModelsChat(
        IConfiguration configuration,
        ILoggerFactory? loggerFactory)
    {
        var endpoint = new Uri(configuration["Llm:Endpoint"] ?? DefaultEndpoint);
        var model = configuration["Llm:ChatModel"] ?? DefaultChatModel;
        var pat = RequireGithubToken(configuration);

        var options = new OpenAIClientOptions { Endpoint = endpoint };

        // ChatClient → IChatClient adapter → MAF-required middleware pipeline.
        // UseFunctionInvocation runs the whole tool-call loop automatically.
        var builder = new ChatClient(model, new ApiKeyCredential(pat), options)
            .AsIChatClient()
            .AsBuilder()
            .UseFunctionInvocation()
            .UseOpenTelemetry();

        if (loggerFactory is not null)
        {
            builder = builder.UseLogging(loggerFactory);
        }

        return builder.Build();
    }

    private static string RequireGithubToken(IConfiguration configuration) =>
        configuration["GITHUB_TOKEN"]
        ?? throw new InvalidOperationException(
            "GITHUB_TOKEN is not configured. Set it on the AppHost via " +
            "'dotnet user-secrets set GITHUB_TOKEN ghp_... --project src/DocProcessing.AppHost'.");
}
