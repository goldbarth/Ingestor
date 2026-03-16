using Ingestor.Domain.Parsing;

namespace Ingestor.Application.Abstractions;

public interface IDeliveryAdviceParser
{
    ParseResult<DeliveryAdviceLine> Parse(Stream content);
}