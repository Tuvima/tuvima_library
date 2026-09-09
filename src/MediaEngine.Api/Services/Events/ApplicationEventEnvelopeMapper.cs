using System.Text.Json;
using MediaEngine.Contracts.Authentication;
using MediaEngine.Domain.Events;

namespace MediaEngine.Api.Services.Events;

public static class ApplicationEventEnvelopeMapper
{
    public static ApplicationEventEnvelope ToEnvelope(StoredApplicationEvent value)
    {
        using var payload = JsonDocument.Parse(value.PayloadJson, new JsonDocumentOptions { MaxDepth = 16 });
        return new(
            value.EventId,
            value.EventType,
            value.Version,
            value.OccurredAt,
            value.ServerId,
            new(value.Subject.Type, value.Subject.Id, value.Subject.LibraryId, value.Subject.ProfileId),
            payload.RootElement.Clone());
    }
}
