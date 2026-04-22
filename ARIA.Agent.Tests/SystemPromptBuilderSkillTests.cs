using ARIA.Agent.Prompts;
using ARIA.Core.Interfaces;
using ARIA.Core.Models;
using ARIA.Core.Options;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ARIA.Agent.Tests;

public sealed class SystemPromptBuilderSkillTests
{
    [Fact]
    public async Task BuildAsync_IncludesSkillMetadataAndPath_NotFullSkillBody()
    {
        var contextStore = Substitute.For<IContextFileStore>();
        contextStore.ReadAsync(Arg.Any<ContextFile>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string>(new FileNotFoundException()));

        var skillStore = Substitute.For<ISkillStore>();
        skillStore.GetAll().Returns([
            new SkillEntry(
                "Sample Skill",
                "Does sample work.",
                "skills/sample",
                "skills/sample/SKILL.md")
        ]);

        var builder = new SystemPromptBuilder(
            contextStore,
            skillStore,
            Options.Create(new AriaOptions()),
            NullLogger<SystemPromptBuilder>.Instance);

        var prompt = await builder.BuildAsync("session-1");

        prompt.Should().Contain("## Available Skills");
        prompt.Should().Contain("name: Sample Skill");
        prompt.Should().Contain("description: Does sample work.");
        prompt.Should().Contain("path: skills/sample/SKILL.md");
        prompt.Should().Contain("use read_file");
        prompt.Should().Contain("ongoing skill workflow");
        prompt.Should().NotContain("# Sample Skill");
    }
}
