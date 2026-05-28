using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rpc.Core;
using Rpc.SignalR.Server;
using Sample.Contracts;
using Sample.Contracts.Generated;
using Sample.Contracts.Gateways;
using Sample.Server.Api;
using Sample.Server.Api.Components;
using Sample.Server.Api.Gateways;
using Sample.Server.Api.Implementations;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddRpcServer();

builder.Services.AddSingleton<IOrderService, OrderServiceImpl>();

// Application-level ports (Blazor pages injetam Gateway, não IRpcClient diretamente).
// Em deployment MAUI, troca pra DirectTefGateway/DirectPrinterGateway sem alterar páginas.
builder.Services.AddScoped<ICurrentUser>(_ => new DemoCurrentUser("operator-1"));
builder.Services.AddScoped<ITefGateway, RemoteTefGateway>();
builder.Services.AddScoped<IPrinterGateway, RemotePrinterGateway>();

builder.Services.AddSingleton<DispatcherStartup>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DispatcherStartup>());

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapRpcHub();

app.Run();

public partial class Program;
