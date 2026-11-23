using System.Text.Json;
using MesaCerta.Server.Transport;
using MesaCerta.Shared;
using MesaCerta.Shared.Contracts;
using MesaCerta.Shared.Json;

namespace MesaCerta.Tests;

public class ContractTests
{
    private const string TabId = "0d033e41-72e2-485f-9178-16a56de2aa32";
    private const string ProductId = "0d033e41-72e2-485f-9178-16a56de2aa33";

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private static bool Accepts(string action, string data)
    {
        try
        {
            Router.ParseData(action, Json(data));
            return true;
        }
        catch (ContractException)
        {
            return false;
        }
    }

    [Fact]
    public void Validacao_recusa_quantidades_negativas_valores_fracionarios_e_forma_nao_suportada()
    {
        var input = $"\"tabId\":\"{TabId}\",\"productId\":\"{ProductId}\",\"version\":1";
        Assert.True(Accepts(Commands.TabsAdd, $"{{{input},\"quantity\":2}}"));
        Assert.False(Accepts(Commands.TabsAdd, $"{{{input},\"quantity\":-1}}"));
        Assert.False(Accepts(Commands.TabsAdd, $"{{{input},\"quantity\":1.5}}"));
        Assert.False(Accepts(Commands.TabsAdd, $"{{{input},\"quantity\":\"2\"}}"));
        Assert.False(Accepts(Commands.TabsAdd, $"{{{input}}}"));
        Assert.False(Accepts(Commands.TabsPay, $"{{{input},\"method\":\"CASH\"}}"));
        Assert.False(Accepts(Commands.TabsPay, $"{{{input},\"method\":\"pix\"}}"));
        Assert.True(Accepts(Commands.TabsPay, $"{{{input},\"method\":\"PIX\"}}"));
    }

    [Fact]
    public void Textos_sao_normalizados_e_limites_respeitados()
    {
        var login = (LoginRequest)Router.ParseData(Commands.Login, Json("{\"username\":\"  admin \",\"password\":\" s \",\"terminal\":\" Caixa 01 \"}"));
        Assert.Equal("admin", login.Username);
        Assert.Equal(" s ", login.Password);
        Assert.Equal("Caixa 01", login.Terminal);
        Assert.False(Accepts(Commands.Login, "{\"username\":\"   \",\"password\":\"x\",\"terminal\":\"t\"}"));
        Assert.False(Accepts(Commands.UsersCreate, "{\"name\":\"Ana\",\"username\":\"a b\",\"password\":\"12345678\",\"role\":\"ADMIN\"}"));
        Assert.True(Accepts(Commands.UsersCreate, "{\"name\":\"Ana\",\"username\":\"ana.b\",\"password\":\"12345678\",\"role\":\"CASHIER\"}"));
        Assert.False(Accepts(Commands.ProductsSave, "{\"name\":\"X\",\"category\":\"Y\",\"priceCents\":0,\"active\":true}"));
        var product = (SaveProductRequest)Router.ParseData(Commands.ProductsSave, Json("{\"name\":\"X\",\"category\":\"Y\",\"priceCents\":10,\"active\":true}"));
        Assert.Equal(SaleType.Unit, product.SaleType);
        var note = (AddItemRequest)Router.ParseData(Commands.TabsAdd, Json($"{{\"tabId\":\"{TabId}\",\"productId\":\"{ProductId}\",\"version\":0,\"quantity\":1}}"));
        Assert.Equal("", note.Note);
        Assert.Null(note.WeightGrams);
    }

    [Fact]
    public void Envelope_exige_identificador_uuid()
    {
        Assert.Throws<ContractException>(() => Envelope.Parse(Json("{\"id\":\"1\",\"action\":\"snapshot\"}")));
        Assert.Throws<ContractException>(() => Envelope.Parse(Json("[1]")));
        var envelope = Envelope.Parse(Json($"{{\"id\":\"{TabId}\",\"action\":\"snapshot\",\"extra\":true}}"));
        Assert.Equal("snapshot", envelope.Action);
        Assert.Equal("1", Envelope.TryReadId(Json("{\"id\":\"1\"}")));
    }

    [Fact]
    public void Json_do_protocolo_usa_camelCase_enums_em_maiusculas_e_datas_utc()
    {
        var dto = new PaymentDto(Guid.Parse(TabId), PaymentMethod.Card, 1300, new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        Assert.Equal($"{{\"id\":\"{TabId}\",\"method\":\"CARD\",\"amountCents\":1300,\"createdAt\":\"2026-01-01T12:00:00.000Z\"}}",
            MesaCertaJson.Serialize(dto));
    }
}
