using MesaCerta.Shared;

namespace MesaCerta.Tests;

public class CatalogTests
{
    [Theory]
    [InlineData("0.350", 350)]
    [InlineData("0,350", 350)]
    [InlineData("1", 1000)]
    [InlineData("0.001", 1)]
    [InlineData(".5", 500)]
    [InlineData("100", 100000)]
    public void Quantidade_em_kg_converte_exatamente_para_gramas(string value, int expected) =>
        Assert.Equal(expected, Catalog.KgToGrams(value));

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("100.001")]
    [InlineData("0.0005")]
    [InlineData("NaN")]
    [InlineData("")]
    [InlineData("99999999999999999999")]
    [InlineData("١")]
    public void Quantidade_em_kg_rejeita_precisao_invalida(string value) =>
        Assert.Null(Catalog.KgToGrams(value));

    [Fact]
    public void Catalogo_inclui_buffet_aguardando_preco_e_oculta_produtos_desativados_com_preco()
    {
        Assert.True(Catalog.InCatalog(SaleType.Weight, 0, active: false));
        Assert.False(Catalog.InCatalog(SaleType.Weight, 5000, active: false));
        Assert.True(Catalog.InCatalog(SaleType.Unit, 500, active: true));
    }

    [Fact]
    public void Peso_e_formatado_em_pt_BR() => Assert.Equal("0,350", Catalog.FormatKg(350));
}
