using Ingestor.Domain.Jobs.Enums;

namespace Ingestor.Application.Jobs.CreateImportJob;

public sealed record CreateImportJobCommand(
    string SupplierCode,
    ImportType ImportType,
    string ContentType,
    byte[] RawData);
