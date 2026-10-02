using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ElectronicsAI.Api;
using ElectronicsAI.Design;
using ElectronicsAI.Simulation;
using ElectronicsAI.Validation;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ElectronicsAI.Tests;

public sealed class LocalProposerFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var existing = services.Where(service => service.ServiceType == typeof(IRequirementProposer)).ToList();
            foreach (var service in existing)
            {
                services.Remove(service);
            }

            services.AddSingleton<IRequirementProposer, LocalRequirementProposer>();
        });
    }
}

public class AnalyzeEndpointTests : IClassFixture<LocalProposerFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client;

    public AnalyzeEndpointTests(LocalProposerFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Analyze_returns_the_supply_graph()
    {
        var response = await _client.PostAsJsonAsync("/circuits/analyze", new AnalyzeRequest(12, 5, 0.2, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AnalysisResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Contains(body.Circuit.Components, component => component.Attributes.GetValueOrDefault("partNumber") == "LM7805");
        Assert.Contains("lm7805", body.Netlist, StringComparison.Ordinal);

        if (!NgspiceRunner.IsAvailable())
        {
            Assert.False(body.Simulation.Available);
            Assert.Null(body.Simulation.OperatingPoint);
            Assert.All(body.Checks, check => Assert.Equal(CheckStatus.Unavailable, check.Result));
            Assert.Contains("ngspice", body.Simulation.Reason, StringComparison.OrdinalIgnoreCase);
            return;
        }

        Assert.NotNull(body.Simulation.OperatingPoint);
        Assert.InRange(body.Simulation.OperatingPoint.OutputVolts, 4.9, 5.05);
        Assert.Contains(body.Checks, check => check.Name == "Regulator dissipation" && check.Result == CheckStatus.Warning);
    }

    [Fact]
    public async Task Home_page_is_served()
    {
        var response = await _client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("ElectronicsAI", html);
        Assert.Contains("/circuits/sketch", html);
        Assert.Contains("id=\"schematic\"", html);
        Assert.DoesNotContain("12 V to 5 V supply", html);
    }

    [Fact]
    public async Task Describe_builds_a_counter_from_a_sentence()
    {
        var response = await _client.PostAsJsonAsync(
            "/circuits/describe",
            new DescribeRequest("counter from 1 to 8"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AnalysisResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Contains(body.Circuit.Components, component => component.Kind == "Counter");
        Assert.Equal(8, body.Circuit.Components.Count(component => component.Kind == "Led"));
        Assert.Contains(body.Checks, check => check.Name == "Count sequence" && check.Result == CheckStatus.Pass);
    }

    [Fact]
    public async Task Describe_builds_the_supply_from_a_sentence()
    {
        var response = await _client.PostAsJsonAsync(
            "/circuits/describe",
            new DescribeRequest("12 V in, 5 V for a sensor board"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AnalysisResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Contains(body.Circuit.Components, component => component.Attributes.GetValueOrDefault("partNumber") == "LM7805");
    }

    [Fact]
    public async Task Describe_refuses_a_buck_converter()
    {
        var response = await _client.PostAsJsonAsync(
            "/circuits/describe",
            new DescribeRequest("Design a buck converter from 12 V to 5 V."));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Contains("does not build", body.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Analyze_rejects_an_unsupported_requirement()
    {
        var response = await _client.PostAsJsonAsync("/circuits/analyze", new AnalyzeRequest(9, 3.3, 1, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Contains("divider", body.Error, StringComparison.OrdinalIgnoreCase);
    }
}
