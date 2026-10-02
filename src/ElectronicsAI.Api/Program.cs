using System.Text.Json;
using System.Text.Json.Serialization;
using ElectronicsAI.Api;
using ElectronicsAI.Design;
using ElectronicsAI.Domain;
using ElectronicsAI.Simulation;
using ElectronicsAI.Validation;

var streamJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddSingleton<ICircuitProposer, DeterministicCircuitProposer>();
builder.Services.AddSingleton<LocalRequirementProposer>();
var apiKey = builder.Configuration["LanguageModel:ApiKey"];
if (string.IsNullOrWhiteSpace(apiKey))
{
    apiKey = Environment.GetEnvironmentVariable("ELECTRONICS_AI_API_KEY");
}

builder.Services.AddSingleton(new SavedReplyCatalog(
    Path.Combine(builder.Environment.ContentRootPath, "model-replies.json")));
builder.Services.AddSingleton(new LanguageModelOptions(
    builder.Configuration["LanguageModel:Endpoint"] ?? "https://api.openai.com/v1/chat/completions",
    builder.Configuration["LanguageModel:Model"] ?? "gpt-5",
    apiKey ?? ""));
builder.Services.AddHttpClient<IRequirementProposer, LanguageModelRequirementProposer>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddHttpClient<SchematicSketchService>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
});
builder.Services.AddSingleton<CircuitCompiler>();
builder.Services.AddSingleton<ICircuitCompiler, CircuitEngine>();
builder.Services.AddSingleton<SpiceNetlistWriter>();
builder.Services.AddSingleton<NgspiceRunner>();
builder.Services.AddSingleton<CircuitValidator>();
builder.Services.AddSingleton<CircuitAnalyzer>();
builder.Services.AddSingleton(new SketchRuleCatalog(SketchRules.All));
builder.Services.AddSingleton<SketchSpice>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/circuits/describe", async (
    DescribeRequest request,
    IRequirementProposer proposer,
    CircuitAnalyzer analyzer,
    CancellationToken cancellationToken) =>
{
    var proposal = await proposer.ProposeAsync(request.Description, cancellationToken);
    if (proposal is RefusedRequirement refused)
    {
        return Results.BadRequest(new ErrorResponse(refused.Reason));
    }

    var supported = (SupportedRequirement)proposal;
    try
    {
        return Results.Ok(analyzer.Analyze(supported.Requirement));
    }
    catch (UnsupportedRequirementException exception)
    {
        return Results.BadRequest(new ErrorResponse(exception.Message));
    }
});

app.MapPost("/circuits/sketch", async (
    DescribeRequest request,
    SchematicSketchService sketches,
    SketchSpice spice,
    ILoggerFactory loggerFactory,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("Sketch");
    var gate = new SemaphoreSlim(1, 1);
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";

    async Task Say(string text)
    {
        var line = text.Replace('\r', ' ').Replace('\n', ' ');
        logger.LogInformation("{SketchLog}", line);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await context.Response.WriteAsync(
                $"data: {JsonSerializer.Serialize(new { log = line }, streamJson)}\n\n",
                cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    if (string.IsNullOrWhiteSpace(request.Description))
    {
        await Say("Describe the circuit you want drawn.");
        return;
    }

    await Say($"Request: {request.Description}");
    try
    {
        var sketch = await sketches.SketchAsync(request.Description, cancellationToken, Say);
        var simulation = spice.Simulate(sketch, request.Description);
        if (simulation.Netlist is not null)
        {
            await Say("Compiled a SPICE netlist.");
            await Say(simulation.Available
                ? "ngspice returned an operating point."
                : simulation.Reason ?? "ngspice did not return an operating point.");
        }
        else if (simulation.Reason is not null)
        {
            await Say(simulation.Reason);
        }

        await context.Response.WriteAsync(
            $"data: {JsonSerializer.Serialize(new { sketch, simulation }, streamJson)}\n\n",
            cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);
    }
    catch (Exception exception)
    {
        var detail = exception is TaskCanceledException
            ? "The model did not answer before the request timed out."
            : exception.Message;
        logger.LogError(exception, "Sketch failed");
        await Say(detail);
        await context.Response.WriteAsync(
            $"data: {JsonSerializer.Serialize(new { error = detail }, streamJson)}\n\n",
            cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);
    }
});

app.MapPost("/circuits/analyze", (AnalyzeRequest request, CircuitAnalyzer analyzer) =>
{
    try
    {
        var result = analyzer.Analyze(ToRequirement(request));
        return Results.Ok(result);
    }
    catch (UnsupportedRequirementException exception)
    {
        return Results.BadRequest(new ErrorResponse(exception.Message));
    }
});

app.Run();

static CircuitRequirement ToRequirement(AnalyzeRequest request)
{
    if (string.Equals(request.Family, "counter", StringComparison.OrdinalIgnoreCase))
    {
        return new CircuitRequirement(
            request.InputVolts <= 0 ? 5 : request.InputVolts,
            request.ForwardVolts <= 0 ? 2 : request.ForwardVolts,
            request.LoadCurrentAmps <= 0 ? 0.01 : request.LoadCurrentAmps,
            request.Description,
            CircuitFamily.Counter,
            request.Steps == 0 ? 8 : request.Steps,
            request.ClockHertz <= 0 ? 1 : request.ClockHertz,
            request.ForwardVolts <= 0 ? 2 : request.ForwardVolts);
    }

    return new CircuitRequirement(
        request.InputVolts,
        request.OutputVolts,
        request.LoadCurrentAmps,
        request.Description);
}

public partial class Program;
