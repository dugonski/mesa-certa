using MesaCerta.Server.Data;
using MesaCerta.Server.Domain;
using MesaCerta.Server.Fiscal;
using MesaCerta.Server.Modules.Audit;
using MesaCerta.Shared;
using MesaCerta.Shared.Contracts;

namespace MesaCerta.Server.Modules.Payments;

public static class PayTab
{
    /// <summary>Pagamento, XML, estado e auditoria são gravados juntos.</summary>
    public static void Apply(CommandContext context, Tab tab, PayTabRequest data)
    {
        if (tab.Items.Count == 0)
            throw new AppException(ErrorCodes.EmptyTab, "Adicione produtos antes de registrar o pagamento.");
        var items = tab.Items.OrderBy(item => item.CreatedAt).ToList();
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            TabId = tab.Id,
            Method = data.Method,
            AmountCents = TabRules.TotalCents(items),
            RecordedBy = context.User.Id,
            CreatedAt = Clock.Now(),
        };
        payment.Xml = NfceXml.Build(
            new NfceTab(tab.Number, tab.Label, items.Select(item => new NfceItem(
                item.ProductId.ToString(), item.ProductName, item.SaleType, item.Quantity,
                item.WeightGrams, item.UnitPriceCents, item.Note)).ToList()),
            new NfcePayment(payment.CreatedAt, payment.Method, payment.AmountCents),
            context.Fiscal);
        context.Db.Payments.Add(payment);
        tab.Status = TabStatus.Paid;
        AuditTrail.Add(context.Db, context.User, context.Terminal, "PAYMENT_SIMULATED", tab.Id.ToString(), new
        {
            method = data.Method,
            amountCents = payment.AmountCents,
        });
    }
}
