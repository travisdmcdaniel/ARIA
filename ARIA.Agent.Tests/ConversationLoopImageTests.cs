using ARIA.Agent.Conversation;
using ARIA.Agent.Prompts;
using ARIA.Core.Interfaces;
using ARIA.Core.Models;
using ARIA.Core.Options;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ARIA.Agent.Tests;

public sealed class ConversationLoopImageTests
{
    [Fact]
    public async Task RunTurnAsync_DetectsCapabilities_WhenImageArrivesBeforeStartupDetection()
    {
        var llm = Substitute.For<ILlmAdapter>();
        llm.Capabilities.Returns((LlmCapabilities?)null);
        llm.DetectCapabilitiesAsync(Arg.Any<CancellationToken>())
            .Returns(new LlmCapabilities(
                SupportsVision: false,
                SupportsToolCalling: false,
                SupportsStreaming: true));

        var store = Substitute.For<IConversationStore>();
        var loop = CreateLoop(llm, store);

        var response = await loop.RunTurnAsync(
            telegramUserId: 42,
            userText: "describe this",
            images: [new ImageAttachment("base64-image", "image/png")]);

        response.Should().Contain("does not support image input");
        _ = llm.Received(1).DetectCapabilitiesAsync(Arg.Any<CancellationToken>());
        _ = llm.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default, default);
        _ = store.DidNotReceiveWithAnyArgs().AppendTurnAsync(default!, default);
    }

    [Fact]
    public async Task RunTurnAsync_RejectsImages_WhenDetectedModelDoesNotSupportVision()
    {
        var llm = Substitute.For<ILlmAdapter>();
        llm.Capabilities.Returns(new LlmCapabilities(
            SupportsVision: false,
            SupportsToolCalling: false,
            SupportsStreaming: true));

        var store = Substitute.For<IConversationStore>();
        var loop = CreateLoop(llm, store);

        var response = await loop.RunTurnAsync(
            telegramUserId: 42,
            userText: "describe this",
            images: [new ImageAttachment("base64-image", "image/png")]);

        response.Should().Contain("does not support image input");
        _ = llm.DidNotReceiveWithAnyArgs().DetectCapabilitiesAsync(default);
        _ = llm.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default, default);
        _ = store.DidNotReceiveWithAnyArgs().AppendTurnAsync(default!, default);
    }

    private static ConversationLoop CreateLoop(ILlmAdapter llm, IConversationStore store)
    {
        var toolRegistry = Substitute.For<IToolRegistry>();
        toolRegistry.GetToolDefinitions().Returns([]);

        var contextStore = Substitute.For<IContextFileStore>();
        var skillStore = Substitute.For<ISkillStore>();
        skillStore.GetAll().Returns([]);

        var options = Options.Create(new AriaOptions());
        var promptBuilder = new SystemPromptBuilder(
            contextStore,
            skillStore,
            options,
            NullLogger<SystemPromptBuilder>.Instance);

        return new ConversationLoop(
            llm,
            store,
            toolRegistry,
            promptBuilder,
            options,
            NullLogger<ConversationLoop>.Instance);
    }
}
