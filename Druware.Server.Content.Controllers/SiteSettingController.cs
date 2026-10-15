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
/// The SiteSetting Controller handles the key/value settings a site reads at
/// start up, such as social links and document pointers. Every row is
/// publicly readable, so nothing secret belongs here. The key is fixed once
/// the setting is created; only the value and the description change.
/// </summary>
[Route("api/[controller]")]
[Route("[controller]")]
public class SiteSettingController : CustomController
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
    public SiteSettingController(
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
    /// Get a list of the settings, in ascending key order, limited to the
    /// parameters passed on the QueryString
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

        if (_context.SiteSettings == null)
            return Ok(Result.Ok("No Data Available"));

        var total = _context.SiteSettings.Count();
        var list = _context.SiteSettings
            .OrderBy(s => s.Key)
            .TagWithSource("Getting Site Settings")
            .Skip(page * count)
            .Take(count)
            .ToList();
        return Ok(ListResult.Ok(list, total, page, count));
    }

    /// <summary>
    /// Get a discrete SiteSetting, either by Id or Key
    /// </summary>
    /// <param name="value">The Id or Key of the setting</param>
    /// <returns></returns>
    [HttpGet("{value}")]
    public async Task<IActionResult> Get(string value)
    {
        // Everyone has access to this method, but we still want to log it
        await LogRequest();

        var r = SiteSetting.ByKeyOrId(_context, value);
        if (r == null) return BadRequest("Not Found");

        return Ok(r);
    }

    /// <summary>
    /// Add a SiteSetting
    /// </summary>
    /// <param name="model">The setting to add</param>
    /// <returns></returns>
    [HttpPost("")]
    [Authorize(Roles = SiteContentSecurityRole.AuthorOrEditor + "," +
                       UserSecurityRole.SystemAdministrator)]
    public async Task<ActionResult<SiteSetting>> Add(
        [FromBody] SiteSetting model)
    {
        var r = await UpdateUserAccess();
        if (r != null) return r;

        // a body that does not bind at all arrives as null. Anything that
        // did bind is checked by SiteSetting.Validate below, which gives a
        // readable message where ModelState would not.
        if (model == null) return BadRequest("Invalid Model Received");

        if (_context.SiteSettings == null)
            return BadRequest("No Data Available");

        var message = SiteSetting.Validate(model);
        if (message != null) return BadRequest(message);

        // the key is unique, so a duplicate is refused here rather than left
        // to throw on the save
        if (SiteSetting.ByKeyOrId(_context, model.Key) != null)
            return BadRequest(
                $"A setting with the key {model.Key} already exists");

        // the id is assigned by the database, never by the caller
        model.SettingId = null;
        model.Updated = DateTime.Now;

        _context.SiteSettings.Add(model);
        var i = await _context.SaveChangesAsync();
        if (i == 0)
        {
            return BadRequest("Save Failed");
        }

        return Ok(model);
    }

    /// <summary>
    /// Update an existing SiteSetting. Only the Value and the Description
    /// change; the key in the route identifies the setting and any key in
    /// the body is ignored.
    /// </summary>
    /// <param name="value">The Id or Key of the setting to update</param>
    /// <param name="model">The updated setting</param>
    /// <returns></returns>
    [HttpPut("{value}")]
    [Authorize(Roles = SiteContentSecurityRole.AuthorOrEditor + "," +
                       UserSecurityRole.SystemAdministrator)]
    public async Task<ActionResult<SiteSetting>> Update(
        string value, [FromBody] SiteSetting model)
    {
        var r = await UpdateUserAccess();
        if (r != null) return r;

        // find the setting
        var obj = SiteSetting.ByKeyOrId(_context, value);
        if (obj == null) return BadRequest("Not Found");

        if (model == null) return BadRequest("Invalid Model Received");

        // the key is not copied, so it is not checked either - only the
        // description has a rule that applies here
        if (model.Description?.Length > SiteSetting.DescriptionMaxLength)
            return BadRequest("Description must be at most " +
                              $"{SiteSetting.DescriptionMaxLength} characters");

        obj.Value = model.Value;
        obj.Description = model.Description;
        obj.Updated = DateTime.Now;

        await _context.SaveChangesAsync();

        // return the entity that was saved
        return Ok(obj);
    }

    /// <summary>
    /// Remove an existing SiteSetting.
    ///
    /// This action is limited to Editors, or System Administrators
    /// </summary>
    /// <param name="value">The Id or Key of the setting to remove</param>
    /// <returns></returns>
    [HttpDelete("{value}")]
    [Authorize(Roles = SiteContentSecurityRole.Editor + "," +
                       UserSecurityRole.SystemAdministrator)]
    public async Task<IActionResult> Remove(string value)
    {
        var r = await UpdateUserAccess();
        if (r != null) return r;

        var obj = SiteSetting.ByKeyOrId(_context, value);
        if (obj == null) return BadRequest("Not Found");

        _context.SiteSettings?.Remove(obj);
        await _context.SaveChangesAsync();

        // Should rework the save to return a success of fail on the delete
        return Ok(Result.Ok("Delete Successful"));
    }
}
