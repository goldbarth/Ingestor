using Ingestor.Contracts.V1.Enums;

namespace Ingestor.Contracts.V1.Requests;

public sealed record CreateImportJobRequest(
    string SupplierCode,
    ImportType ImportType);