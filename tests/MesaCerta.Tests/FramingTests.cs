using System.Text;
using MesaCerta.Shared;

namespace MesaCerta.Tests;

public class FramingTests
{
    [Fact]
    public void Tcp_aceita_mensagens_fragmentadas_agrupadas_e_unicode_dividido_entre_pacotes()
    {
        var decoder = new LineDecoder();
        // Bytes UTF-8 crus (emoji de 4 bytes) e mensagens codificadas pelo próprio protocolo.
        var encoded = Encoding.UTF8.GetBytes("{\"text\":\"Pão 🍞\"}\n")
            .Concat(LineDecoder.Encode(new { text = "Pão 🍞" }))
            .Concat(LineDecoder.Encode(new { number = 2 }))
            .ToArray();
        var received = new List<System.Text.Json.JsonElement>();
        for (var i = 0; i < encoded.Length; i++)
            received.AddRange(decoder.Push(encoded.AsSpan(i, 1)));
        Assert.Equal(3, received.Count);
        Assert.Equal("Pão 🍞", received[0].GetProperty("text").GetString());
        Assert.Equal("Pão 🍞", received[1].GetProperty("text").GetString());
        Assert.Equal(2, received[2].GetProperty("number").GetInt32());
    }

    [Fact]
    public void Tcp_encerra_frames_invalidos_e_excessivos()
    {
        var invalid = new LineDecoder();
        Assert.Throws<FrameException>(() => invalid.Push(Encoding.UTF8.GetBytes("{invalid}\n")));
        Assert.Throws<FrameException>(() => invalid.Push(Encoding.UTF8.GetBytes("{}\n")));

        var large = new LineDecoder();
        Assert.Throws<FrameException>(() => large.Push(Enumerable.Repeat((byte)'A', LineDecoder.MaxFrameBytes + 1).ToArray()));
    }

    [Fact]
    public void Linhas_vazias_sao_ignoradas() =>
        Assert.Single(new LineDecoder().Push(Encoding.UTF8.GetBytes("\n\n{\"a\":1}\n")));
}
