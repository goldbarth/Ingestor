using Ingestor.Domain.Parsing;

namespace Ingestor.Application.Abstractions;

public interface ICsvParser
{
    ParseResult<DeliveryAdviceLine> Parse(Stream content);
}