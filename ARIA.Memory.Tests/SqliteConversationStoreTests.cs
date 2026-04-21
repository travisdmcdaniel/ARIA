using ARIA.Core.Models;
using ARIA.Core.Options;
using ARIA.Memory.Migrations;
using ARIA.Memory.Sqlite;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ARIA.Memory.Tests;

public sealed class SqliteConversationStoreTests
{
    [Fact]
    public async Task GetOrCreateActiveSessionAsync_ReturnsExistingActiveSession_ForUser()
    {
        var store = await CreateStoreAsync();

        var first = await store.GetOrCreateActiveSessionAsync(42);
        var second = await store.GetOrCreateActiveSessionAsync(42);

        second.Should().BeEquivalentTo(first);
    }

    [Fact]
    public async Task ArchiveSessionAsync_DeactivatesSession_AndNextGetCreatesNewActiveSession()
    {
        var store = await CreateStoreAsync();
        var archived = await store.GetOrCreateActiveSessionAsync(42);

        await store.ArchiveSessionAsync(archived.SessionId);
        var next = await store.GetOrCreateActiveSessionAsync(42);
        var archivedReloaded = await store.GetSessionByIdAsync(archived.SessionId);

        archivedReloaded.Should().NotBeNull();
        archivedReloaded!.IsActive.Should().BeFalse();
        next.SessionId.Should().NotBe(archived.SessionId);
        next.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task ListRecentSessionsAsync_ReturnsOnlyRequestedUsersSessions_InRecentFirstOrder()
    {
        var store = await CreateStoreAsync();

        var first = await store.GetOrCreateActiveSessionAsync(42);
        await store.AppendTurnAsync(new ConversationTurn(
            0,
            first.SessionId,
            42,
            DateTime.UtcNow,
            ConversationRole.User,
            "first",
            null,
            null,
            null));
        await store.ArchiveSessionAsync(first.SessionId);

        var otherUser = await store.GetOrCreateActiveSessionAsync(7);
        await store.AppendTurnAsync(new ConversationTurn(
            0,
            otherUser.SessionId,
            7,
            DateTime.UtcNow,
            ConversationRole.User,
            "other",
            null,
            null,
            null));

        await Task.Delay(10);
        var second = await store.GetOrCreateActiveSessionAsync(42);
        await store.AppendTurnAsync(new ConversationTurn(
            0,
            second.SessionId,
            42,
            DateTime.UtcNow,
            ConversationRole.User,
            "second",
            null,
            null,
            null));

        var sessions = await store.ListRecentSessionsAsync(42, retentionDays: 30);

        sessions.Select(s => s.SessionId).Should().Equal(second.SessionId, first.SessionId);
        sessions.Should().OnlyContain(s => s.TelegramUserId == 42);
    }

    [Fact]
    public async Task GetSessionByIdAsync_ReturnsNull_ForUnknownSession()
    {
        var store = await CreateStoreAsync();

        var session = await store.GetSessionByIdAsync("missing-session");

        session.Should().BeNull();
    }

    [Fact]
    public async Task ResumeSessionAsync_ActivatesTargetSession_AndDeactivatesOtherUserSessions()
    {
        var store = await CreateStoreAsync();

        var first = await store.GetOrCreateActiveSessionAsync(42);
        await store.ArchiveSessionAsync(first.SessionId);
        var second = await store.GetOrCreateActiveSessionAsync(42);

        var resumed = await store.ResumeSessionAsync(42, first.SessionId);

        resumed.Should().BeTrue();
        (await store.GetSessionByIdAsync(first.SessionId))!.IsActive.Should().BeTrue();
        (await store.GetSessionByIdAsync(second.SessionId))!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task ResumeSessionAsync_ReturnsFalse_ForAnotherUsersSession()
    {
        var store = await CreateStoreAsync();
        var otherUserSession = await store.GetOrCreateActiveSessionAsync(7);

        var resumed = await store.ResumeSessionAsync(42, otherUserSession.SessionId);

        resumed.Should().BeFalse();
        (await store.GetSessionByIdAsync(otherUserSession.SessionId))!.IsActive.Should().BeTrue();
    }

    private static async Task<SqliteConversationStore> CreateStoreAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "aria-memory-tests", Guid.NewGuid().ToString("N"));
        var options = Options.Create(new AriaOptions
        {
            Workspace =
            {
                RootPath = root,
                DatabasePath = "data/aria.db"
            }
        });

        var dbPath = options.Value.Workspace.GetResolvedDatabasePath();
        var migrator = new DatabaseMigrator(dbPath, NullLogger<DatabaseMigrator>.Instance);
        await migrator.MigrateAsync();

        return new SqliteConversationStore(options);
    }
}
