namespace Ingestor.Contracts.V1.Responses;

public sealed record CursorPagedResponse<T>(
    IReadOnlyList<T> Items,
    string? NextCursor,
    bool HasNextPage);
