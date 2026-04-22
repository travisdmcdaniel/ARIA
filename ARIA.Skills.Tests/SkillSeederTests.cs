using ARIA.Core.Options;
using ARIA.Skills.Loader;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ARIA.Skills.Tests;

public sealed class SkillSeederTests
{
    [Fact]
    public async Task SeedAsync_WritesBuiltInSkillsWithRequiredFrontMatter()
    {
        var root = CreateTempRoot();
        var options = Options.Create(new AriaOptions
        {
            Workspace =
            {
                RootPath = root
            },
            Skills =
            {
                Enabled = true,
                Directory = "skills"
            }
        });

        var seeder = new SkillSeeder(options, NullLogger<SkillSeeder>.Instance);

        await seeder.SeedAsync();

        var createSkill = Path.Combine(root, "skills", "create_new_skill", "SKILL.md");
        var onboarding = Path.Combine(root, "skills", "onboarding", "SKILL.md");
        File.Exists(createSkill).Should().BeTrue();
        File.Exists(onboarding).Should().BeTrue();
        File.ReadAllText(createSkill).Should().Contain("name: create_new_skill");
        File.ReadAllText(onboarding).Should().Contain("name: onboarding");
    }

    [Fact]
    public async Task SeedAsync_RefreshesBuiltInSkill_WhenBundledContentChanges()
    {
        var root = CreateTempRoot();
        var options = Options.Create(new AriaOptions
        {
            Workspace =
            {
                RootPath = root
            },
            Skills =
            {
                Enabled = true,
                Directory = "skills"
            }
        });

        var onboarding = Path.Combine(root, "skills", "onboarding", "SKILL.md");
        Directory.CreateDirectory(Path.GetDirectoryName(onboarding)!);
        await File.WriteAllTextAsync(onboarding, "old content");

        var seeder = new SkillSeeder(options, NullLogger<SkillSeeder>.Instance);

        await seeder.SeedAsync();

        var content = await File.ReadAllTextAsync(onboarding);
        content.Should().Contain("name: onboarding");
        content.Should().Contain("Ask one question at a time");
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "aria-skill-seeder-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
