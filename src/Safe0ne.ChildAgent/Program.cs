using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Safe0ne.ChildAgent;
using Safe0ne.ChildAgent.ChildUx;
using Safe0ne.ChildAgent.Requests;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Allow this to run as a Windows Service when installed.
// During local dev it will run as a console app.
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Safe0ne Child Agent";
});

builder.Services.AddHttpClient("ControlPlane", client =>
{
    client.BaseAddress = new Uri("http://127.0.0.1:8765/");
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddHostedService<HeartbeatWorker>();

// K8: Child-initiated requests (offline outbox + retry)
builder.Services.AddSingleton<AccessRequestQueue>();

// K7: Child-facing UX (Today view + explainable block screens), served locally.
builder.Services.AddSingleton<ChildStateStore>();
builder.Services.AddSingleton<ChildUxServer>();
builder.Services.AddHostedService<ChildUxHostedService>();

builder.Logging.AddConsole();

var host = builder.Build();
host.Run();
