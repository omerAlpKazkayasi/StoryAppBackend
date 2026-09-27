using System.Text.Json;
using StoryApp.Application.Reading;
using Xunit;

namespace StoryApp.Tests;

public class ReadingContractTests
{
    [Fact]
    public void ReadingSessionResponse_SerializesExpectedContract_AndDoesNotExposeInternalFields()
    {
        // Arrange
        var response = new ReadingSessionResponse(
            SessionId: Guid.NewGuid(),
            StoryId: Guid.NewGuid(),
            StoryTitle: "The Lost Kingdom",
            Status: "InProgress",
            CanGoBack: true,
            CurrentNode: new ReadingNodeResponse(
                Id: Guid.NewGuid(),
                Title: "Chapter 1: The Gateway",
                IsEnding: false,
                Scenes:
                [
                    new ReadingSceneResponse(
                        Id: Guid.NewGuid(),
                        SortOrder: 1,
                        Text: "You arrive at the ancient stone archway.",
                        ImageObjectKey: "scenes/archway.webp")
                ],
                Choices:
                [
                    new ReadingChoiceResponse(
                        TransitionId: Guid.NewGuid(),
                        Title: "Step through the archway",
                        SortOrder: 1)
                ]));

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        // Act
        var json = JsonSerializer.Serialize(response, options);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        // Assert - Expected root fields exist
        Assert.True(root.TryGetProperty("sessionId", out _));
        Assert.True(root.TryGetProperty("storyId", out _));
        Assert.True(root.TryGetProperty("storyTitle", out _));
        Assert.True(root.TryGetProperty("status", out _));
        Assert.True(root.TryGetProperty("canGoBack", out _));
        Assert.True(root.TryGetProperty("currentNode", out var currentNode));

        // Assert - Expected currentNode fields exist
        Assert.True(currentNode.TryGetProperty("id", out _));
        Assert.True(currentNode.TryGetProperty("title", out _));
        Assert.True(currentNode.TryGetProperty("isEnding", out _));
        Assert.True(currentNode.TryGetProperty("scenes", out var scenes));
        Assert.True(currentNode.TryGetProperty("choices", out var choices));

        // Assert - Expected choice fields exist
        var firstChoice = choices.EnumerateArray().First();
        Assert.True(firstChoice.TryGetProperty("transitionId", out _));
        Assert.True(firstChoice.TryGetProperty("title", out _));
        Assert.True(firstChoice.TryGetProperty("sortOrder", out _));

        // Assert - Expected scene fields exist
        var firstScene = scenes.EnumerateArray().First();
        Assert.True(firstScene.TryGetProperty("id", out _));
        Assert.True(firstScene.TryGetProperty("sortOrder", out _));
        Assert.True(firstScene.TryGetProperty("text", out _));
        Assert.True(firstScene.TryGetProperty("imageObjectKey", out _));

        // Assert - Internal fields MUST NOT exist anywhere in the payload
        var lowerJson = json.ToLowerInvariant();
        Assert.DoesNotContain("tonodeid", lowerJson);
        Assert.DoesNotContain("fromnodeid", lowerJson);
        Assert.DoesNotContain("choiceintent", lowerJson);
        Assert.DoesNotContain("nexttargetmomentum", lowerJson);
        Assert.DoesNotContain("memory", lowerJson);
        Assert.DoesNotContain("failurereason", lowerJson);
        Assert.DoesNotContain("currentstep", lowerJson);
        Assert.DoesNotContain("createdat", lowerJson);
        Assert.DoesNotContain("updatedat", lowerJson);
        Assert.DoesNotContain("storynodeid", lowerJson);
        Assert.DoesNotContain("universeid", lowerJson);
        Assert.DoesNotContain("rootnodeid", lowerJson);
    }

    [Fact]
    public void ReadingNodeResponse_WhenCollectionsNull_InitializesToEmptyList()
    {
        // Act
        var node = new ReadingNodeResponse(
            Id: Guid.NewGuid(),
            Title: "Ending Node",
            IsEnding: true,
            Scenes: null!,
            Choices: null!);

        // Assert
        Assert.NotNull(node.Scenes);
        Assert.Empty(node.Scenes);
        Assert.NotNull(node.Choices);
        Assert.Empty(node.Choices);
    }
}
