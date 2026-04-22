using ARIA.Core.Options;
using ARIA.Skills.Loader;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ARIA.Skills.Tests;

public sealed class SkillLoaderTests
{
    [Fact]
    public void Load_ReturnsMetadataForSkillWithValidFrontMatter()
    {
        var root = CreateTempRoot();
        var skillDirectory = Path.Combine(root, "skills", "sample");
        Directory.CreateDirectory(skillDirectory);
        File.WriteAllText(Path.Combine(skillDirectory, "SKILL.md"), """
            ---
            name: Sample Skill
            description: Does sample work.
            ---

            # Sample Skill
            """);

        var skills = CreateLoader(root).Load();

        skills.Should().ContainSingle();
        skills[0].Name.Should().Be("Sample Skill");
        skills[0].Description.Should().Be("Does sample work.");
        skills[0].Directory.Should().Be("skills/sample");
        skills[0].Path.Should().Be("skills/sample/SKILL.md");
    }

    [Fact]
    public void Load_SkipsSkillMissingRequiredFrontMatter()
    {
        var root = CreateTempRoot();
        var skillDirectory = Path.Combine(root, "skills", "bad");
        Directory.CreateDirectory(skillDirectory);
        File.WriteAllText(Path.Combine(skillDirectory, "SKILL.md"), "# Missing front matter");

        var skills = CreateLoader(root).Load();

        skills.Should().BeEmpty();
    }

    [Fact]
    public void Load_ReturnsEmpty_WhenSkillsAreDisabled()
    {
        var root = CreateTempRoot();
        var skillDirectory = Path.Combine(root, "skills", "sample");
        Directory.CreateDirectory(skillDirectory);
        File.WriteAllText(Path.Combine(skillDirectory, "SKILL.md"), """
            ---
            name: Sample Skill
            description: Does sample work.
            ---
            """);

        var skills = CreateLoader(root, enabled: false).Load();

        skills.Should().BeEmpty();
    }

    private static SkillLoader CreateLoader(string root, bool enabled = true) =>
        new(
            Options.Create(new AriaOptions
            {
                Workspace =
                {
                    RootPath = root
                },
                Skills =
                {
                    Enabled = enabled,
                    Directory = "skills"
                }
            }),
            NullLogger<SkillLoader>.Instance);

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "aria-skill-loader-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
