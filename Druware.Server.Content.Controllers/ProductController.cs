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

using AutoMapper;
using Druware.Server;
using Druware.Server.Entities;
using RESTfulFoundation.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Authorization;
using Druware.Server.Content.Entities;
using System.Linq.Expressions;
using Druware.Server.Content.Migrations.PostgreSql;
using Product = Druware.Server.Content.Entities.Product;

namespace Druware.Server.Content.Controllers;

/// <summary>
/// The News Controller handles all of the heavy lifting for the Articles
/// and News Feed bits. An Article will support being Tagged using the
/// generic tag pool from Druwer.Server.
/// </summary>
[Route("api/[controller]")]
[Route("[controller]")]
public class ProductController : CustomController
{
    private readonly IMapper _mapper;
    private readonly AppSettings _settings;
    private readonly ContentContext _context;

    /// <summary>
    /// Constructor, handles the passed in elements and passes them to the
    /// base CustomController before moving forward.
    /// </summary>
    /// <param name="configuration"></param>
    /// <param name="mapper"></param>
    /// <param name="userManager"></param>
    /// <param name="signInManager"></param>
    /// <param name="context"></param>
    /// <param name="serverContext"></param>
    public ProductController(
        IConfiguration configuration,
        IMapper mapper,
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        ContentContext context,
        ServerContext serverContext)
        : base(configuration, userManager, signInManager, serverContext)
    {
        _settings = new AppSettings(Configuration);
        _mapper = mapper;
        _context = context;
    }

    /// <summary>
    /// Get a list of the documents, in descending modified date order,
    /// limisted to the paramters passed on the QueryString
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

        if (_context.Products == null)
            return Ok(Result.Ok("No Data Available"));

        var total = _context.Products?.Count() ?? 0;
        var list = _context.Products?
            .OrderBy(a => a.Name)
            //.Include("Product.Tags")
            .TagWithSource("Getting Products")
            .Skip(page * count)
            .Take(count)
            .ToList();
        return Ok(ListResult.Ok(list!, total, page, count));
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

        var r = Product.ByShortOrId(_context, value);
        return (r != null) ? Ok(r) : BadRequest("Not Found");
    }

    private ListResult? _news = null;

    [HttpGet("{value}/news")]
    public async Task<IActionResult> GetNews(string value,
        [FromQuery] int page = 0, [FromQuery] int count = 10)
    {
        // Everyone has access to this method, but we still want to log it
        await LogRequest();

        var p = Product.ByShortOrId(_context, value);

        if (p?.Short == null) return BadRequest("Not Found");
        if (_news != null) return Ok(_news);

        var t = Tag.ByNameOrId(ServerContext, p.Short);
        if (t.TagId == null) return BadRequest("Not Found");

        if (_context.ArticleTags == null) return BadRequest("Not Found");

        var list = _context.News?
            .AsQueryable()
            .Where(article =>
                _context.ArticleTags!.Any(tag =>
                    tag.TagId == t.TagId &&
                    article.ArticleId == tag.ArticleId)
            )
            .Include("ArticleTags")
            .TagWithSource($"Getting articles for tag {p.Short}")
            .OrderByDescending(e => e.Modified)
            .Skip(page * count)
            .Take(count)
            .ToList();
        if (list == null) return BadRequest("Not Found");

        _news = ListResult.Ok(list!, list!.Count, page, count);
        return Ok(_news);
    }

    /// <summary>
    /// Add an Article to the News Library
    /// </summary>
    /// <param name="model"></param>
    /// <returns></returns>
    [HttpPost("")]
    [Authorize(Roles = ProductSecurityRole.AuthorOrEditor + "," +
                       UserSecurityRole.SystemAdministrator)]
    public async Task<ActionResult<Product>> Add(
        [FromBody] Product model)
    {
        var r = await UpdateUserAccess();
        if (r != null) return r;

        if (!ModelState.IsValid)
        {
            var errors = ModelState.Select(x => x.Value.Errors)
                .Where(y => y.Count > 0)
                .ToList();
            var message = "Invalid Model Received";
            foreach (var error in errors)
                message += $"\n\t{error}";
            return Ok(Result.Error(message));
        }

        if (_context.Products == null)
            return
                Ok(Result.Ok(
                    "No Data Available")); // think I want to alter this to not need the Ok()

        // validate the permalink, a duplicate WILL fail the save

        if (!Product.IsShortAvailable(_context, model.Short!))
            return Ok(
                Result.Error("Permalink cannot duplicate an existing link"));

        if (model.Tags != null)
            foreach (string t in model.Tags)
            {
                ProductTag at = new();
                at.Product = model;
                Tag tag = Tag.ByNameOrId(ServerContext, t);
                if (tag.TagId == null)
                    at.Tag = tag;
                else
                    at.TagId = (long)tag.TagId!;
                model.ProductTags.Add(at);
            }

        // update the model with some relevant elements.
        _context.Products.Add(model);
        var i = await _context.SaveChangesAsync();
        if (i == 0)
        {
            return BadRequest("Save Failed");
        }

        // add a tag to the system that matches the short if it does not 
        // already exist

        _ = Tag.ByNameOrId(ServerContext, model.Short!);

        return Ok(model);
    }

    /// <summary>
    /// Update an existing documents within the new library
    /// </summary>
    /// <param name="model">The updated article</param>
    /// <param name="value">The Id or Permalink of the article to update</param>
    /// <returns></returns>
    [HttpPut("{value}")]
    [Authorize(Roles = ProductSecurityRole.AuthorOrEditor + "," +
                       UserSecurityRole.SystemAdministrator)]
    public async Task<ActionResult<Product>> Update(
        [FromBody] Product model, string value)
    {
        var r = await UpdateUserAccess();
        if (r != null) return r;

        // find the article
        var obj = Product.ByShortOrId(_context, value);
        if (obj == null) return BadRequest("Not Found");

        // validate the model
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Select(x => x.Value.Errors)
                .Where(y => y.Count > 0)
                .ToList();
            var message = "Invalid Model Received";
            foreach (var error in errors)
                message += $"\n\t{error}";
            return Ok(Result.Error(message));
        }

        // only handle this if the short has changed
        if (obj.Short != model.Short)
        {
            if (!Product.IsShortAvailable(_context, model.Short!))
                return Ok(
                    Result.Error("Short cannot duplicate an existing short"));
        }

        // set and write the changes
        // cannot use this because it maps the private values too.
        // Author and ByLine cannot be altered here
        obj.Name = model.Name;
        obj.Short = model.Short;
        obj.Description = model.Description;
        obj.License = model.License;
        obj.Summary = model.Summary;
        obj.DocumentationUrl = model.DocumentationUrl;
        obj.DownloadUrl = model.DownloadUrl;
        obj.IconUrl = model.IconUrl;
        obj.Updated = DateTime.Now;

        if (obj.ProductTags != null) obj.ProductTags.Clear();
        if (model.Tags != null && obj.ProductTags != null)
        {
            foreach (string t in model.Tags)
            {
                ProductTag at = new();
                at.ProductId = (long)obj.ProductId!;
                Tag tag = Tag.ByNameOrId(ServerContext, t);
                if (tag.TagId == null)
                    at.Tag = tag;
                else
                    at.TagId = (long)tag.TagId!;

                obj.ProductTags.Add(at);
            }
        }

        await _context.SaveChangesAsync();

        // return the updated object*/
        return Ok(Product.ByShortOrId(_context, value));
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
    [Authorize(Roles = ProductSecurityRole.Editor + "," +
                       UserSecurityRole.SystemAdministrator)]
    public async Task<IActionResult> Remove(string value)
    {
        var r = await UpdateUserAccess();
        if (r != null) return r;

        var obj = Product.ByShortOrId(_context, value);
        if (obj == null) return BadRequest("Not Found");

        _context.Products?.Remove(obj);
        await _context.SaveChangesAsync();

        // Should rework the save to return a success of fail on the delete
        return Ok(Result.Ok("Delete Successful"));
    }


    /// <summary>
    /// Get a discrete News item, either by Id or Permalink
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    [HttpGet("{value}/history")]
    public async Task<IActionResult> GetHistory(string value)
    {
        // Everyone has access to this method, but we still want to log it
        await LogRequest();

        var r = Product.ByShortOrId(_context, value);
        var list = (r != null && r.History != null)
            ? r.History.ToList()
            : new List<ProductRelease>();

        return r is { History: not null }
            ? Ok(ListResult.Ok(list, list.Count, 0, list.Count))
            : BadRequest("Not Found");
    }
    
    /// <summary>
    /// Remove an existing Article ( and it's associated children from the
    /// library.
    ///
    /// This action is limited to Editors, or System Administrators
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    [HttpPost("{productId}/history")]
    [Authorize(Roles = ProductSecurityRole.Editor + "," +
                       UserSecurityRole.SystemAdministrator)]
    public async Task<IActionResult> AddRelease(string productId, [FromBody] ProductRelease model)
    {
        var r = await UpdateUserAccess();
        if (r != null) return r;

        if (!ModelState.IsValid)
        {
            var errors = ModelState.Select(x => x.Value.Errors)
                .Where(y => y.Count > 0)
                .ToList();
            var message = "Invalid Model Received";
            foreach (var error in errors)
                message += $"\n\t{error}";
            return Ok(Result.Error(message));
        }
        
        // validate that the product exists
        var product = Product.ByShortOrId(_context, productId);
        if (product == null) return BadRequest("Not Found");

        model.ProductId = product.ProductId;
        
        _context.ProductReleases?.Add(model);
        await _context.SaveChangesAsync();

        // Should rework the save to return a success of fail on the delete
        return Ok(model);
    }
    
    /// <summary>
    /// Remove an existing Article ( and it's associated children from the
    /// library.
    ///
    /// This action is limited to Editors, or System Administrators
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    [HttpPut("{productId}/history/{releaseId}")]
    [Authorize(Roles = ProductSecurityRole.Editor + "," +
                       UserSecurityRole.SystemAdministrator)]
    public async Task<IActionResult> UpdateRelease(string productId, string releaseId, [FromBody] ProductRelease model)
    {
        var r = await UpdateUserAccess();
        if (r != null) return r;

        if (!ModelState.IsValid)
        {
            var errors = ModelState.Select(x => x.Value.Errors)
                .Where(y => y.Count > 0)
                .ToList();
            var message = "Invalid Model Received";
            foreach (var error in errors)
                message += $"\n\t{error}";
            return Ok(Result.Error(message));
        }
        
        // validate that the product exists
        var product = Product.ByShortOrId(_context, productId);
        if (product == null) return BadRequest("Not Found");
        
        var release = ProductRelease.ById(_context, releaseId);
        if (release == null) return BadRequest("Not Found");
        release.DownloadUrl = model.DownloadUrl;
        release.Body = model.Body;
        release.Title = model.Title;
        release.Modified = DateTime.Now;
        
        await _context.SaveChangesAsync();

        // Should rework the save to return a success of fail on the delete
        return Ok(release);
    }
    
    /// <summary>
    /// Remove an existing Article ( and it's associated children from the
    /// library.
    ///
    /// This action is limited to Editors, or System Administrators
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    [HttpDelete("{productId}/history/{releaseId}")]
    [Authorize(Roles = ProductSecurityRole.Editor + "," +
                       UserSecurityRole.SystemAdministrator)]
    public async Task<IActionResult> RemoveRelease(string productId, string releaseId)
    {
        var r = await UpdateUserAccess();
        if (r != null) return r;

        var obj = ProductRelease.ById(_context, releaseId);
        if (obj == null) return BadRequest("Not Found");

        // find the release in the history.
        _context.ProductReleases?.Remove(obj);
        await _context.SaveChangesAsync();

        // Should rework the save to return a success of fail on the delete
        return Ok(Result.Ok("Delete Successful"));
    }
}

