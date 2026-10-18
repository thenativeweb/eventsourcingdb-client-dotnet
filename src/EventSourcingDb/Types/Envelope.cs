using System.Text.Json;

namespace EventSourcingDb.Types;

// An answer of the v2 API, or an item of one of its streams: data, together with the type that
// tells what the data is.
internal record Envelope(string Type, JsonElement Data);

// The data of an error that the v2 API reports within its answer.
internal record ErrorData(string? Code, string? Description, JsonElement? Details);

// The data of a row that a query of the v2 API returns.
internal record RowData(JsonElement Value);
