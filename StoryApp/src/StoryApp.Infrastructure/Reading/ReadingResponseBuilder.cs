using Microsoft.EntityFrameworkCore;
using StoryApp.Application.Common.Exceptions;
using StoryApp.Application.Reading;
using StoryApp.Infrastructure.Persistence;

namespace StoryApp.Infrastructure.Reading;

public static class ReadingResponseBuilder
{
    public static async Task<ReadingNodeResponse> BuildNodeResponseAsync(
        AppDbContext dbContext,
        Guid nodeId,
        CancellationToken cancellationToken = default)
    {
        var node = await dbContext.StoryNodes
            .AsNoTracking()
            .Where(n => n.Id == nodeId)
            .Select(n => new
            {
                n.Id,
                n.Title,
                n.IsEnding
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (node == null)
        {
            throw new NotFoundException("Story node not found.");
        }

        var scenes = await dbContext.StoryNodeScenes
            .AsNoTracking()
            .Where(s => s.StoryNodeId == nodeId)
            .OrderBy(s => s.SortOrder)
            .Select(s => new ReadingSceneResponse(
                s.Id,
                s.SortOrder,
                s.Text,
                s.ImageObjectKey))
            .ToListAsync(cancellationToken);

        IReadOnlyList<ReadingChoiceResponse> choices;
        if (node.IsEnding)
        {
            choices = [];
        }
        else
        {
            choices = await dbContext.StoryTransitions
                .AsNoTracking()
                .Where(t => t.FromNodeId == nodeId && t.ToNodeId != null)
                .OrderBy(t => t.SortOrder)
                .Select(t => new ReadingChoiceResponse(
                    t.Id,
                    t.ChoiceTitle,
                    t.SortOrder))
                .ToListAsync(cancellationToken);
        }

        return new ReadingNodeResponse(
            node.Id,
            node.Title,
            node.IsEnding,
            scenes,
            choices);
    }
}
