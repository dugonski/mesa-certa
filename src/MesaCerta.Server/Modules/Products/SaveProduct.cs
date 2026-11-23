using MesaCerta.Server.Data;
using MesaCerta.Server.Domain;
using MesaCerta.Server.Modules.Audit;
using MesaCerta.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace MesaCerta.Server.Modules.Products;

public static class SaveProduct
{
    /// <summary>Editar o catálogo não altera vendas anteriores: o item guarda nome e preço do lançamento.</summary>
    public static async Task<object> HandleAsync(CommandContext context)
    {
        var db = context.Db;
        var data = context.DataAs<SaveProductRequest>();
        Product product;
        if (data.Id is { } id)
        {
            product = await db.Products.FirstOrDefaultAsync(p => p.Id == id)
                ?? throw new AppException(ErrorCodes.NotFound, "Registro não encontrado.");
        }
        else
        {
            product = new Product { Id = Guid.NewGuid(), CreatedAt = Clock.Now() };
            db.Products.Add(product);
        }
        product.Name = data.Name;
        product.Category = data.Category;
        product.PriceCents = data.PriceCents;
        product.SaleType = data.SaleType;
        product.Active = data.Active;

        AuditTrail.Add(db, context.User, context.Terminal, data.Id is null ? "PRODUCT_CREATED" : "PRODUCT_UPDATED",
            product.Id.ToString(), new
            {
                name = data.Name,
                category = data.Category,
                priceCents = data.PriceCents,
                saleType = data.SaleType,
                active = data.Active,
            });
        await db.SaveChangesAsync();
        return product.ToDto();
    }
}
