using DocProcessing.Common.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DocProcessing.Persistence.Archive;

public static class ArchiveServiceCollectionExtensions
{
    // Caller is responsible for registering BlobServiceClient (e.g. via
    // builder.AddAzureBlobServiceClient("blobs") from the Aspire integration).
    public static IServiceCollection AddDocProcArchive(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ArchiveOptions>()
            .Bind(configuration.GetSection(ArchiveOptions.SectionName));

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IArchiveService, ArchiveService>();

        return services;
    }
}
