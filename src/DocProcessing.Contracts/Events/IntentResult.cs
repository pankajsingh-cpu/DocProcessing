using System.Text.Json;

namespace DocProcessing.Contracts.Events;

// Per-intent payload is polymorphic — the shape depends on which transaction
// type was identified (drip_ocp, sell_stock, ...). The KB's payload_template
// for the intent describes the expected shape; the classifier fills it in
// with values extracted from the OCR text (null for fields it cannot find).
//
// All payload field names are snake_case to match the data definitions
// downstream consumers (workflow/RPA, persistence dashboards) expect.
public sealed record IntentResult(
    string IntentName,
    double Confidence,
    JsonElement Payload);
