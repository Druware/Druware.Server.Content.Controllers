/* This file is part of the Druware.Server API Library
 *
 * Foobar is free software: you can redistribute it and/or modify it under the
 * terms of the GNU General Public License as published by the Free Software
 * Foundation, either version 3 of the License, or (at your option) any later
 * version.
 *
 * The Druware.Server API Library is distributed in the hope that it will be
 * useful, but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General
 * Public License for more details.
 *
 * You should have received a copy of the GNU General Public License along with
 * the Druware.Server API Library. If not, see <https://www.gnu.org/licenses/>.
 *
 * Copyright 2019-2023 by:
 *    Andy 'Dru' Satori @ Satori & Associates, Inc.
 *    All Rights Reserved
 */

using Druware.Server;
using Druware.Server.Entities;
using RESTfulFoundation.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Authorization;
using Druware.Server.Content.Entities;

namespace Druware.Server.Content.Controllers;

/// <summary>
/// The Review Controller handles gear reviews. Anyone may read the published
/// ones; a News author or editor, or a system administrator, also sees the
/// drafts. Reviews use the News roles and the generic tag pool from
/// Druware.Server, as articles do.
/// </summary>
[Route("api/[controller]")]
[Route("[controller]")]
public class ReviewController : CustomController
{
    private readonly ContentContext _context;

    /// <summary>
    /// Constructor, handles the passed in elements and passes them to the
    /// base CustomController before moving forward.
    /// </summary>
    /// <param name="configuration"></param>
    /// <param name="userManager"></param>
    /// <param name="signInManager"></param>
    /// <param name="context"></param>
    /// <param name="serverContext"></param>
    public ReviewController(
        IConfiguration configuration,
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        ContentContext context,
        ServerContext serverContext)
        : base(configuration, userManager, signInManager, serverContext)
    {
        _context = context;
    }

    // drafts are visible only to the roles that may write reviews. The
    // Identity cookie carries the user's roles as claims, which is also what
    // [Authorize(Roles = ...)] checks, so no lookup is needed. AuthorOrEditor
    // is a comma-joined list, so each role is checked on its own.
    private bool IncludeUnpublished =>
        User.IsInRole(NewsSecurityRole.Author) ||
        User.IsInRole(NewsSecurityRole.Editor) ||
        User.IsInRole(UserSecurityRole.SystemAdministrator);

    /// <summary>
    /// Get a list of the reviews, newest posted first, limited to the
    /// parameters passed on the QueryString. Only News authors and editors
    /// and system administrators see drafts; everyone else sees only the
    /// published reviews, and the total counts only those.
    /// </summary>
    /// <param name="page">Which 0 based page to fetch</param>
    /// <param name="count">Limit the items per page</param>
    /// <returns>A ListResult containing the resulting list</returns>
    [HttpGet("")]
    public async Task<IActionResult> GetList([FromQuery] int page = 0,
        [FromQuery] int count = 10)
    {
        // Everyone has access to this method, but we still want to log it
        await LogRequest();

        if (_context.Reviews == null)
            return Ok(Result.Ok("No Data Available"));

        var query = Review.List(_context, IncludeUnpublished);
        var total = query.Count();
        var list = query
            .TagWithSource("Getting reviews")
            .Skip(page * count)
            .Take(count)
            .ToList();
        return Ok(ListResult.Ok(list, total, page, count));
    }

    /// <summary>
    /// Get a discrete Review, either by Id or Permalink. A draft answers
    /// Not Found unless the caller is a News author or editor, or a system
    /// administrator.
    /// </summary>
    /// <param name="value">The Id or Permalink of the review</param>
    /// <returns></returns>
    [HttpGet("{value}")]
    public async Task<IActionResult> Get(string value)
    {
        // Everyone has access to this method, but we still want to log it
        await LogRequest();

        var r = Review.ByPermalinkOrId(_context, value, IncludeUnpublished);
        if (r == null) return BadRequest("Not Found");

        return Ok(r);
    }

    /// <summary>
    /// Add a Review. A blank permalink is built from the title.
    /// </summary>
    /// <param name="model">The review to add</param>
    /// <returns></returns>
    [HttpPost("")]
    [Authorize(Roles = NewsSecurityRole.AuthorOrEditor + "," +
                       UserSecurityRole.SystemAdministrator)]
    public async Task<ActionResult<Review>> Add([FromBody] Review model)
    {
        var r = await UpdateUserAccess();
        if (r != null) return r;

        // a body that does not bind at all arrives as null. Anything that
        // did bind is checked by Review.Validate below, which gives a
        // readable message where ModelState would not.
        if (model == null) return BadRequest("Invalid Model Received");

        if (_context.Reviews == null)
            return BadRequest("No Data Available");

        Review.Normalize(model);
        Review.EnsurePermalink(model);

        var message = Review.Validate(model) ?? CheckPhoto(model);
        if (message != null) return BadRequest(message);

        // the permalink is unique, so a duplicate is refused here rather than
        // left to throw on the save
        if (!Review.IsPermalinkValid(_context, model.Permalink!))
            return BadRequest("Permalink cannot duplicate an existing link");

        // the id is assigned by the database, never by the caller
        model.ReviewId = null;
        model.Posted ??= DateTime.Now;
        model.Modified = DateTime.Now;
        ApplyTags(model, model.Tags);

        _context.Reviews.Add(model);
        var i = await _context.SaveChangesAsync();
        if (i == 0)
        {
            return BadRequest("Save Failed");
        }

        // return the entity that was saved, read back so the tags project
        // from the rows
        return Ok(Reread(model));
    }

    /// <summary>
    /// Replace an existing Review. Every editable field takes the value in
    /// the body, so a missing optional field or tag list clears it, except
    /// that a blank permalink is built from the title again, as on Add, and
    /// a missing posted date keeps the stored one. Modified is always set to
    /// now.
    /// </summary>
    /// <param name="value">The Id or Permalink of the review to update</param>
    /// <param name="model">The updated review</param>
    /// <returns></returns>
    [HttpPut("{value}")]
    [Authorize(Roles = NewsSecurityRole.AuthorOrEditor + "," +
                       UserSecurityRole.SystemAdministrator)]
    public async Task<ActionResult<Review>> Update(
        string value, [FromBody] Review model)
    {
        var r = await UpdateUserAccess();
        if (r != null) return r;

        // find the review
        var obj = Review.ByPermalinkOrId(_context, value);
        if (obj == null) return BadRequest("Not Found");

        if (model == null) return BadRequest("Invalid Model Received");

        Review.Normalize(model);
        Review.EnsurePermalink(model);
        var message = Review.Validate(model) ?? CheckPhoto(model);
        if (message != null) return BadRequest(message);

        // the review's own id is left out, so keeping or regenerating the
        // current permalink is not a duplicate
        if (!Review.IsPermalinkValid(_context, model.Permalink!, obj.ReviewId))
            return BadRequest("Permalink cannot duplicate an existing link");

        obj.Title = model.Title;
        obj.Permalink = model.Permalink;
        obj.ProductName = model.ProductName;
        obj.Category = model.Category;
        obj.Summary = model.Summary;
        obj.Body = model.Body;
        obj.PhotoAssetId = model.PhotoAssetId;
        obj.ProductUrl = model.ProductUrl;
        obj.IsAffiliate = model.IsAffiliate;
        obj.EddieScore = model.EddieScore;
        obj.DruScore = model.DruScore;
        obj.EddieTake = model.EddieTake;
        obj.DruTake = model.DruTake;
        if (model.Posted != null) obj.Posted = model.Posted;
        obj.Modified = DateTime.Now;
        obj.IsPublished = model.IsPublished;
        obj.IsFeatured = model.IsFeatured;

        // the old rows are orphaned by the clear and deleted on the save
        obj.ReviewTags.Clear();
        ApplyTags(obj, model.Tags);

        await _context.SaveChangesAsync();

        // return the entity that was saved
        return Ok(Reread(obj));
    }

    /// <summary>
    /// Remove an existing Review, and its tag rows with it.
    ///
    /// This action is limited to Editors, or System Administrators
    /// </summary>
    /// <param name="value">The Id or Permalink of the review to remove</param>
    /// <returns></returns>
    [HttpDelete("{value}")]
    [Authorize(Roles = NewsSecurityRole.Editor + "," +
                       UserSecurityRole.SystemAdministrator)]
    public async Task<IActionResult> Remove(string value)
    {
        var r = await UpdateUserAccess();
        if (r != null) return r;

        var obj = Review.ByPermalinkOrId(_context, value);
        if (obj == null) return BadRequest("Not Found");

        _context.Reviews?.Remove(obj);
        await _context.SaveChangesAsync();

        // Should rework the save to return a success of fail on the delete
        return Ok(Result.Ok("Delete Successful"));
    }

    // an unknown asset would fail the foreign key on the save
    private string? CheckPhoto(Review model) =>
        model.PhotoAssetId == null ||
        _context.Assets?.Any(a => a.AssetId == model.PhotoAssetId) == true
            ? null
            : "Photo asset not found";

    // adds a row per distinct, non-blank tag name, creating missing tags in
    // the shared pool the way NewsController does
    private void ApplyTags(Review review, string[]? tags)
    {
        if (tags == null) return;

        foreach (var name in tags
                     .Where(t => !string.IsNullOrWhiteSpace(t))
                     .Select(t => t.Trim())
                     .Distinct())
        {
            ReviewTag rt = new() { Review = review };
            var tag = Tag.ByNameOrId(ServerContext, name);
            if (tag.TagId == null)
                rt.Tag = tag;
            else
                rt.TagId = (long)tag.TagId!;
            review.ReviewTags.Add(rt);
        }
    }

    // a fresh read, so Tags projects the stored rows rather than echoing
    // what the body sent
    private Review? Reread(Review review)
    {
        _context.ChangeTracker.Clear();
        return Review.ByPermalinkOrId(_context, review.ReviewId!.Value.ToString());
    }
}
