# Arquitetura do MesaCerta

O projeto é um monólito modular em .NET 8: um servidor TCP, um banco MySQL e vários clientes desktop Avalonia. A migração de JavaScript para C# manteve o protocolo JSON, o modelo de dados, as telas e as regras existentes.

## Compartilhado (`MesaCerta.Shared`)

- `Contracts/Requests.cs` define os formatos de entrada de cada operação e sua validação (equivalente aos esquemas Zod anteriores): campos obrigatórios, inteiros estritos, limites e textos normalizados.
- `Contracts/Dtos.cs` e `Contracts/Protocol.cs` definem as respostas, o envelope da requisição e os códigos de erro.
- `Json/MesaCertaJson.cs` fixa o formato do protocolo: camelCase, enums em maiúsculas (`OPEN`, `PIX`, `WEIGHT`), datas UTC ISO 8601 e números sem conversão de texto.
- `Commands.cs` define as operações de escrita e permissões exigidas; também limita o que o desktop pode solicitar. `Permissions.cs` associa perfis a permissões.
- `Money.cs` e `Catalog.cs`: dinheiro em centavos, peso em gramas inteiras, entrada em kg.
- `Framing.cs` enquadra JSON por nova linha (limite de 4 MB). `Client/ServerConnection.cs` é o cliente TCP usado pelo desktop e pelos testes.

## Servidor (`MesaCerta.Server`)

- `Program.cs` monta configuração, banco, serviço e servidor TCP, e expõe os subcomandos `migrate`, `seed`, `reset-orders` e `prepare-db-access`.
- `Config` lê o `.env` (variáveis de ambiente têm prioridade) e valida a URL MySQL, host e porta sem exibir credenciais em erros.
- `Transport/TcpServer.cs` trata conexões, enquadramento, fila por conexão e respostas. `Transport/Router.cs` associa comandos a contratos e casos de uso.
- `RestaurantService.cs` autentica, valida contratos, autoriza e encaminha operações.
- `Modules/Auth` mantém login, sessão e proteção contra tentativas repetidas.
- `Modules/Tabs`, `Tables`, `Products`, `Users` e `Payments` agrupam operações específicas; `Modules/Snapshot.cs` monta o estado autorizado para cada perfil.
- `Modules/Audit` registra operações dentro da transação da alteração.
- `Data/Database.cs` centraliza isolamento `Serializable` e repetição de conflitos transitórios (deadlock e espera de bloqueio).
- `Modules/CommandExecutor.cs` centraliza recibos de comandos e idempotência. O caso de uso recebe o mesmo contexto transacional; módulos não abrem transações independentes.
- `Data/MesaCertaDbContext.cs` mapeia as mesmas tabelas e colunas do esquema MySQL original. `Data/Migrator.cs` aplica `database/migrations/*.sql` e reconhece bancos migrados pelo Prisma.
- `Domain/TabRules.cs` contém regras puras de estado, peso, permissões e totais.
- `Fiscal/NfceXml.cs` gera o XML atual, sem dependência do transporte ou do banco.

Fluxo de escrita: TCP → contrato → sessão → permissão → recibo/transação → caso de uso → auditoria → recibo → commit → aviso de atualização.

## Desktop (`MesaCerta.Desktop`)

- `ViewModels/MainViewModel.cs` gerencia sessão, snapshot, atualização entre terminais, seleção de ficha, comandos e mensagens. A sequência das consultas impede uma resposta antiga de substituir uma mais recente; uma operação por vez.
- `ViewModels` contém mapa de mesas, pedido, pagamento, listas e as janelas de operação (`DialogViewModels.cs`), incluindo a conferência de versão da ficha.
- `Views` contém as telas XAML; `Styles/Theme.axaml` concentra cores e estilos. `ViewLocator` associa cada view model à sua tela.
- `Services/RestaurantApi.cs` mantém a conexão TCP e o token; `SettingsStore` guarda endereço/porta/terminal; `StorageFileSaver` exporta o XML pelo diálogo nativo.

## Manutenção dos registros

`MesaCerta.Server reset-orders` exige o servidor parado e a opção explícita `--confirm-reset-fichas`. Ele reserva a porta TCP durante a manutenção, cria backup JSON com hash SHA-256 e verifica a gravação antes de apagar qualquer registro.

A limpeza libera as mesas e remove fichas, itens, pagamentos/XMLs, auditoria e recibos relacionados. Usuários, senhas, produtos, preços, quantidade de mesas e registros não relacionados são preservados e conferidos por comparação. O contador das fichas é reiniciado após o commit; o MySQL executa ALTER TABLE fora da transação.

Os arquivos em `backups/` são dados locais, ignorados pelo Git. O backup contém IDs e datas originais e permite recuperar fichas, itens, pagamentos e vínculos das mesas. Para recuperação, com o servidor parado, restaure primeiro fichas, depois itens/pagamentos, depois `activeTabId` das mesas e os registros de auditoria/recibos. Confira o hash e os cadastros referenciados antes de importar. Não sobrescreva novos atendimentos ao restaurar.

## Validação

`MesaCerta.Tests`: regras, arredondamento, contratos, protocolo, XML e configuração. `MesaCerta.IntegrationTests`: transações MySQL com clientes TCP diferentes, permissões, idempotência, concorrência e XML. `MesaCerta.UiTests`: interface Avalonia headless com servidor de fixtures, incluindo navegação, formulários, pesos, pagamento e queda de conexão. Os testes de integração devem preceder a limpeza final para não avançar a numeração zerada.
