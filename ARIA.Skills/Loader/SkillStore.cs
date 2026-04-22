using ARIA.Core.Interfaces;
using ARIA.Core.Models;
using ARIA.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ARIA.Skills.Loader;

public sealed class SkillStore : ISkillStore, IDisposable
{
    private readonly SkillLoader _loader;
    private readonly AriaOptions _options;
    private readonly ILogger<SkillStore> _logger;
    private readonly object _gate = new();
    private IReadOnlyList<SkillEntry> _skills = [];
    private FileSystemWatcher? _watcher;

    public SkillStore(
        SkillLoader loader,
        IOptions<AriaOptions> options,
        ILogger<SkillStore> logger)
    {
        _loader = loader;
        _options = options.Value;
        _logger = logger;
    }

    public IReadOnlyList<SkillEntry> GetAll()
    {
        lock (_gate)
            return _skills;
    }

    public Task ReloadAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var loaded = _loader.Load();
        lock (_gate)
            _skills = loaded;

        _logger.LogInformation("Loaded {Count} skill(s)", loaded.Count);
        EnsureWatcher();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }

    private void EnsureWatcher()
    {
        if (!_options.Skills.Enabled || _watcher is not null)
            return;

        var skillsDirectory = _options.Skills.GetResolvedDirectory(_options.Workspace.GetResolvedRootPath());
        Directory.CreateDirectory(skillsDirectory);

        _watcher = new FileSystemWatcher(skillsDirectory, "SKILL.md")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnSkillFileChanged;
        _watcher.Changed += OnSkillFileChanged;
        _watcher.Renamed += OnSkillFileRenamed;
        _watcher.Deleted += OnSkillFileChanged;
    }

    private void OnSkillFileChanged(object sender, FileSystemEventArgs args)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250);
                await ReloadAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to reload skills after change to {Path}", args.FullPath);
            }
        });
    }

    private void OnSkillFileRenamed(object sender, RenamedEventArgs args) =>
        OnSkillFileChanged(sender, args);
}
