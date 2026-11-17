using System.Buffers;
using System.Text;
using System.Text.Json;

namespace MesaCerta.Shared;

public sealed class FrameException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// TCP é um fluxo de bytes: mensagens podem chegar fragmentadas ou agrupadas.
/// Cada mensagem é um JSON UTF-8 terminado por nova linha. O separador é um byte
/// ASCII, então um caractere multibyte nunca é dividido ao cortar a linha.
/// </summary>
public sealed class LineDecoder
{
    public const int MaxFrameBytes = 4 * 1024 * 1024;

    private readonly ArrayBufferWriter<byte> _buffer = new();
    private bool _failed;

    /// <summary>Recebe um bloco de bytes e devolve as mensagens completas.</summary>
    public IReadOnlyList<JsonElement> Push(ReadOnlySpan<byte> chunk)
    {
        if (_failed) throw new FrameException("Conexão inválida.");
        var messages = new List<JsonElement>();
        try
        {
            while (!chunk.IsEmpty)
            {
                var newline = chunk.IndexOf((byte)'\n');
                var part = newline < 0 ? chunk : chunk[..newline];
                if (_buffer.WrittenCount + part.Length > MaxFrameBytes)
                    throw new FrameException("Mensagem muito grande.");
                _buffer.Write(part);
                if (newline < 0) break;
                chunk = chunk[(newline + 1)..];
                var line = _buffer.WrittenSpan;
                if (!line.IsEmpty)
                {
                    using var document = JsonDocument.Parse(line.ToArray());
                    messages.Add(document.RootElement.Clone());
                }
                _buffer.Clear();
            }
            return messages;
        }
        catch (Exception error) when (error is JsonException or FrameException)
        {
            _failed = true;
            throw error as FrameException ?? new FrameException("Mensagem inválida.", error);
        }
    }

    public static byte[] Encode<T>(T message)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message, Json.MesaCertaJson.Options);
        if (json.Length > MaxFrameBytes) throw new FrameException("Mensagem muito grande.");
        var frame = new byte[json.Length + 1];
        json.CopyTo(frame, 0);
        frame[^1] = (byte)'\n';
        return frame;
    }

    public static string EncodeToString<T>(T message) => Encoding.UTF8.GetString(Encode(message));
}
