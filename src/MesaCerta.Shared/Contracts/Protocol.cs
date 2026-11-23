using System.Text.Json;
using System.Text.Json.Serialization;

namespace MesaCerta.Shared.Contracts;

/// <summary>Requisição enviada pelo cliente: um JSON por linha.</summary>
public sealed record Envelope
{
    public required string Id { get; init; }
    public required string Action { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Token { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CommandId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Data { get; init; }

    /// <summary>Valida o envelope recebido, equivalente ao contrato original.</summary>
    public static Envelope Parse(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object) throw new ContractException("envelope");
        Envelope envelope;
        try
        {
            envelope = message.Deserialize<Envelope>(Json.MesaCertaJson.Options)!;
        }
        catch (JsonException error)
        {
            throw new ContractException("envelope", error);
        }
        if (!Rules.IsUuid(envelope.Id)) throw new ContractException("id");
        if (envelope.Action is null || envelope.Action.Length > 50) throw new ContractException("action");
        if (envelope.Token is { Length: > 128 }) throw new ContractException("token");
        if (envelope.CommandId is not null && !Rules.IsUuid(envelope.CommandId)) throw new ContractException("commandId");
        return envelope;
    }

    /// <summary>Lê o identificador mesmo de uma mensagem inválida, para responder ao pedido certo.</summary>
    public static string? TryReadId(JsonElement message) =>
        message.ValueKind == JsonValueKind.Object
        && message.TryGetProperty("id", out var id)
        && id.ValueKind == JsonValueKind.String
            ? id.GetString()
            : null;
}

public sealed record ErrorInfo(string Code, string Message);

/// <summary>Resposta a uma requisição ou aviso de atualização (<c>event</c>).</summary>
public sealed record ServerMessage
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Ok { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Result { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ErrorInfo? Error { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Event { get; init; }

    public static ServerMessage Success(string id, JsonElement result) => new() { Id = id, Ok = true, Result = result };

    public static ServerMessage Failure(string? id, string code, string message) =>
        new() { Id = id, Ok = false, Error = new ErrorInfo(code, message) };

    public static readonly ServerMessage Changed = new() { Event = "changed" };
}

public static class ErrorCodes
{
    public const string Validation = "VALIDATION";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string Conflict = "CONFLICT";
    public const string InvalidState = "INVALID_STATE";
    public const string Limit = "LIMIT";
    public const string EmptyTab = "EMPTY_TAB";
    public const string RateLimit = "RATE_LIMIT";
    public const string InvalidLogin = "INVALID_LOGIN";
    public const string UnknownAction = "UNKNOWN_ACTION";
    public const string Internal = "INTERNAL";
    public const string Timeout = "TIMEOUT";
    public const string Connection = "CONNECTION";
}
