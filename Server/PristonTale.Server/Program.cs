using PristonTale.Server.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.AddPristonServer();

var app = builder.Build();
app.Run();
