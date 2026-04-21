using ARIA.Core.Models;
using ARIA.LlmAdapter.Ollama;
using FluentAssertions;

namespace ARIA.Agent.Tests;

public sealed class OllamaRequestBuilderImageTests
{
    [Fact]
    public void BuildChatRequest_IncludesImages_WhenVisionIsSupported()
    {
        var builder = new OllamaRequestBuilder();

        var request = builder.BuildChatRequest(
            "llava:latest",
            [
                new ChatMessage(
                    ConversationRole.User,
                    "What is in this image?",
                    Images: [new ImageAttachment("base64-image", "image/png")])
            ],
            tools: null,
            stream: false,
            supportsVision: true);

        request.Messages[0].Images.Should().Equal("base64-image");
    }

    [Fact]
    public void BuildChatRequest_OmitsImages_WhenVisionIsNotSupported()
    {
        var builder = new OllamaRequestBuilder();

        var request = builder.BuildChatRequest(
            "qwen:latest",
            [
                new ChatMessage(
                    ConversationRole.User,
                    "What is in this image?",
                    Images: [new ImageAttachment("base64-image", "image/png")])
            ],
            tools: null,
            stream: false,
            supportsVision: false);

        request.Messages[0].Images.Should().BeNull();
    }
}
