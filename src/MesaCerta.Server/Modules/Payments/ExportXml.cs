using MesaCerta.Server.Data;
using MesaCerta.Server.Domain;
using MesaCerta.Shared;
using MesaCerta.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace MesaCerta.Server.Modules.Payments;

public static class ExportXml
{
    public static async Task<XmlExportDto> HandleAsync(Database database, User user, ExportXmlRequest data)
    {
        TabRules.RequirePermission(user.Role, Permissions.TabsPay);
        await using var db = database.CreateContext();
        var tab = await db.Tabs.AsNoTracking().Include(t => t.Payment).FirstOrDefaultAsync(t => t.Id == data.TabId);
        if (tab?.Payment is null)
            throw new AppException(ErrorCodes.NotFound, "Esta ficha ainda não possui um XML.");
        return new XmlExportDto(tab.Payment.Xml, $"venda-demo-{tab.Number}.xml");
    }
}
