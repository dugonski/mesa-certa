using MesaCerta.Server.Domain;
using MesaCerta.Shared;
using MesaCerta.Shared.Contracts;

namespace MesaCerta.Tests;

public class DomainTests
{
    [Fact]
    public void Buffet_usa_gramas_inteiras_e_arredonda_cada_item_para_centavos()
    {
        Assert.Equal(1648, TabRules.TotalCents([new PricedItem(SaleType.Weight, 1, 250, 6590)]));
        Assert.Equal(2194, TabRules.TotalCents([new PricedItem(SaleType.Weight, 1, 333, 6590)]));
        Assert.Equal(ErrorCodes.Validation, Assert.Throws<AppException>(() => TabRules.ItemMeasures(SaleType.Weight, 1, null)).Code);
        Assert.Equal(ErrorCodes.Validation, Assert.Throws<AppException>(() => TabRules.ItemMeasures(SaleType.Weight, 2, 300)).Code);
        Assert.Equal(ErrorCodes.Validation, Assert.Throws<AppException>(() => TabRules.ItemMeasures(SaleType.Unit, 1, 300)).Code);
        Assert.Equal(ErrorCodes.Validation, Assert.Throws<AppException>(() => TabRules.ItemMeasures(SaleType.Weight, 1, -1)).Code);
        Assert.Equal((1, 350), TabRules.ItemMeasures(SaleType.Weight, 1, 350));
        Assert.Equal((3, (int?)null), TabRules.ItemMeasures(SaleType.Unit, 3, null));
    }

    [Fact]
    public void Totais_sao_calculados_em_centavos_sem_perda_de_precisao()
    {
        Assert.Equal(11170, TabRules.TotalCents([
            new PricedItem(SaleType.Unit, 3, null, 3290),
            new PricedItem(SaleType.Unit, 2, null, 650),
        ]));
        var error = Assert.Throws<AppException>(() => TabRules.TotalCents([new PricedItem(SaleType.Unit, 9_999_999, null, 9_999_999)]));
        Assert.Equal(ErrorCodes.Limit, error.Code);
        Assert.Contains("limite", error.Message);
    }

    [Fact]
    public void Permissoes_sao_verificadas_no_servidor()
    {
        Assert.Equal(ErrorCodes.Forbidden, Assert.Throws<AppException>(() => TabRules.RequirePermission(Role.Attendant, Permissions.TabsPay)).Code);
        Assert.Equal(ErrorCodes.Forbidden, Assert.Throws<AppException>(() => TabRules.RequirePermission(Role.Cashier, Permissions.ProductsWrite)).Code);
        TabRules.RequirePermission(Role.Admin, Permissions.UsersWrite);
        Assert.False(Permissions.Can(null, Permissions.TabsRead));
    }

    [Fact]
    public void Fichas_pagas_e_versoes_desatualizadas_rejeitam_alteracoes()
    {
        Assert.Equal(ErrorCodes.InvalidState, Assert.Throws<AppException>(() => TabRules.AssertTab((2, TabStatus.Paid), 2)).Code);
        Assert.Equal(ErrorCodes.Conflict, Assert.Throws<AppException>(() => TabRules.AssertTab((2, TabStatus.Open), 1)).Code);
        Assert.Equal(ErrorCodes.NotFound, Assert.Throws<AppException>(() => TabRules.AssertTab(((int, TabStatus)?)null, 1)).Code);
        TabRules.AssertTab((2, TabStatus.Paid), 2, TabStatus.Paid);
    }

    [Fact]
    public void Comandos_de_escrita_tem_permissoes_e_acoes_do_desktop()
    {
        Assert.Equal(Permissions.TabsEdit, Commands.MutationPermissions[Commands.TabsRemove]);
        Assert.Contains(Commands.Snapshot, Commands.DesktopActions);
        Assert.DoesNotContain(Commands.Login, Commands.DesktopActions);
        Assert.Equal(10, Commands.DesktopActions.Count);
    }
}
