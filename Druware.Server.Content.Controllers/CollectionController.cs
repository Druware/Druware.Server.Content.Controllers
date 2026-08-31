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
using Collection = Druware.Server.Content.Entities.Collection;
using Product = Druware.Server.Content.Entities.Product;

namespace Druware.Server.Content.Controllers;

/// <summary>
/// The Collection Controller handles the grouping of Products into named
/// Collections. A Collection is little more than a name, a description, and
/// the set of Products that belong to it, so the membership is exposed both
/// as the Products projection on the entity itself and as a discrete set of
/// routes below the collection.
/// </summary>
[Route("api/[controller]")]
[Route("[controller]")]
public class CollectionController : CustomController
{
    private readonly AppSettings _settings;
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
    public CollectionController(
        IConfiguration configuration,
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        ContentContext context,
        ServerContext serverContext)
        : base(configuration, userManager, signInManager, serverContext)
    {
        _settings = new AppSettings(Configuration);
        _context = context;
    }

    /// <summary>
    /// Get a list of the collections, in ascending name order, limited to
    /// the parameters passed on the QueryString
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

        if (_context.Collections == null)
            return Ok(Result.Ok("No Data Available"));

        var total = _context.Collections?.Count() ?? 0;
        var list = _context.Collections?
            .OrderBy(c => c.Name)
            .Include("CollectionProducts.Product")
            .TagWithSource("Getting Collections")
            .Skip(page * count)
            .Take(count)
            .ToList();
        return Ok(ListResult.Ok(list!, total, page, count));
    }

    /// <summary>
    /// Get a discrete Collection, either by Id or Name
    /// </summary>
    /// <param name="value">The Id or Name of the collection</param>
    /// <returns></returns>
    [HttpGet("{value}")]
    public async Task<IActionResult> Get(string value)
    {
        // Everyone has access to this method, but we still want to log it
        await LogRequest();

        var r = Collection.ByNameOrId(_context, value);
        if (r == null) return BadRequest("Not Found");

        // ByNameOrId only includes the join when it matches on the id, so the
        // Products projection would come back empty on a name lookup. Load
        // the join, and the products it points at, explicitly.
        _context.Entry(r)
            .Collection(c => c.CollectionProducts)
            .Query()
            .Include(cp => cp.Product)
            .Load();

        return Ok(r);
    }

    /// <summary>
    /// Add a Collection to the library
    /// </summary>
    /// <param name="model">The collection to add</param>
    /// <returns></returns>
    [HttpPost("")]
    [Authorize(Roles = CollectionSecurityRole.AuthorOrEditor + "," +
                       UserSecurityRole.SystemAdministrator)]
    public async Task<ActionResult<Collection>> Add(
        [FromBody] Collection model)
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

        if (_context.Collections == null)
            return
                Ok(Result.Ok(
                    "No Data Available")); // think I want to alter this to not need the Ok()

        // the name is the human readable key, so it has to be there, and it
        // has to be unique or the save WILL fail
        if (string.IsNullOrWhiteSpace(model.Name))
            return Ok(Result.Error("Name is required"));

        if (!Collection.IsNameAvailable(_context, model.Name!))
            return Ok(
                Result.Error("Name cannot duplicate an existing collection"));

        if (model.Products != null)
        {
            // the join carries a unique index on (collection, product), so a
            // repeated short in the submitted array has to be dropped here
            // rather than left to throw on the save
            List<string> seen = new();
            foreach (string p in model.Products)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                if (seen.Contains(p)) continue;
                seen.Add(p);

                Product? found = Product.ByShortOrId(_context, p);
                if (found == null)
                    return Ok(Result.Error($"Unknown product: {p}"));

                CollectionProduct cp = new();
                cp.Collection = model;
                if (found.ProductId == null)
                    cp.Product = found;
                else
                    cp.ProductId = (long)found.ProductId!;
                model.CollectionProducts.Add(cp);
            }
        }

        _context.Collections.Add(model);
        var i = await _context.SaveChangesAsync();
        if (i == 0)
        {
            return BadRequest("Save Failed");
        }

        return Ok(model);
    }

    /// <summary>
    /// Update an existing Collection within the library
    /// </summary>
    /// <param name="value">The Id or Name of the collection to update</param>
    /// <param name="model">The updated collection</param>
    /// <returns></returns>
    [HttpPut("{value}")]
    [Authorize(Roles = CollectionSecurityRole.AuthorOrEditor + "," +
                       UserSecurityRole.SystemAdministrator)]
    public async Task<ActionResult<Collection>> Update(
        string value, [FromBody] Collection model)
    {
        var r = await UpdateUserAccess();
        if (r != null) return r;

        // find the collection
        var obj = Collection.ByNameOrId(_context, value);
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

        // a rename still has to land on a name nobody else owns, but leaving
        // the name alone is not a duplicate of itself
        if (model.Name != null && model.Name != obj.Name)
        {
            if (string.IsNullOrWhiteSpace(model.Name))
                return Ok(Result.Error("Name is required"));
            if (!Collection.IsNameAvailable(_context, model.Name))
                return Ok(
                    Result.Error(
                        "Name cannot duplicate an existing collection"));
        }

        // set and write the changes
        // cannot map the whole model because it maps the private values too.
        // CollectionId is the key, and Created and Updated are computed by
        // the database, so none of the three may be assigned here.
        if (model.Name != null) obj.Name = model.Name;
        if (model.Description != null) obj.Description = model.Description;

        // membership is replace on update - a null Products leaves the
        // existing membership untouched
        if (model.Products != null)
        {
            // ByNameOrId only includes the join when it matches on the id, so
            // load it explicitly before severing it
            _context.Entry(obj)
                .Collection(c => c.CollectionProducts)
                .Query()
                .Include(cp => cp.Product)
                .Load();

            obj.CollectionProducts.Clear();

            List<string> seen = new();
            foreach (string p in model.Products)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                if (seen.Contains(p)) continue;
                seen.Add(p);

                Product? found = Product.ByShortOrId(_context, p);
                if (found == null)
                    return Ok(Result.Error($"Unknown product: {p}"));

                CollectionProduct cp = new();
                cp.CollectionId = (long)obj.CollectionId!;
                if (found.ProductId == null)
                    cp.Product = found;
                else
                    cp.ProductId = (long)found.ProductId!;

                obj.CollectionProducts.Add(cp);
            }
        }

        await _context.SaveChangesAsync();

        // return the updated object. The route value cannot be used to look
        // it up again - on a rename addressed by name it is the old name,
        // which no longer resolves - so the entity that was just saved is
        // returned directly.
        _context.Entry(obj)
            .Collection(c => c.CollectionProducts)
            .Query()
            .Include(cp => cp.Product)
            .Load();

        // the Products projection caches on first read, so clear it and let
        // it rebuild from the membership that was actually written
        obj.Products = null;

        return Ok(obj);
    }

    /// <summary>
    /// Remove an existing Collection ( and it's associated membership ) from
    /// the library. The Products themselves are left alone, only the join
    /// rows go away with the collection.
    ///
    /// This action is limited to Editors, or System Administrators
    /// </summary>
    /// <param name="value">The Id or Name of the collection to remove</param>
    /// <returns></returns>
    [HttpDelete("{value}")]
    [Authorize(Roles = CollectionSecurityRole.Editor + "," +
                       UserSecurityRole.SystemAdministrator)]
    public async Task<IActionResult> Remove(string value)
    {
        var r = await UpdateUserAccess();
        if (r != null) return r;

        var obj = Collection.ByNameOrId(_context, value);
        if (obj == null) return BadRequest("Not Found");

        // the join is ClientCascade, so the rows have to be loaded for EF to
        // cascade them. ByNameOrId only includes them on an id match.
        _context.Entry(obj)
            .Collection(c => c.CollectionProducts)
            .Load();

        _context.Collections?.Remove(obj);
        await _context.SaveChangesAsync();

        // Should rework the save to return a success of fail on the delete
        return Ok(Result.Ok("Delete Successful"));
    }

    /// <summary>
    /// Get the list of Products that belong to a Collection, in ascending
    /// name order, limited to the parameters passed on the QueryString
    /// </summary>
    /// <param name="value">The Id or Name of the collection</param>
    /// <param name="page">Which 0 based page to fetch</param>
    /// <param name="count">Limit the items per page</param>
    /// <returns>A ListResult containing the resulting list</returns>
    [HttpGet("{value}/products")]
    public async Task<IActionResult> GetProducts(string value,
        [FromQuery] int page = 0, [FromQuery] int count = 10)
    {
        // Everyone has access to this method, but we still want to log it
        await LogRequest();

        var obj = Collection.ByNameOrId(_context, value);
        if (obj == null) return BadRequest("Not Found");

        if (_context.CollectionProducts == null)
            return Ok(Result.Ok("No Data Available"));

        var id = (long)obj.CollectionId!;

        var total = _context.CollectionProducts?
            .Count(cp => cp.CollectionId == id) ?? 0;
        var list = _context.CollectionProducts?
            .Where(cp => cp.CollectionId == id)
            // no Include here - EF discards an Include that precedes a
            // projection, and the Select already pulls the products
            .Select(cp => cp.Product!)
            .OrderBy(p => p.Name)
            .TagWithSource($"Getting products for collection {obj.Name}")
            .Skip(page * count)
            .Take(count)
            .ToList();
        return Ok(ListResult.Ok(list!, total, page, count));
    }

    /// <summary>
    /// Add a Product to a Collection
    /// </summary>
    /// <param name="value">The Id or Name of the collection</param>
    /// <param name="productValue">The Id or Short of the product</param>
    /// <returns></returns>
    [HttpPost("{value}/products/{productValue}")]
    [Authorize(Roles = CollectionSecurityRole.AuthorOrEditor + "," +
                       UserSecurityRole.SystemAdministrator)]
    public async Task<IActionResult> AddProduct(string value,
        string productValue)
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

        if (_context.CollectionProducts == null)
            return Ok(Result.Ok("No Data Available"));

        var obj = Collection.ByNameOrId(_context, value);
        if (obj == null) return BadRequest("Not Found");

        var product = Product.ByShortOrId(_context, productValue);
        if (product?.ProductId == null) return BadRequest("Not Found");

        var id = (long)obj.CollectionId!;
        var productId = (long)product.ProductId!;

        // the join carries a unique index on (collection, product), so a
        // repeat has to be caught here rather than left to throw on the save
        var exists = _context.CollectionProducts?
            .TagWithSource("Checking collection membership")
            .Any(cp => cp.CollectionId == id &&
                       cp.ProductId == productId) ?? false;
        if (exists) return Ok(Result.Ok("Already a member"));

        CollectionProduct model = new();
        model.CollectionId = id;
        model.ProductId = productId;

        _context.CollectionProducts?.Add(model);
        await _context.SaveChangesAsync();

        return Ok(Result.Ok("Add Successful"));
    }

    /// <summary>
    /// Remove a Product from a Collection. The Product itself is left alone,
    /// only the membership goes away.
    /// </summary>
    /// <param name="value">The Id or Name of the collection</param>
    /// <param name="productValue">The Id or Short of the product</param>
    /// <returns></returns>
    [HttpDelete("{value}/products/{productValue}")]
    [Authorize(Roles = CollectionSecurityRole.AuthorOrEditor + "," +
                       UserSecurityRole.SystemAdministrator)]
    public async Task<IActionResult> RemoveProduct(string value,
        string productValue)
    {
        var r = await UpdateUserAccess();
        if (r != null) return r;

        if (_context.CollectionProducts == null)
            return Ok(Result.Ok("No Data Available"));

        var obj = Collection.ByNameOrId(_context, value);
        if (obj == null) return BadRequest("Not Found");

        var product = Product.ByShortOrId(_context, productValue);
        if (product?.ProductId == null) return BadRequest("Not Found");

        var id = (long)obj.CollectionId!;
        var productId = (long)product.ProductId!;

        var join = _context.CollectionProducts?
            .TagWithSource("Getting collection membership")
            .SingleOrDefault(cp => cp.CollectionId == id &&
                                   cp.ProductId == productId);
        if (join == null) return BadRequest("Not Found");

        _context.CollectionProducts?.Remove(join);
        await _context.SaveChangesAsync();

        // Should rework the save to return a success of fail on the delete
        return Ok(Result.Ok("Delete Successful"));
    }
}
