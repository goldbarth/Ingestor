namespace Ingestor.Application.Jobs.CreateImportJob;

public static class IdempotencyKeyComputer
{
    public static string Compute(string supplierCode, byte[] rawData)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(rawData);
        return $"{supplierCode}:{Convert.ToHexString(hash)}";
    }
}