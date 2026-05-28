# RpcKit

[![ci](https://github.com/afernandes/Rpc/actions/workflows/ci.yml/badge.svg)](https://github.com/afernandes/Rpc/actions/workflows/ci.yml)
[![NuGet RpcKit.Core](https://img.shields.io/nuget/v/RpcKit.Core?label=RpcKit.Core)](https://www.nuget.org/packages/RpcKit.Core)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

RPC bidirecional tipado para sistemas multi-superfície em .NET — MAUI (Windows/Android), ASP.NET, Blazor Server e agentes de hardware locais (TEF / impressora) — usando **SignalR como transporte único** e uma camada fina de tipagem por cima.

Resolve quatro cenários com **um único mecanismo**:

1. MAUI ↔ Servidor (chamada típica cliente→servidor).
2. Client A ↔ Client B (peer-to-peer com o servidor como relay).
3. Browser (Blazor Server) → pinpad USB no PDV do usuário, com **loop interativo do CliSiTef** (Software Express): cada iteração emite um evento (`TefDisplayMessage`, `TefCollectInput`, `TefMenu`, `TefConfirm`, …) e espera resposta antes de continuar.
4. Browser → impressora térmica em qualquer PDV/cozinha, roteado por *capability*.

---

## Por que um mecanismo só

Os quatro casos são o **mesmo problema arquitetural**: invocar operação tipada num endpoint remoto identificado, com resposta. Diferem só em payload e endereçamento. Construir três stacks (uma para REST, outra para gRPC, outra para socket de impressora) triplica auth, reconexão, observabilidade.

SignalR resolve a parte difícil (transporte, reconexão, scale-out, autenticação ASP.NET, streaming duplex nativo). RpcKit adiciona o que falta: **endereçamento tipado + roteamento por capability + duplex bidirecional para loops interativos**.

A análise comparativa completa dos mecanismos (gRPC, MagicOnion, StreamJsonRpc, WebRTC, MQTT, WebUSB, etc.) e o porquê desta escolha estão em `C:\Users\ander\.claude\plans\eu-tenho-um-sistema-sunny-locket.md`.

---

## Arquitetura

```
                  ┌──────────────────────────────────────────┐
                  │   Sample.Server.Api (ASP.NET + Blazor)   │
                  │   ┌────────────┐    ┌──────────────────┐ │
                  │   │  RpcHub    │←→  │ DispatcherStartup│ │
                  │   │ (SignalR)  │    │  (server impls)  │ │
                  │   └─────┬──────┘    └──────────────────┘ │
                  │         │  routes via DeviceRegistry +    │
                  │         │  PendingDuplexRegistry          │
                  └─────────┼──────────────────────────────────┘
                            │ SignalR over HTTPS / WebSocket
            ┌───────────────┼─────────────────────────────────┐
            │               │                                 │
   ┌────────▼─────┐   ┌─────▼──────┐                  ┌──────▼──────┐
   │ MAUI client  │   │ LocalAgent │                  │ Other MAUI  │
   │  (PDV-01)    │   │  (Worker)  │                  │  (PDV-02)   │
   │              │   │ TefImpl    │                  │             │
   │              │   │ PrinterImpl│                  │             │
   └──────────────┘   └────────────┘                  └─────────────┘
```

Camadas (transport-neutral por baixo, SignalR por cima):

```
Rpc.Contracts      [RpcContract], [RpcMethod], IDuplexCall<TUp, TDown>
       ▲
       │
Rpc.Core           RpcAddress, IRpcInvoker, IRpcClient, DuplexCallChannel,
                   ProxyRegistry, DispatcherRegistry, RpcException
       ▲
       ├────────────────────────────────────────────┐
       │                                            │
Rpc.SignalR.Server                          Rpc.SignalR.Client
RpcHub + IRpcRouter                         SignalRRpcInvoker
DeviceRegistry                              RpcConnectionHost
PendingDuplexRegistry                       (auto-reconnect + handshake)
InProcessRpcInvoker (p/ Blazor)             AgentDuplexSessions
```

---

## Instalação via NuGet

```bash
# Lado cliente (MAUI, Worker Service, WASM)
dotnet add package RpcKit.SignalR.Client

# Lado servidor (ASP.NET / Blazor Server)
dotnet add package RpcKit.SignalR.Server

# Para gerar proxies/dispatchers automaticamente
dotnet add package RpcKit.SourceGen
```

Os 5 pacotes publicados:

| Pacote | Quem instala | O que entrega |
|---|---|---|
| `RpcKit.Contracts` | Quem define interfaces `[RpcContract]` | Atributos + `IDuplexCall` |
| `RpcKit.Core` | Todo mundo (transitiva) | `RpcAddress`, invoker, registry, exceções |
| `RpcKit.SignalR.Server` | API ASP.NET / Blazor Server | `RpcHub`, router, `InProcessRpcInvoker` |
| `RpcKit.SignalR.Client` | MAUI / Worker Service / WASM | `SignalRRpcInvoker`, connection host |
| `RpcKit.SourceGen` | Quem define contratos | Roslyn generator emitindo proxies/dispatchers |

## Quickstart (build local)

Pré-requisitos: .NET 10.0.300 (pino em [`global.json`](global.json)).

```bash
# 1. Compilar tudo
dotnet build Rpc.slnx

# 2. Rodar os testes
dotnet test tests/Rpc.IntegrationTests
# → 7 testes passam, incluindo o loop CliSiTef completo

# 3. Subir servidor + Blazor (janela 1)
dotnet run --project samples/Sample.Server.Api

# 4. Subir agente local (janela 2)
dotnet run --project samples/Sample.LocalAgent

# 5. Abrir o navegador
# http://localhost:5000 → home com as quatro demos
```

Páginas da demo (em [`samples/Sample.Server.Api/Components/Pages`](samples/Sample.Server.Api/Components/Pages)):
- `/orders` — Caso 1: chama `IOrderService` no servidor (`RpcAddress.Server`).
- `/ping` — Caso 2: chama `IPosTerminal` em outro device (`RpcAddress.Device("…")`).
- `/tef` — Caso 3: dispara `ITefService.Charge` em duplex, mostra cada `TefEvent` e coleta respostas do operador.
- `/print` — Caso 4: roteamento por capability (`RpcAddress.Role(Capability.PrinterKitchen, …)`).

---

## Os quatro casos, mapeados ao código

### Caso 1 — Chamada cliente → servidor

```csharp
var orders = await Rpc.For<IOrderService>(RpcAddress.Server)
                      .GetOpenOrdersAsync(ct);
```

O contrato é definido em [`samples/Sample.Contracts/IOrderService.cs`](samples/Sample.Contracts/IOrderService.cs). A implementação do servidor é [`OrderServiceImpl`](samples/Sample.Server.Api/Implementations/OrderServiceImpl.cs), registrada pelo [`DispatcherStartup`](samples/Sample.Server.Api/DispatcherStartup.cs) via `OrderServiceDispatcher.Register(...)`.

### Caso 2 — Client A invoca Client B (relay pelo servidor)

```csharp
var pong = await Rpc.For<IPosTerminal>(RpcAddress.Device("PDV-02"))
                    .PingAsync(ct);
```

O Hub resolve o `ConnectionId` de `PDV-02` no `DeviceRegistry`, repassa o envelope via `Clients.Client(connId).InvokeAsync<InvokeResponse>("HandleInvoke", …)`. O agente registra o handler em [`RpcConnectionHost.HandleInvokeCore`](src/Rpc.SignalR.Client/RpcConnectionHost.cs).

### Caso 3 — Browser → TEF no PDV do usuário (loop CliSiTef)

```csharp
await using var call = Rpc.For<ITefService>(
        RpcAddress.User(currentUserId, Capability.Tef))
    .Charge(new TefRequest(amount, userId, reference), ct);

await foreach (var ev in call.Responses.WithCancellation(ct))
{
    switch (ev)
    {
        case TefDisplayMessage m: ShowMessage(m.Text); break;
        case TefCollectInput ask:
            await call.SendAsync(new TefResponse(await PromptUser(ask)));
            break;
        case TefMenu menu:
            await call.SendAsync(new TefResponse(await PickMenu(menu)));
            break;
        case TefConfirm c:
            await call.SendAsync(new TefResponse(await Confirm(c) ? "S" : "N"));
            break;
        case TefCompleted ok: return ok;
        case TefError err: throw new TefException(err);
    }
}
```

A implementação do agente está em [`TefServiceImpl`](samples/Sample.LocalAgent/Implementations/TefServiceImpl.cs) e produz a sequência: `Display → CollectInput → Menu → Display → Display → Display → Confirm → Completed`. A modelagem com `IDuplexCall<TefResponse, TefEvent>` significa que **a sessão é o par de canais** — sem `transactionId` correlacionando chamadas avulsas; cancelamento se propaga atomicamente; desconexão do agente derruba a sessão.

A UI Blazor completa do fluxo está em [`Tef.razor`](samples/Sample.Server.Api/Components/Pages/Tef.razor).

### Caso 4 — Impressão por capability

```csharp
await Rpc.For<IPrinterService>(
        RpcAddress.Role(Capability.PrinterKitchen, tenantId))
    .PrintAsync(new PrintJob("Pedido 1", "1x Pizza\n2x Suco"), ct);
```

`RpcAddress.Role(capability, tenantId)` pega qualquer agente daquele tenant com a capability declarada. Útil quando há várias impressoras equivalentes — o router escolhe a primeira disponível.

---

## Concorrência: pinpad exclusivo, fila de impressão e gateway de aplicação

Pinpad é recurso de uma transação por vez. `ITefService.Charge` está marcado com
`[RpcMethod(1, Exclusive = true)]` — o router rejeita a 2ª chamada concorrente para o mesmo
agente com `RpcBusyException` **antes** de chegar no agente. Defesa em profundidade: o
`TefServiceImpl` também tem um `SemaphoreSlim(1, 1)`.

Impressão é sequencial, mas aceita concorrência — o `PrinterServiceImpl` usa
`Channel<T>` bounded(32) + worker FIFO. Caller espera o **seu** job sair pela impressora,
sem bloquear outros callers.

A UI (Razor) **não depende** de `IRpcClient` diretamente. Depende de gateways de aplicação
(`ITefGateway`, `IPrinterGateway`) cuja implementação é escolhida por DI:

| Host | Implementação | Caminho |
|---|---|---|
| MAUI Hybrid (mesma máquina do pinpad) | `DirectTefGateway` (TODO) | CliSiTef direto, sem RPC |
| Blazor Server / WASM / Web App Auto | `RemoteTefGateway` | `IRpcClient.For<ITefService>(RpcAddress.User(...))` |

Documentação completa de concorrência: [`docs/concurrency.html`](docs/concurrency.html).

## Endereçamento (`RpcAddress`)

| Variante | Construtor | Uso |
|---|---|---|
| `ServerAddress` | `RpcAddress.Server` | Chama handler local no servidor (sem hop). |
| `DeviceAddress` | `RpcAddress.Device(deviceId)` | Alvo é um device específico (ex: `PDV-02`). |
| `RoleAddress` | `RpcAddress.Role(capability, tenantId)` | Qualquer device do tenant com a capability. |
| `UserAddress` | `RpcAddress.User(userId, capability)` | Qualquer device do usuário logado com a capability (caso TEF). |

`tenantId` já está no endereçamento mesmo no modo single-tenant atual — quando o sistema virar multi-tenant, basta o `DeviceRegistry` particionar por tenant e o Hub validar `tenant_id` no claim JWT. Zero refactor de contratos.

---

## Adicionando um novo contrato

Proxies e dispatchers são **gerados automaticamente** pelo `Rpc.SourceGen` a partir das interfaces marcadas com `[RpcContract]`. Passos:

**1.** Adicione o gerador no seu assembly de contratos (`csproj`):

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\Rpc.SourceGen\Rpc.SourceGen.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

**2.** Defina a interface — marcadores `[RpcContract]` e `[RpcMethod(N)]`:

```csharp
using Rpc.Contracts;

namespace MyApp.Contracts;

[RpcContract]
public interface IInventoryService
{
    [RpcMethod(1)]
    Task<StockLevel> GetStockAsync(string sku, CancellationToken ct);

    [RpcMethod(2, Exclusive = true)]
    Task<bool> ReserveAsync(string sku, int qty, CancellationToken ct);
}

public sealed record StockLevel(string Sku, int Quantity);
```

**3.** Compile. O gerador emite, em `MyApp.Contracts.Generated`:
- `InventoryServiceProxy : IInventoryService` (caller-side, serializa args, chama `IRpcInvoker`)
- `InventoryServiceDispatcher` com `Register(IDispatcherRegistry, IInventoryService impl)` (server- ou agent-side)

**4.** Registre no host:

```csharp
using MyApp.Contracts.Generated;

// Server-side:
proxies.Register(typeof(IInventoryService),
    static (inv, addr) => new InventoryServiceProxy(inv, addr));
InventoryServiceDispatcher.Register(dispatchers, sp.GetRequiredService<IInventoryService>());
```

**Para inspecionar a saída do gerador**, adicione ao `csproj`:
```xml
<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
<CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)GeneratedFiles</CompilerGeneratedFilesOutputPath>
```
Os arquivos `.g.cs` aparecem em `obj/GeneratedFiles/Rpc.SourceGen/.../*.RpcGenerated.g.cs`.

**Tipos de retorno suportados pelo gerador:**

| Retorno do método | O que gera |
|---|---|
| `Task<T>` / `ValueTask<T>` | `InvokeAsync` → JsonSerializer.Deserialize<T> |
| `Task` / `ValueTask` | `InvokeAsync` → ignora resultado |
| `IAsyncEnumerable<T>` | `OpenStreamAsync` → async iterator que deserializa cada chunk |
| `IDuplexCall<TUp, TDown>` | `OpenDuplexAsync` → `DeferredDuplexCall<TUp, TDown>` |

**Múltiplos parâmetros**: o gerador emite um `record __{Interface}_{Method}_Args(...)` internal pra empacotar args + Json. Caller serializa, dispatcher deserializa.

---

## Layout do repositório

```
Rpc/
├── global.json                 # SDK pinado em 10.0.300
├── Rpc.slnx                    # solution
├── README.md
├── src/
│   ├── Rpc.Contracts/          # atributos + IDuplexCall
│   ├── Rpc.Core/               # RpcAddress, IRpcInvoker, IRpcClient, DuplexCallChannel
│   ├── Rpc.SignalR.Server/     # RpcHub, RpcRouter, DeviceRegistry, InProcessRpcInvoker
│   ├── Rpc.SignalR.Client/     # SignalRRpcInvoker, RpcConnectionHost, agent handlers
│   └── Rpc.SourceGen/          # Roslyn generator — emite proxies + dispatchers
├── samples/
│   ├── Sample.Contracts/       # IOrder, IPos, IPrinter, ITef + proxies + dispatchers
│   ├── Sample.Server.Api/      # ASP.NET + Blazor Server hospedando o Hub
│   ├── Sample.LocalAgent/      # Worker Service simulando TEF + impressora
│   └── (Sample.MauiClient/)    # TODO: bloqueado em workload MAUI .NET 11
├── tests/
│   └── Rpc.IntegrationTests/   # WebApplicationFactory + HubConnection in-memory
└── docs/                       # Documentação HTML
```

---

## Testes

```bash
dotnet test tests/Rpc.IntegrationTests
```

Cobertura atual (8 casos):

| Teste | Cenário |
|---|---|
| `Caller_invokes_server_side_handler_via_RpcAddress_Server` | Caso 1 |
| `Caller_invokes_agent_via_RpcAddress_Device` | Caso 2 |
| `Caller_invokes_agent_via_RpcAddress_Role_for_capability` | Caso 4 |
| `Caller_invokes_agent_via_RpcAddress_User_for_user_plus_capability` | Caso 3, fluxo feliz |
| `Tef_caller_cancellation_propagates_to_agent_loop` | Caso 3, cancelamento atômico |
| `Invoke_to_offline_device_throws_RouteNotFound` | Erro de roteamento |
| `Exclusive_duplex_rejects_second_concurrent_call_with_Busy_without_hitting_agent` | `[RpcMethod(Exclusive=true)]` |

A fixture [`RpcTestFixture`](tests/Rpc.IntegrationTests/RpcTestFixture.cs) hospeda o `Sample.Server.Api` via `WebApplicationFactory` e ergue conexões SignalR em memória (`Server.CreateHandler()`) para caller e agente, evitando portas TCP.

---

## Publicação do agente como serviço

O `Sample.LocalAgent` é configurado para publish **self-contained single-file** (todo o runtime
embutido num único exe — não precisa .NET instalado na máquina de destino).

### Windows

```bash
dotnet publish samples/Sample.LocalAgent -c Release -r win-x64 --self-contained
# saída: samples/Sample.LocalAgent/bin/Release/net10.0/win-x64/publish/RpcKitSampleAgent.exe
```

Instalar como Windows Service:

```powershell
sc.exe create RpcKitSampleAgent binPath= "C:\Apps\RpcKit\RpcKitSampleAgent.exe" start= auto
sc.exe description RpcKitSampleAgent "Sample agent — TEF + impressora via RpcKit"
sc.exe start RpcKitSampleAgent
```

### Linux (systemd)

```bash
dotnet publish samples/Sample.LocalAgent -c Release -r linux-x64 --self-contained
# saída: RpcKitSampleAgent (binário sem extensão)
sudo cp RpcKitSampleAgent /opt/rpckit/
```

`/etc/systemd/system/rpckit-agent.service`:
```ini
[Unit]
Description=RpcKit Sample Agent
After=network.target

[Service]
Type=notify
ExecStart=/opt/rpckit/RpcKitSampleAgent
Restart=on-failure
Environment=DOTNET_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now rpckit-agent
sudo systemctl status rpckit-agent
```

`AddSystemd()` no `Program.cs` faz integração com `Type=notify` (READY=1, watchdog) automaticamente.

## CI/CD

- [`.github/workflows/ci.yml`](.github/workflows/ci.yml) — restore/build/test em push e PR.
- [`.github/workflows/release.yml`](.github/workflows/release.yml) — em tag `v*.*.*`:
  - Build + test
  - `dotnet pack` dos 5 packages → publica em **nuget.org** (precisa `NUGET_API_KEY` em GitHub Secrets)
  - Publish self-contained do `Sample.LocalAgent` (win-x64 + linux-x64) → zipa → anexa ao **GitHub Release**

Lançar uma versão:
```bash
git tag v0.1.0
git push --tags
# o pipeline release.yml roda automaticamente
```

A versão é derivada do tag via **MinVer** — não tem version hardcoded em csproj.

## Roadmap

- **`Sample.MauiClient`** — esperando workload MAUI estável em .NET 10 ou migração geral pra .NET 11.
- **Modo LAN isolada** — implementação alternativa de `IRpcInvoker` (`LanRpcInvoker`) com SignalR self-hosted num nó mestre da LAN + descoberta via mDNS. Contratos não mudam.
- **Multi-tenant** — `RpcAddress` já carrega `tenantId`; falta o `DeviceRegistry` particionar e o Hub validar JWT.
- **Scale-out cloud** — adicionar Redis backplane no SignalR e particionar `PendingDuplexRegistry`.
- **Streaming server→client** para clientes remotos (hoje suporta apenas para `Address.Server`).

---

## Documentação

Site estático completo em [`docs/index.html`](docs/index.html) — abrir direto no navegador.
