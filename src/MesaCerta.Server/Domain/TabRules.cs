using MesaCerta.Server.Data;
using MesaCerta.Shared;
using MesaCerta.Shared.Contracts;

namespace MesaCerta.Server.Domain;

/// <summary>Regras puras de estado, peso, permissões e totais.</summary>
public static class TabRules
{
    public const long MaxTabCents = 2_000_000_000;

    public static void RequirePermission(Role role, string permission)
    {
        if (!Permissions.Can(role, permission))
            throw new AppException(ErrorCodes.Forbidden, "Seu perfil não tem permissão para esta operação.");
    }

    public static int TotalCents(IEnumerable<IPricedItem> items)
    {
        var total = Money.SumItems(items);
        if (total > MaxTabCents)
            throw new AppException(ErrorCodes.Limit, "O valor da ficha excede o limite permitido.");
        return (int)total;
    }

    public static void AssertTab(Tab? tab, int version, TabStatus status = TabStatus.Open) =>
        AssertTab(tab is null ? null : (tab.Version, tab.Status), version, status);

    public static void AssertTab((int Version, TabStatus Status)? tab, int version, TabStatus status = TabStatus.Open)
    {
        if (tab is null) throw new AppException(ErrorCodes.NotFound, "Ficha não encontrada.");
        if (tab.Value.Version != version)
            throw new AppException(ErrorCodes.Conflict, "A ficha foi atualizada em outro terminal. Confira os itens e tente novamente.");
        if (tab.Value.Status != status)
            throw new AppException(ErrorCodes.InvalidState, "Esta operação não está disponível no estado atual da ficha.");
    }

    public static (int Quantity, int? WeightGrams) ItemMeasures(SaleType saleType, int quantity, int? weightGrams)
    {
        if (saleType == SaleType.Weight)
        {
            if (weightGrams is not { } grams || grams < 1 || grams > Catalog.MaxWeightGrams || quantity != 1)
                throw new AppException(ErrorCodes.Validation, "Informe o peso do buffet. Cada lançamento corresponde a uma pesagem.");
            return (1, grams);
        }
        if (weightGrams is not null)
            throw new AppException(ErrorCodes.Validation, "Bebidas são vendidas por quantidade, não por peso.");
        return (quantity, null);
    }
}
