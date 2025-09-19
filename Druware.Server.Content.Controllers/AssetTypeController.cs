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
 * Copyright 2019-2024 by:
 *    Andy 'Dru' Satori @ Druware Software Designs
 *    All Rights Reserved
 */

using Druware.Server.Content.Entities;
using Druware.Server.Entities;
using RESTfulFoundation.Server;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Druware.Server.Content.Controllers
{
    /// <summary>
    /// AssetTypeController provides the foundation AssetType pool for ALL objects within
    /// the various Druware.Server.* libraries.
    /// </summary>
    [Route("api/[controller]")]
    [Route("[controller]")]
    public class AssetTypeController : CustomController
    {
        private readonly ContentContext _context;

        public AssetTypeController(
            IConfiguration configuration,
            UserManager<User> userManager,
            SignInManager<User> signInManager,
            ServerContext serverContext,
            ContentContext context)
            : base(configuration, userManager, signInManager, serverContext)
        {
            _context = context;
        }

        // get - get a list of all the article, with count and offset built in
        [HttpGet("")]
        public IActionResult GetList([FromQuery] int page = 0, [FromQuery] int count = 1000)
        {
            if (_context.AssetTypes == null)
                return BadRequest("Context is invalid");
            
            var total = _context.AssetTypes.Count();
            var list = _context.AssetTypes
                .OrderBy(a => a.Description)
                .Skip(page * count)
                .Take(count)
                .ToList();
            return Ok(ListResult.Ok(list, total, 0, 1000));
        }

        [HttpGet("{value}")]
        public IActionResult GetAssetType(int value) =>
            Ok(AssetType.ById(_context, value));
        
        [HttpPost("")]
        [Authorize(Roles = UserSecurityRole.ManagerOrSystemAdministrator)]
        public async Task<ActionResult<AssetType>> Add(
            [FromBody] AssetType model)
        {
            var r = await UpdateUserAccess();
            if (r != null) return r;

            if (!ModelState.IsValid)
                return Ok(Result.Error("Invalid Model Received"));

            _context.AssetTypes?.Add(model);
            await _context.SaveChangesAsync();

            return Ok(model);
        }

        [HttpDelete("{value}")]
        [Authorize(Roles = UserSecurityRole.ManagerOrSystemAdministrator)]
        public async Task<IActionResult> DeleteObject(int value)
        {
            var r = await UpdateUserAccess();
            if (r != null) return r;

            var tag = AssetType.ById(_context, value);

            _context.AssetTypes?.Remove(tag);
            await _context.SaveChangesAsync();

            // Should rework the save to return a success of fail on the delete
            return Ok(Result.Ok("Delete Successful"));
        }
    }
}

