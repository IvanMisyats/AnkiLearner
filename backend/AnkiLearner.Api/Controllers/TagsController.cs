using AnkiLearner.Api.Contracts;
using AnkiLearner.Core.Abstractions;
using AnkiLearner.Core.Entities;
using AnkiLearner.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AnkiLearner.Api.Controllers;

[ApiController]
[Route("api/tags")]
[Authorize]
public class TagsController(AppDbContext db, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<TagDto>>> List(CancellationToken ct)
    {
        return await db.Tags
            .Where(t => t.UserId == currentUser.UserId)
            .OrderBy(t => t.Name)
            .Select(t => new TagDto(t.Id, t.Name, t.WordTags.Count))
            .ToListAsync(ct);
    }

    [HttpPost]
    public async Task<ActionResult<TagDto>> Create(SaveTagRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim();
        if (name.Length == 0)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Tag name is empty.");

        var exists = await db.Tags.AnyAsync(
            t => t.UserId == currentUser.UserId && t.Name == name, ct);
        if (exists)
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: $"Tag '{name}' already exists.");

        var tag = new Tag { UserId = currentUser.UserId, Name = name };
        db.Tags.Add(tag);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(List), new TagDto(tag.Id, tag.Name, 0));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TagDto>> Rename(Guid id, SaveTagRequest request, CancellationToken ct)
    {
        var tag = await db.Tags
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == currentUser.UserId, ct);
        if (tag is null) return NotFound();

        var name = request.Name.Trim();
        if (name.Length == 0)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Tag name is empty.");

        var taken = await db.Tags.AnyAsync(
            t => t.UserId == currentUser.UserId && t.Name == name && t.Id != id, ct);
        if (taken)
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: $"Tag '{name}' already exists.");

        tag.Name = name;
        await db.SaveChangesAsync(ct);
        var count = await db.WordTags.CountAsync(wt => wt.TagId == id, ct);
        return new TagDto(tag.Id, tag.Name, count);
    }

    /// <summary>Deletes the tag and its links; words themselves are untouched (spec FR-D6).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var tag = await db.Tags
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == currentUser.UserId, ct);
        if (tag is null) return NotFound();

        db.Tags.Remove(tag); // WordTag links cascade
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
