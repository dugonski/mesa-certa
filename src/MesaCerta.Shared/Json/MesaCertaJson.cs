using System.Text.Json;
using System.Text.Json.Serialization;

namespace MesaCerta.Shared.Json;

/// <summary>Configuração JSON única para protocolo TCP, recibos e auditoria.</summary>
public static class MesaCertaJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            NumberHandling = JsonNumberHandling.Strict,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new WireEnumConverterFactory());
        options.Converters.Add(new UtcDateTimeConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static JsonElement ToElement<T>(T value) => JsonSerializer.SerializeToElement(value, Options);

    public static T Deserialize<T>(JsonElement element) =>
        element.Deserialize<T>(Options) ?? throw new JsonException("Conteúdo vazio.");

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("Conteúdo vazio.");
}

/// <summary>Enumerações como texto em maiúsculas, sem aceitar números ou outra grafia.</summary>
public sealed class WireEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(WireEnumConverter<>).MakeGenericType(typeToConvert))!;

    private sealed class WireEnumConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String || !EnumNames.TryParseWire<T>(reader.GetString(), out var value))
                throw new JsonException($"Valor inválido para {typeof(T).Name}.");
            return value;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(EnumNames.ToWire(value));
    }
}

/// <summary>Datas sempre em UTC, no formato ISO 8601 com milissegundos.</summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTime().ToUniversalTime();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
        writer.WriteStringValue(utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
    }
}
