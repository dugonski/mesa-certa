using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MesaCerta.Desktop.Services;
using MesaCerta.Desktop.ViewModels;
using MesaCerta.Desktop.Views;
using MesaCerta.Server.Transport;
using MesaCerta.Shared;

namespace MesaCerta.UiTests;

public sealed class MemoryFileSaver : IFileSaver
{
    public List<(string Name, string Content)> Saved { get; } = [];

    public Task<bool> SaveTextAsync(string title, string suggestedName, string content)
    {
        Saved.Add((suggestedName, content));
        return Task.FromResult(true);
    }
}

/// <summary>
/// Abre a interface real (sem janela visível) contra um servidor TCP de fixtures e percorre
/// login, mesas, lançamento por quantidade e por peso, edição, pagamento, XML, encerramento,
/// histórico e páginas administrativas. Capturas de tela vão para MESACERTA_SCREENSHOTS.
/// </summary>
public class DesktopFlowTests
{
    private static readonly string ScreenshotDir =
        Environment.GetEnvironmentVariable("MESACERTA_SCREENSHOTS") ?? Path.Combine(AppContext.BaseDirectory, "screenshots");

    private static async Task Settle(Func<bool>? until = null)
    {
        for (var i = 0; i < 200; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            if (until?.Invoke() ?? i > 2) return;
            await Task.Delay(10);
        }
        throw new TimeoutException("A interface não atingiu o estado esperado.");
    }

    private static void Capture(Window window, string name)
    {
        Directory.CreateDirectory(ScreenshotDir);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.CaptureRenderedFrame()?.Save(Path.Combine(ScreenshotDir, $"{name}.png"));
    }

    private static IEnumerable<string> Texts(Window window) =>
        window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "");

    private static async Task<(Window Window, MainViewModel Main, MemoryFileSaver Files, TcpServer Server)> StartAsync()
    {
        var server = new TcpServer(new FixtureService(), TextWriter.Null);
        server.Start("127.0.0.1", 0);
        var files = new MemoryFileSaver();
        var settings = new SettingsStore(Path.Combine(Path.GetTempPath(), $"mesacerta-ui-{Guid.NewGuid():N}.json"));
        var main = new MainViewModel(new RestaurantApi(), settings, files);
        var window = new MainWindow { DataContext = main, Width = 1440, Height = 940 };
        window.Show();
        main.Login.Port = server.Port.ToString();
        main.Login.Host = "127.0.0.1";
        await Settle();
        return (window, main, files, server);
    }

    private static async Task LoginAsync(MainViewModel main, string username)
    {
        main.Login.Username = username;
        main.Login.Password = FixtureService.Password;
        main.Login.Terminal = "Terminal UI";
        await main.Login.SubmitCommand.ExecuteAsync(null);
        await Settle(() => main.IsLoggedIn && !main.Busy);
    }

    [AvaloniaFact]
    public async Task Fluxo_completo_do_administrador_com_mesa_buffet_pagamento_e_historico()
    {
        var (window, main, files, server) = await StartAsync();
        try
        {
            Capture(window, "01-login");
            main.Login.Username = "admin";
            main.Login.Password = "errada";
            main.Login.Terminal = "Terminal UI";
            await main.Login.SubmitCommand.ExecuteAsync(null);
            await Settle(() => !main.Busy);
            Assert.Equal("Usuário ou senha incorretos.", main.Error);
            Assert.False(main.IsLoggedIn);

            await LoginAsync(main, "admin");
            Assert.Equal(["Mesas", "Caixa", "Histórico", "Produtos", "Usuários", "Atividades"], main.Navigation.Select(n => n.Title));
            var map = Assert.IsType<TableMapViewModel>(main.Content);
            Assert.Equal(30, map.Tiles.Count);
            Assert.True(main.ShowManageTables);
            Capture(window, "02-mesas");

            // Abre a mesa 1 e lança duas águas.
            await map.Tiles[0].Command.ExecuteAsync(null);
            await Settle(() => main.Content is OrderWorkspaceViewModel && !main.Busy);
            Assert.Equal("Mesa aberta.", main.Toast);
            Assert.Equal("Mesa 01", main.HeadingTitle);
            var order = Assert.IsType<OrderWorkspaceViewModel>(main.Content);
            order.Cards.Single(c => c.Name == "Água mineral").Command.Execute(null);
            order.Quantity = "2";
            await Settle();
            Assert.Equal("R$ 10,00", order.PreviewText);
            await order.AddCommand.ExecuteAsync(null);
            await Settle(() => order.Lines.Count == 1 && !main.Busy);
            Assert.Equal("1", order.Quantity);

            // Buffet sem preço: o administrador define o preço pela própria tela de pedido.
            order.Cards.Single(c => c.Name == "Buffet regular").Command.Execute(null);
            await Settle();
            Assert.True(order.NeedsPrice);
            Assert.False(order.IsValid);
            Capture(window, "03-buffet-sem-preco");
            order.ConfigureCommand.Execute(null);
            var productDialog = Assert.IsType<ProductDialogViewModel>(main.Modal);
            Assert.True(productDialog.Active);
            productDialog.Price = "65,90";
            await Settle();
            Capture(window, "04-definir-preco");
            await productDialog.SubmitCommand.ExecuteAsync(null);
            await Settle(() => main.Modal is null && !main.Busy && main.Content is OrderWorkspaceViewModel { NeedsPrice: false });

            order = Assert.IsType<OrderWorkspaceViewModel>(main.Content);
            Assert.False(order.NeedsPrice);
            order.Weight = "0,250";
            await Settle();
            Assert.Equal("R$ 16,48", order.PreviewText);
            await order.AddCommand.ExecuteAsync(null);
            await Settle(() => order.Lines.Count == 2 && !main.Busy && order.TotalText == "R$ 26,48");
            Assert.Equal("0,250 kg", order.Lines[1].Quantity);
            Assert.Equal("R$ 26,48", order.TotalText);
            Capture(window, "05-pedido");

            // Edição do peso e remoção da água.
            order.Lines[1].EditCommand.Execute(null);
            var edit = Assert.IsType<EditItemDialogViewModel>(main.Modal);
            Assert.Equal("0,250", edit.Weight);
            edit.Weight = "0.333";
            edit.Note = "Peso corrigido";
            await Settle();
            Capture(window, "06-editar-item");
            await edit.SubmitCommand.ExecuteAsync(null);
            await Settle(() => main.Modal is null && !main.Busy && order.TotalText == "R$ 31,94");
            order = Assert.IsType<OrderWorkspaceViewModel>(main.Content);
            Assert.Equal("R$ 31,94", order.TotalText);
            order.Lines[0].RemoveCommand.Execute(null);
            await Assert.IsType<RemoveItemDialogViewModel>(main.Modal).SubmitCommand.ExecuteAsync(null);
            await Settle(() => main.Modal is null && !main.Busy && order.Lines.Count == 1);
            order = Assert.IsType<OrderWorkspaceViewModel>(main.Content);
            Assert.Single(order.Lines);

            // Caixa: pagamento Pix, XML e encerramento.
            order.GoCashierCommand.Execute(null);
            await Settle();
            var payment = Assert.IsType<PaymentWorkspaceViewModel>(main.Content);
            Assert.Equal("Pagamento · Mesa 01", main.HeadingTitle);
            Assert.True(payment.ShowPay);
            payment.PayCommand.Execute(null);
            var pay = Assert.IsType<PayDialogViewModel>(main.Modal);
            await pay.SubmitCommand.ExecuteAsync(null);
            Assert.Equal("Escolha a forma de pagamento.", main.Error);
            pay.IsPix = true;
            await Settle();
            Capture(window, "07-pagamento");
            await pay.SubmitCommand.ExecuteAsync(null);
            await Settle(() => main.Modal is null && !main.Busy && main.Content is PaymentWorkspaceViewModel { PaidInfo: "Pix" });
            payment = Assert.IsType<PaymentWorkspaceViewModel>(main.Content);
            Assert.Equal("Pix", payment.PaidInfo);
            Assert.True(payment.ShowClose);
            Assert.False(payment.ShowPay);
            Capture(window, "08-paga");

            await payment.ExportCommand.ExecuteAsync(null);
            await Settle(() => !main.Busy);
            var (fileName, xml) = Assert.Single(files.Saved);
            Assert.Equal("venda-demo-1.xml", fileName);
            Assert.Contains("<vNF>21.94</vNF>", xml);
            Assert.Contains("<qCom>0.3330</qCom>", xml);
            Assert.Contains("Peso corrigido", xml);
            Assert.Equal("XML salvo.", main.Toast);

            payment.CloseCommand.Execute(null);
            await Assert.IsType<CloseDialogViewModel>(main.Modal).SubmitCommand.ExecuteAsync(null);
            await Settle(() => main.Modal is null && !main.Busy && main.Data.History.Count == 1);
            Assert.Equal("Ficha encerrada.", main.Toast);

            main.NavigateCommand.Execute(Pages.Tabs);
            await Settle();
            map = Assert.IsType<TableMapViewModel>(main.Content);
            Assert.True(map.Tiles[0].IsFree);

            main.NavigateCommand.Execute(Pages.History);
            await Settle();
            var history = Assert.IsType<TabsListViewModel>(main.Content);
            Assert.Equal("Mesa 01", Assert.Single(history.Cards).Title);
            Assert.Equal("Últimas 1 fichas", history.CountText);
            Capture(window, "09-historico");

            main.NavigateCommand.Execute(Pages.Products);
            await Settle();
            var products = Assert.IsType<ProductsPageViewModel>(main.Content);
            Assert.Contains(products.Rows, r => r.Name == "Buffet regular" && r.Price == "R$ 65,90 / kg");
            Capture(window, "10-produtos");

            main.NewUserCommand.Execute(null);
            var userDialog = Assert.IsType<UserDialogViewModel>(main.Modal);
            userDialog.Name = "Bruna";
            userDialog.Username = "bruna";
            userDialog.Password = "12345678";
            await userDialog.SubmitCommand.ExecuteAsync(null);
            Assert.Equal("Selecione um perfil.", main.Error);
            userDialog.RoleIndex = 2;
            await userDialog.SubmitCommand.ExecuteAsync(null);
            await Settle(() => main.Modal is null && !main.Busy && main.Data.Users.Any(u => u.Username == "bruna"));
            main.NavigateCommand.Execute(Pages.Users);
            await Settle();
            Assert.Contains(Assert.IsType<UsersPageViewModel>(main.Content).Rows, r => r.Username == "bruna" && r.Role == "Caixa");
            Capture(window, "11-usuarios");

            main.NavigateCommand.Execute(Pages.Audit);
            await Settle();
            var audit = Assert.IsType<AuditPageViewModel>(main.Content);
            Assert.Contains(audit.Rows, r => r.Action == "Pagamento registrado");
            Assert.Contains("Atividade", Texts(window));
            Capture(window, "12-atividades");

            await main.LogoutCommand.ExecuteAsync(null);
            await Settle(() => !main.IsLoggedIn && !main.Busy);
        }
        finally
        {
            window.Close();
            await server.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task Atendente_lanca_mas_nao_recebe_e_queda_do_servidor_exige_novo_login()
    {
        var (window, main, _, server) = await StartAsync();
        try
        {
            await LoginAsync(main, "atendente");
            Assert.Equal(["Mesas"], main.Navigation.Select(n => n.Title));
            Assert.False(main.ShowManageTables);
            var map = Assert.IsType<TableMapViewModel>(main.Content);
            await map.Tiles[4].Command.ExecuteAsync(null);
            await Settle(() => main.Content is OrderWorkspaceViewModel && !main.Busy);
            var order = Assert.IsType<OrderWorkspaceViewModel>(main.Content);
            Assert.True(order.CanAdd);
            Assert.False(order.CanPay);
            order.Cards.Single(c => c.Name == "Buffet regular").Command.Execute(null);
            await Settle();
            Assert.True(order.CannotConfigure);
            Capture(window, "13-atendente");

            main.BackCommand.Execute(null);
            await Settle();
            map = Assert.IsType<TableMapViewModel>(main.Content);
            Assert.True(map.Tiles[4].IsOpen);
            Assert.Equal("R$ 0,00", map.Tiles[4].Amount);
            map.Filter = "OPEN";
            await Settle();
            Assert.Single(map.Tiles);

            await server.DisposeAsync();
            await Settle(() => !main.IsLoggedIn);
            Assert.Equal("Conexão interrompida. Entre novamente para consultar os dados atualizados.", main.Error);
            Capture(window, "14-conexao-perdida");
        }
        finally
        {
            window.Close();
            await server.DisposeAsync();
        }
    }
}
