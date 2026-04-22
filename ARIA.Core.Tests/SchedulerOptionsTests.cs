using ARIA.Core.Options;
using FluentAssertions;

namespace ARIA.Core.Tests;

public sealed class SchedulerOptionsTests
{
    [Fact]
    public void Defaults_MatchSchedulerConfigDefaults()
    {
        var options = new SchedulerOptions();

        options.Enabled.Should().BeTrue();
        options.Directory.Should().Be("jobs");
        options.RunMissedJobsAsap.Should().BeTrue();
    }

    [Fact]
    public void GetResolvedDirectory_ReturnsWorkspaceRelativePath_ForRelativeDirectory()
    {
        var options = new SchedulerOptions { Directory = "jobs" };

        var resolved = options.GetResolvedDirectory(Path.Combine("C:", "ARIAWorkspace"));

        resolved.Should().Be(Path.Combine("C:", "ARIAWorkspace", "jobs"));
    }

    [Fact]
    public void GetResolvedDirectory_ReturnsAbsolutePath_ForAbsoluteDirectory()
    {
        var absolute = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "aria-jobs"));
        var options = new SchedulerOptions { Directory = absolute };

        var resolved = options.GetResolvedDirectory(Path.Combine("C:", "ARIAWorkspace"));

        resolved.Should().Be(absolute);
    }

    [Fact]
    public void GetResolvedDirectory_ExpandsEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable("ARIA_TEST_JOBS", "scheduled-jobs");
        var options = new SchedulerOptions { Directory = "%ARIA_TEST_JOBS%" };

        var resolved = options.GetResolvedDirectory(Path.Combine("C:", "ARIAWorkspace"));

        resolved.Should().Be(Path.Combine("C:", "ARIAWorkspace", "scheduled-jobs"));
    }
}
