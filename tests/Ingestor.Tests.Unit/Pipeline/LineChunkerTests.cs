using FluentAssertions;
using Ingestor.Application.Pipeline;

namespace Ingestor.Tests.Unit.Pipeline;

public sealed class LineChunkerTests
{
    [Fact]
    public void Split_EmptyList_ReturnsEmptyResult()
    {
        var result = LineChunker.Split(Array.Empty<int>(), chunkSize: 10);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Split_ItemsLessThanChunkSize_ReturnsSingleChunk()
    {
        var lines = Enumerable.Range(1, 3).ToList();

        var result = LineChunker.Split(lines, chunkSize: 10);

        result.Should().HaveCount(1);
        result[0].Should().BeEquivalentTo(lines);
    }

    [Fact]
    public void Split_ItemsEqualToChunkSize_ReturnsSingleChunk()
    {
        var lines = Enumerable.Range(1, 500).ToList();

        var result = LineChunker.Split(lines, chunkSize: 500);

        result.Should().HaveCount(1);
        result[0].Should().HaveCount(500);
    }

    [Fact]
    public void Split_ExactMultiple_ReturnsEqualSizedChunks()
    {
        var lines = Enumerable.Range(1, 1000).ToList();

        var result = LineChunker.Split(lines, chunkSize: 500);

        result.Should().HaveCount(2);
        result[0].Should().HaveCount(500);
        result[1].Should().HaveCount(500);
    }

    [Fact]
    public void Split_UnevenDivision_LastChunkContainsRemainder()
    {
        var lines = Enumerable.Range(1, 11).ToList();

        var result = LineChunker.Split(lines, chunkSize: 4);

        result.Should().HaveCount(3);
        result[0].Should().HaveCount(4);
        result[1].Should().HaveCount(4);
        result[2].Should().HaveCount(3);
    }

    [Fact]
    public void Split_TenThousandLines_ReturnsTwentyChunksOfFiveHundred()
    {
        var lines = Enumerable.Range(1, 10_000).ToList();

        var result = LineChunker.Split(lines, chunkSize: 500);

        result.Should().HaveCount(20);
        result.Should().AllSatisfy(chunk => chunk.Should().HaveCount(500));
    }

    [Fact]
    public void Split_PreservesAllItems()
    {
        var lines = Enumerable.Range(1, 7).ToList();

        var result = LineChunker.Split(lines, chunkSize: 3);

        result.SelectMany(chunk => chunk).Should().BeEquivalentTo(lines, options => options.WithStrictOrdering());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Split_InvalidChunkSize_ThrowsArgumentOutOfRangeException(int chunkSize)
    {
        var lines = Enumerable.Range(1, 5).ToList();

        var act = () => LineChunker.Split(lines, chunkSize);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("chunkSize");
    }
}