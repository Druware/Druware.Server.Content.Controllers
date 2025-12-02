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

/* History
 * 2025-11-21 - Dru - Code cleanup, added support for Header and Icon Images
 *                    and fixed a bug in update with duplicate permalinks
 *                    released v1.1.4
 */


using Druware.Extensions;
using Druware.Server.Entities;
using RESTfulFoundation.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Authorization;
using Druware.Server.Content.Entities;

// TODO: Add a search/query function

namespace Druware.Server.Content.Controllers
{
    /// <summary>
    /// The News Controller handles all of the heavy lifting for the Articles
    /// and News Feed bits. An Article will support being Tagged using the
    /// generic tag pool from Druwer.Server.
    /// </summary>
    [Route("api/[controller]")]
    [Route("[controller]")]
    public class NewsController : CustomController
    {
        private readonly ContentContext _context;

        /// <summary>
        /// Constructor handles the passed in elements and passes them to the
        /// base CustomController before moving forward.
        /// </summary>
        /// <param name="configuration"></param>
        /// <param name="userManager"></param>
        /// <param name="signInManager"></param>
        /// <param name="context"></param>
        /// <param name="serverContext"></param>
        public NewsController(
            IConfiguration configuration,
            UserManager<User> userManager,
            SignInManager<User> signInManager,
            ContentContext context,
            ServerContext serverContext)
            : base(configuration, userManager, signInManager, serverContext)
        {
            _context = context;
        }

        /// <summary>
        /// Get a list of the articles, in descending modified date order,
        /// limited to the paramters passed on the QueryString
        /// </summary>
        /// <param name="page">Which 0 based page to fetch</param>
        /// <param name="count">Limit the items per page</param>
        /// <returns>A ListResult containing the resulting list</returns>
        [HttpGet("")]
        public async Task<IActionResult> GetList([FromQuery] int page = 0, [FromQuery] int count = 10)
        {
            // Everyone has access to this method, but we still want to log it
            await LogRequest();

            if (_context.News == null) return Ok(Result.Ok("No Data Available"));

            var total = _context.News?.Count() ?? 0;
            var list = _context.News?
                .OrderByDescending(a => a.Modified)
                .Include("ArticleTags.Tag")
                .Include("HeaderImage")
                .TagWithSource("Getting articles")
                .Skip(page * count)
                .Take(count)
                .ToList();
            var result = ListResult.Ok(list!, total, page, count);
            return Ok(result);
        }

        /// <summary>
        /// Get a discrete News item, either by Id or Permalink
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        [HttpGet("{value}")]
        public async Task<IActionResult> Get(string value)
        {
            // Everyone has access to this method, but we still want to log it
            await LogRequest();

            var article = Article.ByPermalinkOrId(_context, value);
            return (article != null) ? Ok(article) : BadRequest("Not Found");
        }

        /// <summary>
        /// Add an Article to the News Library
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost("")]
        [Authorize(Roles = NewsSecurityRole.AuthorOrEditor + "," + UserSecurityRole.SystemAdministrator)]
        public async Task<ActionResult<Article>> Add(
            [FromBody] Article model)
        {
            ActionResult? r = await UpdateUserAccess();
            if (r != null) return r;

            if (!ModelState.IsValid)
            {
                var errors = ModelState.Select(x => x.Value.Errors)
                       .Where(y => y.Count > 0)
                       .ToList();
                var message = "Invalid Model Recieved";
                foreach (var error in errors)
                    message += $"\n\t{error}";
                return Ok(Result.Error(message));
            }

            if (_context.News == null)
                return Ok(Result.Ok("No Data Available")); // think I want to alter this to not need the Ok()

            // validate the permalink, a duplicate WILL fail the save

            model.Permalink ??= model.Title
                .Replace(" ", "_")
                .Replace("!", "")
                .Replace(",", "")
                .Replace("\"", "")
                .Replace("?", "")
                .Replace("=", "")
                .EncodeUrl();

            if (!Article.IsPermalinkValid(_context, model.Permalink))
                return Ok(Result.Error("Permalink cannot duplicate an existing link"));

            // update the model with some relevant elements.

            // authorId & byLine
            if (model.AuthorId == null)
            {
                var user = await UserManager.GetUserAsync(HttpContext.User);
                if (user == null) return BadRequest("No User Found");
                model.AuthorId = new Guid(user.Id);
                model.ByLine = user.LastName + ", " + user.FirstName;
            }
            model.Posted = DateTime.Now;
            model.Modified = DateTime.Now;

            // tags-

            if (model.Tags != null)
                foreach (string t in model.Tags)
                { 
                    ArticleTag at = new()
                    {
                        Article = model
                    };
                    var tag = Tag.ByNameOrId(ServerContext, t);
                    if (tag.TagId == null)
                        at.Tag = tag;
                    else
                        at.TagId = (long)tag.TagId!;
                    model.ArticleTags.Add(at);
                }

            _context.News.Add(model);
            await _context.SaveChangesAsync();
            return Ok(model);
        }

        /// <summary>
        /// Update an existing article within the new library
        /// </summary>
        /// <param name="model">The updated article</param>
        /// <param name="value">The Id or Permalink of the article to update</param>
        /// <returns></returns>
        [HttpPut("{value}")]
        [Authorize(Roles = NewsSecurityRole.AuthorOrEditor + "," + UserSecurityRole.SystemAdministrator)]
        public async Task<ActionResult<Article>> Update(      
            [FromBody] Article model, string value)
        {
            ActionResult? r = await UpdateUserAccess();
            if (r != null) return r;

            // find the article
            Article? article = Article.ByPermalinkOrId(_context, value);
            if (article == null) return BadRequest("Not Found");

            // validate the model
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Select(x => x.Value.Errors)
                       .Where(y => y.Count > 0)
                       .ToList();
                var message = "Invalid Model Recieved";
                foreach (var error in errors)
                    message += $"\n\t{error}";
                return Ok(Result.Error(message));
            }

            // validate the changes on the internal rules.
            // the Id cannot be changed.
            // the postedDate cannot be changed
            // may need to adjust the collections ( tags/comments )

            // validate the permalink, a duplicate WILL fail the save

            // if the existing link is the same as the new, then skip this check
            if (model.Permalink != article.Permalink)
            {
                model.Permalink ??= model.Title
                    .Replace(" ", "_")
                    .Replace("!", "")
                    .Replace(",", "")
                    .Replace("\"", "")
                    .Replace("?", "")
                    .Replace("=", "")
                    .EncodeUrl();
                
                if (!Article.IsPermalinkValid(_context, model.Permalink,
                        model.ArticleId))
                    return Ok(
                        Result.Error(
                            "Permalink cannot duplicate an existing link"));
            }

            // set and write the changes
            // cannot use this because it maps the private values too.
            // Author and ByLine cannot be altered here
            article.Title = model.Title;
            article.Summary = model.Summary;
            article.Body = model.Body;
            article.Modified = DateTime.Now;
            article.Pinned = model.Pinned;
            article.Expires = model.Expires;
            article.Permalink = model.Permalink;

            article.ArticleTags.Clear();
            if (model.Tags != null)
            { 
                foreach (var t in model.Tags)
                {
                    ArticleTag at = new()
                    {
                        ArticleId = article.ArticleId
                    };
                    var tag = Tag.ByNameOrId(ServerContext, t);
                    if (tag.TagId == null)
                        at.Tag = tag;
                    else
                        at.TagId = (long)tag.TagId!;

                    article.ArticleTags.Add(at);
                }
            }
            await _context.SaveChangesAsync();

            // return the updated object
            return Ok(Article.ByPermalinkOrId(_context, value));
        }

        /// <summary>
        /// Remove an existing Article ( and it's associated children from the
        /// library.
        ///
        /// This action is limited to Editors, or System Administrators
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        [HttpDelete("{value}")]
        [Authorize(Roles = NewsSecurityRole.Editor + "," + UserSecurityRole.SystemAdministrator)]
        public async Task<IActionResult> Remove(string value)
        {
            ActionResult? r = await UpdateUserAccess();
            if (r != null) return r;

            Article? article = Article.ByPermalinkOrId(_context, value);
            if (article == null) return BadRequest("Not Found");

            _context.News?.Remove(article);
            await _context.SaveChangesAsync();

            // Should rework the save to return a success of fail on the delete
            return Ok(Result.Ok("Delete Successful"));
        }

    }

}

