# MesaCerta — aplicação desktop

Protótipo funcional em **C# / .NET 8**, com cliente desktop **Avalonia**, servidor **TCP** próprio, mensagens JSON e **EF Core + MySQL 8**. Interface clara em português. Sem envio à SEFAZ, cobrança financeira ou integração com balança.

## Estrutura

```text
src/
  MesaCerta.Shared/     Contratos, comandos, permissões, cálculos, protocolo TCP e cliente TCP
  MesaCerta.Server/
    Config/             .env e configuração validada
    Data/               EF Core, transações, migrações e mapeamento para DTOs
    Domain/             Regras de estado, peso, permissões e totais
    Modules/            Autenticação, mesas, fichas, produtos, usuários, pagamentos e auditoria
    Transport/          Servidor e roteamento TCP
    Fiscal/             Geração do XML
    Maintenance/        Seed, limpeza de fichas e preparo de acesso ao MySQL
  MesaCerta.Desktop/
    Services/           Ponte com o servidor, preferências e salvamento de arquivos
    ViewModels/         Estado da aplicação, telas e janelas de operação (MVVM)
    Views/              Telas Avalonia (XAML) e tema visual
database/migrations/    Migrações SQL versionadas para MySQL
scripts/                Publicação dos executáveis
tests/
  MesaCerta.Tests/              Unitários: regras, contratos, protocolo, XML e configuração
  MesaCerta.IntegrationTests/   MySQL real com três terminais TCP
  MesaCerta.UiTests/            Interface Avalonia headless contra servidor de fixtures
```

Responsabilidades e manutenção dos registros: [Arquitetura](docs/ARCHITECTURE.md).

## Preparar o banco no Windows

Pré-requisitos: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) e MySQL 8 instalado e em execução. O aplicativo não instala nem altera o serviço do Windows.

1. Crie um banco dedicado `restaurante` e um usuário com acesso somente a esse banco, usando o MySQL Workbench. Para as migrações, o usuário precisa de permissões DDL nesse banco. Opcionalmente, `dotnet run --project src/MesaCerta.Server -- prepare-db-access` gera `artifacts/preparar-mysql.sql` com um usuário e senha aleatórios e atualiza o `.env` (nada é executado no MySQL).
2. Copie `.env.example` para `.env`, se ainda não existir.
3. Configure `DATABASE_URL` e as três senhas iniciais no `.env`. Caracteres especiais da senha dentro da URL precisam estar codificados (percent-encoding). O `.env` é ignorado pelo Git; não publique credenciais.
4. Execute na raiz:

```powershell
dotnet run --project src/MesaCerta.Server -- migrate
dotnet run --project src/MesaCerta.Server -- seed
```

`migrate` aplica os scripts de `database/migrations` uma única vez. Bancos já criados pela versão anterior (Prisma) são reconhecidos pela tabela `_prisma_migrations` e mantidos como estão: o esquema e os dados são os mesmos.

O seed cria `admin`, `atendente` e `caixa`, com as senhas definidas no `.env`. Não troca senhas existentes. As bebidas de demonstração são criadas somente quando não existe um produto com o mesmo nome. A migração cadastra **Buffet regular** e **Buffet especial carnes** sem preço e inativos: aparecem no pedido com **Definir preço**. O administrador pode selecionar o buffet e clicar em **Definir preço**, ou usar **Produtos → Editar**. Nenhum preço de buffet é presumido.

## Executar

No computador servidor, deixe este comando em execução:

```powershell
dotnet run --project src/MesaCerta.Server
```

Em outro terminal, inicie a aplicação:

```powershell
dotnet run --project src/MesaCerta.Desktop
```

Na tela de login, informe usuário, senha e um nome para identificar o terminal. Em **Servidor**, configure o endereço e a porta (5050 por padrão); esses dados ficam em `%AppData%\MesaCerta\connection.json`. O servidor é um processo separado; o aplicativo desktop não inicia MySQL ou backend automaticamente.

### Dois ou mais terminais

No servidor, configure `SERVER_HOST` com o IP da interface de rede local (ou `0.0.0.0`) e reinicie o backend. Configure o mesmo IP e porta na tela de login dos clientes. Libere a porta TCP escolhida no firewall somente para a rede local desejada. O MySQL pode continuar restrito a `127.0.0.1`: apenas o servidor acessa o banco.

Esta versão usa TCP sem TLS, apropriado apenas para demonstração em rede confiável. TLS/VPN é necessário antes de expor a comunicação fora desse ambiente. A interface avisa quando a conexão cai e exige novo login; não faz lançamentos offline nem reenvia pagamentos automaticamente.

## Fluxo e acesso

1. Atendente, caixa ou administrador seleciona uma mesa na grade. Uma mesa livre abre uma nova ficha; uma ocupada mostra o pedido existente. São 30 mesas inicialmente; o administrador pode alterar a quantidade em **Gerenciar mesas** (1 a 1.000). Reduzir desativa os números maiores sem apagar seu histórico; contas abertas impedem a redução.
2. Selecione a bebida ou o buffet, informe a quantidade ou **quantidade em kg** (ex.: 0,350), e clique em **Adicionar**. Selecionar o produto não lança o item. O preço do buffet é cadastrado por kg. Peso, quantidade e observação são redefinidos após o lançamento. **Editar** altera quantidade/peso e observação; **Remover** exclui o item após confirmação. Atendente e caixa podem usar essas ações somente antes do pagamento.
3. Outros terminais autenticados recebem um aviso de atualização e consultam o estado autorizado no servidor.
4. Caixa ou administrador acessa **Caixa** ou **Ir ao caixa** no pedido. Na tela separada de pagamento, confere a conta, clica em **Pagar** e escolhe **Pix** ou **cartão**.
5. O servidor registra o pagamento e salva o XML de pré-autorização na mesma transação. A mesa fica **paga** e não recebe novos itens.
6. **Salvar XML** exporta o documento. **Encerrar ficha** libera a mesa e mantém o atendimento no histórico. Reabrir a mesa cria outra ficha.

Fichas antigas sem mesa associada aparecem em **Fichas anteriores** quando ainda estão abertas. A grade distingue livres (verde), ocupadas (azul) e pagas (amarelo), sempre com legenda escrita.

| Perfil | Operações |
|---|---|
| Administrador | Todas, incluindo produtos, criação de usuários e consulta de atividades |
| Atendente | Consultar mesas, abrir fichas, adicionar, editar e remover itens antes do pagamento |
| Caixa | Operações do atendente, pagamento, XML, encerramento e histórico |

O XML segue a estrutura de pré-autorização da **NFC-e 4.00**, modelo 65, em ambiente de homologação (`tpAmb=2`). Ele inclui chave de acesso, identificação, emitente, itens, grupos tributários provisórios, totais, transporte e pagamento. Ainda não contém assinatura digital, protocolo, autorização, QR Code/CSC ou comunicação com a SEFAZ. CNPJ, IE, endereço, NCM, CFOP e tributação precisam ser configurados e validados antes da futura integração fiscal. O protótipo também ainda não processa Pix ou cartão com um adquirente.

## Consistência e segurança

- Valores monetários em centavos inteiros; total calculado exclusivamente no servidor.
- Buffet: entrada em kg com até três casas decimais e armazenamento em gramas inteiras, preço por kg, arredondamento por item para o centavo mais próximo (meio centavo para cima). Exemplo: 0,250 kg × R$ 65,90/kg = R$ 16,48. O peso e o preço aplicado são preservados no item e no XML demonstrativo. Não há leitura de balança.
- Edição e remoção validam o vínculo do item com a ficha, as permissões e a versão; auditoria registra as alterações. Contas pagas e encerradas não podem ser editadas.
- Nome e preço do produto copiados para o item no momento do lançamento. Editar o catálogo não altera vendas anteriores.
- Alterações da ficha usam versão e transação `Serializable`, com repetição automática de deadlocks. Ao detectar concorrência, a interface exige nova conferência.
- Abertura e reserva da mesa ocorrem na mesma transação. Dois terminais não podem criar dois atendimentos ativos para a mesma mesa. O fechamento libera a reserva atomicamente.
- Cada comando de escrita tem um UUID e recibo persistente na mesma transação; repetir o mesmo comando não duplica a operação.
- Um pagamento por ficha, protegido por índice único. Pagamento, XML, estado e auditoria são gravados juntos.
- Senhas com bcrypt (compatível com os hashes existentes), sessões de 12 horas com tokens armazenados como hash e permissões verificadas no servidor.
- Auditoria de operações concluídas com usuário, terminal, ação, entidade, horário e detalhes, sem senha/token. Não equivale a um log externo imutável.
- O token da sessão fica apenas no cliente TCP; as telas só recebem resultados. O desktop aceita somente as operações previstas para a interface.
- JSON delimitado por nova linha, limite de 4 MB por mensagem, fila limitada por conexão e tratamento de fragmentação UTF-8. Eventos não enviam dados da venda sem autenticação.

## Testar e gerar executáveis

```powershell
dotnet test tests/MesaCerta.Tests
dotnet test tests/MesaCerta.IntegrationTests
dotnet test tests/MesaCerta.UiTests
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
```

`MesaCerta.Tests` cobre regras e protocolo sem banco. A integração exige MySQL e usa `DATABASE_URL` do `.env` (ou `MESACERTA_TEST_DATABASE_URL`); aplica as migrações pendentes, cria registros identificados exclusivamente para o teste e os remove ao terminar. Sem banco configurado, o teste é ignorado. Ela verifica clientes TCP distintos, permissões, idempotência, concorrência, preço preservado, XML e encerramento. O teste de interface abre as telas Avalonia sem janela visível contra um servidor TCP de fixtures em memória; ele não comprova persistência MySQL. Defina `MESACERTA_SCREENSHOTS` para salvar as capturas de tela.

O script de publicação gera `release\desktop\MesaCerta.exe` e `release\server\MesaCerta.Server.exe`, autocontidos (não exigem .NET instalado). São pacotes de demonstração sem assinatura de código. Copie o `.env` para a pasta do servidor antes de executá-lo; os subcomandos `migrate`, `seed` e `reset-orders` funcionam igualmente no executável publicado.

## Manutenção

`MesaCerta.Server reset-orders --confirm-reset-fichas` limpa fichas com o servidor parado, depois de gravar e conferir um backup em `backups/`. Detalhes em [Arquitetura](docs/ARCHITECTURE.md#manutenção-dos-registros).

## Próximas decisões

Fora desta versão: cancelamento de fichas, descontos, taxa de serviço, pagamento dividido, estorno, troca/desativação de usuários, paginação além dos últimos 100 registros no histórico/atividades, recuperação automática após queda, TLS, backups operacionais, atualização automática, fiscal e balança. Essas regras precisam ser definidas antes da implementação.

Referências técnicas: [Avalonia](https://docs.avaloniaui.net/), [EF Core — transações](https://learn.microsoft.com/ef/core/saving/transactions), [Pomelo MySQL](https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql).
