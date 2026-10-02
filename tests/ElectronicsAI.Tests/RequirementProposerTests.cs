using ElectronicsAI.Design;
using ElectronicsAI.Domain;

namespace ElectronicsAI.Tests;

public class RequirementProposerTests
{
    private readonly LocalRequirementProposer _proposer = new();

    [Fact]
    public async Task Sensor_board_sentence_becomes_the_5_v_supply()
    {
        var proposal = await _proposer.ProposeAsync("12 V in, 5 V for a sensor board", CancellationToken.None);

        var supported = Assert.IsType<SupportedRequirement>(proposal);
        Assert.Equal(12, supported.Requirement.InputVolts);
        Assert.Equal(5, supported.Requirement.OutputVolts);
        Assert.Equal(0.2, supported.Requirement.LoadCurrentAmps);
    }

    [Fact]
    public async Task Divider_sentence_keeps_a_negligible_load()
    {
        var proposal = await _proposer.ProposeAsync("Divide 12 V down to about 3.3 V for a logic pin.", CancellationToken.None);

        var supported = Assert.IsType<SupportedRequirement>(proposal);
        Assert.Equal(3.3, supported.Requirement.OutputVolts);
        Assert.Equal(0, supported.Requirement.LoadCurrentAmps);
    }

    [Fact]
    public async Task Counter_sentence_becomes_eight_steps()
    {
        var proposal = await _proposer.ProposeAsync("counter from 1 to 8", CancellationToken.None);

        var supported = Assert.IsType<SupportedRequirement>(proposal);
        Assert.Equal(CircuitFamily.Counter, supported.Requirement.Family);
        Assert.Equal(8, supported.Requirement.Steps);
        Assert.Equal(5, supported.Requirement.InputVolts);
        Assert.Equal(0.01, supported.Requirement.LoadCurrentAmps);
    }

    [Fact]
    public async Task Counter_with_a_different_count_is_refused()
    {
        var proposal = await _proposer.ProposeAsync("counter from 1 to 4", CancellationToken.None);

        var refused = Assert.IsType<RefusedRequirement>(proposal);
        Assert.Contains("1 to 8", refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Buck_converter_is_refused()
    {
        var proposal = await _proposer.ProposeAsync("Design a buck converter from 12 V to 5 V.", CancellationToken.None);

        var refused = Assert.IsType<RefusedRequirement>(proposal);
        Assert.Contains("does not build", refused.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Model_reply_parser_accepts_a_supported_circuit()
    {
        var proposal = ModelReplyParser.Parse(
            "{\"supported\":true,\"inputVolts\":12,\"outputVolts\":5,\"loadCurrentAmps\":0.2}",
            "12 V in, 5 V for a sensor board");

        var supported = Assert.IsType<SupportedRequirement>(proposal);
        Assert.Equal(0.2, supported.Requirement.LoadCurrentAmps);
    }

    [Fact]
    public void Model_reply_parser_accepts_json_wrapped_in_a_fence()
    {
        var proposal = ModelReplyParser.Parse(
            "```json\n{\"supported\":true,\"inputVolts\":12,\"outputVolts\":5,\"loadCurrentAmps\":0.2}\n```",
            "12 V in, 5 V for a sensor board");

        Assert.IsType<SupportedRequirement>(proposal);
    }

    [Fact]
    public void Model_reply_parser_keeps_a_refusal()
    {
        var proposal = ModelReplyParser.Parse(
            """{"supported":false,"reason":"A buck converter is outside this version."}""",
            "buck");

        var refused = Assert.IsType<RefusedRequirement>(proposal);
        Assert.Contains("buck", refused.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Model_reply_parser_accepts_a_counter()
    {
        var proposal = ModelReplyParser.Parse(
            "{\"supported\":true,\"family\":\"counter\",\"steps\":8,\"inputVolts\":5,\"forwardVolts\":2,\"loadCurrentAmps\":0.01,\"clockHertz\":1}",
            "counter from 1 to 8");

        var supported = Assert.IsType<SupportedRequirement>(proposal);
        Assert.Equal(CircuitFamily.Counter, supported.Requirement.Family);
        Assert.Equal(8, supported.Requirement.Steps);
    }
}
