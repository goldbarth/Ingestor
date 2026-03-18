using FluentAssertions;
using Ingestor.Application.Jobs.CreateImportJob;

namespace Ingestor.Tests.Unit.Jobs;

public sealed class IdempotencyKeyComputerTests
{
    [Fact]
    public void Compute_SameSupplierAndSameData_ReturnsSameKey()
    {
        var data = "article,qty\nA001,10"u8.ToArray();

        var key1 = IdempotencyKeyComputer.Compute("ACME", data);
        var key2 = IdempotencyKeyComputer.Compute("ACME", data);

        key1.Should().Be(key2);
    }

    [Fact]
    public void Compute_SameSupplierDifferentData_ReturnsDifferentKey()
    {
        var data1 = "article,qty\nA001,10"u8.ToArray();
        var data2 = "article,qty\nA001,99"u8.ToArray();

        var key1 = IdempotencyKeyComputer.Compute("ACME", data1);
        var key2 = IdempotencyKeyComputer.Compute("ACME", data2);

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void Compute_DifferentSupplierSameData_ReturnsDifferentKey()
    {
        var data = "article,qty\nA001,10"u8.ToArray();

        var key1 = IdempotencyKeyComputer.Compute("ACME", data);
        var key2 = IdempotencyKeyComputer.Compute("GLOBEX", data);

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void Compute_KeyContainsSupplierCodePrefix()
    {
        var data = "article,qty\nA001,10"u8.ToArray();

        var key = IdempotencyKeyComputer.Compute("ACME", data);

        key.Should().StartWith("ACME:");
    }
}