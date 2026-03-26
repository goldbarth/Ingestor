namespace Ingestor.Application.Pipeline;

internal static class LineChunker
{
    public static IReadOnlyList<IReadOnlyList<T>> Split<T>(IReadOnlyList<T> lines, int chunkSize)
    {
        if (chunkSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be greater than zero.");

        return lines
            .Chunk(chunkSize)
            .Select(IReadOnlyList<T> (chunk) => chunk)
            .ToList();
    }
}