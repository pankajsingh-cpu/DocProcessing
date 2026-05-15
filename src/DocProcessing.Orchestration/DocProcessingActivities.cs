using System.Diagnostics;

namespace DocProcessing.Orchestration;

// Custom span source. ServiceDefaults adds "DocProcessing" to the OTel tracing
// pipeline, so any Activity started from here ends up on the Aspire dashboard.
//
// We emit two short-lived spans for the document lifecycle — start when the
// saga first ingests the document, end on Persisted/Failed. They're children
// of the inbound MassTransit consume span, so they slot naturally into the
// trace tree that already carries the document across workers.
public static class DocProcessingActivities
{
    public const string SourceName = "DocProcessing";

    public const string ProcessStartName = "document.process";
    public const string ProcessCompletedName = "document.process.completed";

    public const string DocumentIdTag = "document.id";
    public const string BatchIdTag = "document.batch_id";
    public const string SourceBlobTag = "document.source_blob";
    public const string OutcomeTag = "document.outcome";
    public const string FailureStageTag = "document.failure_stage";
    public const string FailureErrorTag = "document.failure_error";

    public static readonly ActivitySource Source = new(SourceName);

    public static Activity? StartProcessing(Guid documentId, string batchId, string sourceBlobPath)
    {
        var activity = Source.StartActivity(ProcessStartName, ActivityKind.Internal);
        if (activity is null) return null;

        activity.SetTag(DocumentIdTag, documentId);
        activity.SetTag(BatchIdTag, batchId);
        activity.SetTag(SourceBlobTag, sourceBlobPath);
        return activity;
    }

    public static Activity? MarkSucceeded(Guid documentId)
    {
        var activity = Source.StartActivity(ProcessCompletedName, ActivityKind.Internal);
        if (activity is null) return null;

        activity.SetTag(DocumentIdTag, documentId);
        activity.SetTag(OutcomeTag, "success");
        activity.SetStatus(ActivityStatusCode.Ok);
        return activity;
    }

    public static Activity? MarkFailed(Guid documentId, string stage, string error)
    {
        var activity = Source.StartActivity(ProcessCompletedName, ActivityKind.Internal);
        if (activity is null) return null;

        activity.SetTag(DocumentIdTag, documentId);
        activity.SetTag(OutcomeTag, "failed");
        activity.SetTag(FailureStageTag, stage);
        activity.SetTag(FailureErrorTag, error);
        activity.SetStatus(ActivityStatusCode.Error, error);
        return activity;
    }
}
