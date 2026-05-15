using DocProcessing.Common.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.InMemory;

namespace DocProcessing.Embeddings;

public static class EmbeddingsServiceCollectionExtensions
{
    // One-stop wiring for the intent KB. Both the KB worker and the Classifier
    // worker call this in their own DI containers because Aspire runs each
    // worker as its own process — each gets its own seeded in-memory store
    // today, both swap to Azure AI Search in cloud (Prompt 12+).
    public static IServiceCollection AddIntentKnowledgeBase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<KnowledgeBaseOptions>()
            .Bind(configuration.GetSection(KnowledgeBaseOptions.SectionName));

        services.AddDocProcEmbeddings(configuration);

        services.AddSingleton<VectorStore>(_ => new InMemoryVectorStore());
        services.AddSingleton(sp =>
        {
            var store = sp.GetRequiredService<VectorStore>();
            var opts = sp.GetRequiredService<IOptions<KnowledgeBaseOptions>>().Value;
            return store.GetCollection<string, IntentRecord>(opts.CollectionName);
        });

        services.AddSingleton<IIntentKnowledgeBase, IntentKnowledgeBase>();
        services.AddHostedService<IntentKnowledgeBaseSeeder>();

        return services;
    }
}
