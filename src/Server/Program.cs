using SunsetMP.Server;
using SunsetMP.Server.Library;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<LibraryOptions>(builder.Configuration.GetSection("Library"));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));

builder.Services.AddSingleton<MetadataCache>();
builder.Services.AddSingleton<MusicLibrary>();
builder.Services.AddHostedService<LibraryHostedService>();

var app = builder.Build();

app.UseMiddleware<TokenAuthMiddleware>();

app.MapGet("/", () => "SunsetMP Server is running");
app.MapApi();

app.Run();
