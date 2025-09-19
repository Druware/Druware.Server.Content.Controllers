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

using Druware.Server.Content.Entities;
using Druware.Server.Entities;
using Microsoft.AspNetCore.Authorization;
using RESTfulFoundation.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;

// TODO: Add a search/query function

namespace Druware.Server.Content.Controllers
{
    /// <summary>
    /// The Asset Controller handles all of the heavy lifting for the Articles
    /// and Asset Feed bits. An Article will support being Tagged using the
    /// generic tag pool from Druware.Server.
    /// </summary>
    [Route("[controller]")]
    [Route("api/[controller]")]
    public class AssetController : CustomController
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
        public AssetController(
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
        /// limited to the parameters passed on the QueryString
        /// </summary>
        /// <param name="page">Which 0 based page to fetch</param>
        /// <param name="count">Limit the items per page</param>
        /// <returns>A ListResult containing the resulting list</returns>
        [HttpGet("")]
        public async Task<IActionResult> GetList([FromQuery] int page = 0, [FromQuery] int count = 10)
        {
            // Everyone has access to this method, but we still want to log it
            await LogRequest();

            if (_context.Assets == null) return Ok(Result.Ok("No Data Available"));

            var total = _context.Assets?.Count() ?? 0;
            var list = _context.Assets?
                .OrderBy(x => x.FileName)
                .Select(x => new
                {
                    x.AssetId, x.TypeId, x.Description, x.MediaType, x.FileName
                })
                .TagWithSource("Getting Assets")
                .Skip(page * count)
                .Take(count)
                .ToList();
            var result = ListResult.Ok(list!, total, page, count);
            return Ok(result);
        }

        /// <summary>
        /// Get a discrete Asset item, either by Id or FileName
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        [HttpGet("{value}")]
        public async Task<IActionResult?> Get(string value)
        {
            // Everyone has access to this method, but we still want to log it
            await LogRequest();

            var r = Asset.ByFileNameOrId(_context, value);
            
            return (r != null) ? File(r.Content,  r.MediaType) 
                : BadRequest("File Not Found");
        }
        
        /// <summary>
        /// Get a discrete Asset item, either by Id or FileName
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        [HttpGet("{value}/detail")]
        public async Task<IActionResult?> Detail(string value)
        {
            // Everyone has access to this method, but we still want to log it
            await LogRequest();

            var r = Asset.ByFileNameOrId(_context, value);
            
            return (r != null) ? Ok(r) : BadRequest("File Not Found");
        }

        /// <summary>
        /// Add an Article to the Asset Library
        /// </summary>
        /// <param name="typeId"></param>
        /// <param name="description"></param>
        /// <param name="content"></param>
        /// <param name="mediaType"></param>
        /// <param name="fileName"></param>
        /// <returns></returns>
        [HttpPost("")]
        [Consumes("multipart/form-data")]
        [Authorize(Roles = AssetSecurityRole.AuthorOrEditor + "," + UserSecurityRole.SystemAdministrator)]
        public async Task<ActionResult<Asset>> Add(
            [FromForm] int typeId, 
            [FromForm] string description,
            [FromForm] IFormFile content, 
            [FromForm] string? mediaType, 
            [FromForm] string? fileName 
            )
        {
            var r = await UpdateUserAccess();
            if (r != null) return r;

            var asset = new Asset()
            {
                TypeId = typeId,
                Description = description
            };
            
            // now make some decisions
            asset.FileName = fileName ?? content.FileName;
            asset.MediaType = mediaType ?? content.ContentType;
            
            // now read the content to a byte[] into the asset.content value
            long length = content.Length;
            if (length < 0)
                return BadRequest();

            await using var fileStream = content.OpenReadStream();
            var bytes = new byte[length];
            if (fileStream != null)
                _ = await fileStream.ReadAsync(bytes.AsMemory(0, (int)content.Length));
            asset.Content = bytes;

            if (!Asset.IsFileNameAvailable(_context, asset.FileName!))
                return Ok(Result.Error("FileName cannot duplicate an existing file"));

            _context.Assets?.Add(asset);
            await _context.SaveChangesAsync();
            
            // clear the content, because we do not want to return it.
            asset.Content = null;
            
            return Ok(asset);
        }

        /// <summary>
        /// Update an existing article within the new library
        /// </summary>
        /// <param name="value">The Id or Permalink of the article to update</param>
        /// <param name="typeId"></param>
        /// <param name="description"></param>
        /// <param name="content"></param>
        /// <param name="mediaType"></param>
        /// <param name="fileName"></param>
        /// <returns></returns>
        [HttpPut("{value}")]
        [Consumes("multipart/form-data")]
        [Authorize(Roles = AssetSecurityRole.AuthorOrEditor + "," + UserSecurityRole.SystemAdministrator)]
        public async Task<ActionResult<Article>> Update(  
                string value, 
               [FromForm] int typeId, 
               [FromForm] string description,
               [FromForm] IFormFile content, 
               [FromForm] string? mediaType, 
               [FromForm] string? fileName 
        )
        {
            var r = await UpdateUserAccess();
            if (r != null) return r;

            var asset = Asset.ByFileNameOrId(_context, value);
            if (asset == null) return BadRequest("File Not Found");

            asset.TypeId = typeId;
            asset.Description = description;
            asset.FileName = fileName ?? content.FileName;
            asset.MediaType = mediaType ?? content.ContentType;
            
            // now read the content to a byte[] into the asset.content value
            var length = content.Length;
            if (length < 0)
                return BadRequest();

            await using var fileStream = content.OpenReadStream();
            var bytes = new byte[length];
            if (fileStream != null)
                _ = await fileStream.ReadAsync(bytes.AsMemory(0, (int)content.Length));
            asset.Content = bytes;

            if (asset.FileName != value) 
                if (!Asset.IsFileNameAvailable(_context, asset.FileName!))
                    return Ok(Result.Error("FileName cannot duplicate an existing file"));

            _context.Assets?.Update(asset);
            await _context.SaveChangesAsync();
            
            // clear the content, because we do not want to return it.
            asset.Content = null;
            
            return Ok(asset); 
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
        [Authorize(Roles = AssetSecurityRole.Editor + "," + UserSecurityRole.SystemAdministrator)]
        public async Task<IActionResult> Remove(string value)
        {
            var r = await UpdateUserAccess();
            if (r != null) return r;

            var asset = Asset.ByFileNameOrId(_context, value);
            if (asset == null) return BadRequest("Not Found");

            _context.Assets?.Remove(asset);
            await _context.SaveChangesAsync();

            // Should rework the save to return a success of fail on the delete
            return Ok(Result.Ok("Delete Successful"));
        }

    }

}

