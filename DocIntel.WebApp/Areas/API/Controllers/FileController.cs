/* DocIntel
 * Copyright (C) 2018-2021 Belgian Defense, Antoine Cailliau
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;

using AutoMapper;

using DocIntel.Core.Exceptions;
using DocIntel.Core.Logging;
using DocIntel.Core.Models;
using DocIntel.Core.Repositories;
using DocIntel.Core.Repositories.Query;
using DocIntel.Core.Settings;
using DocIntel.WebApp.Areas.API.Models;
using DocIntel.WebApp.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace DocIntel.WebApp.Areas.API.Controllers;

/// <summary>
/// Files in DocIntel are the computer files enclosed within a document.
///
/// ## Document Attributes
///
/// | Attribute              | Description                                  |
/// |------------------------|----------------------------------------------|
/// | FileId                 | The file identifier                          |
/// | DocumentId             | The document identifier                      |
/// | Title                  | The tile                                     |
/// | MimeType               | The mime type                                |
/// | FileDate               | The file date                                |
/// | RegistrationDate       | The registration date                        |
/// | ModificationDate       | The modification date                        |
/// | SourceUrl              | The URL at which the document was found      |
/// | OverrideClassification | Whether classification is overriden          |
/// | ClassificationId       | The new classification, if applicable        |
/// | OverrideReleasableTo   | Whether releasable to groups are overriden   |
/// | ReleasableToId         | The new releasable to groups, if applicable  |
/// | OverrideEyesOnly       | Whether the eyes only groups are overriden   |
/// | EyesOnlyId             | The new eyes only groups, if applicable      |
/// | MetaData               | The metadata                                 |
/// | Visible                | Whether the file is visible by default       |
/// | Preview                | Whether the preview is enabled               |
/// | AutoGenerated          | Whether the file was automatically generated |
/// 
/// ## Document Relationships
///
/// | Relationship   | Description                                 |
/// |----------------|---------------------------------------------|
/// | Document       | The associated document                     |
/// | RegisteredBy   | The user who registered the document        |
/// | LastModifiedBy | The last modifier                           |
/// | Classification | The new classification, if applicable       |
/// | ReleasableTo   | The new releasable to groups, if applicable |
/// | EyesOnly       | The new eyes only groups, if applicable     |
/// 
/// </summary>
[Area("API")]
[Route("API/File")]
[ApiController]
public class FileController : DocIntelAPIControllerBase
{
    private readonly IHttpContextAccessor _accessor;
    private readonly ILogger _logger;
    private readonly IMapper _mapper;
    private readonly IDocumentRepository _documentRepository;
    private readonly IGroupRepository _groupRepository;
    private readonly ApplicationSettings _configuration;

    public FileController(UserManager<AppUser> userManager,
        DocIntelContext context,
        ILogger<FileController> logger,
        IHttpContextAccessor accessor,
        IMapper mapper, 
        IDocumentRepository documentRepository, 
        ApplicationSettings configuration, 
        IGroupRepository groupRepository)
        : base(userManager, context)
    {
        _logger = logger;
        _accessor = accessor;
        _mapper = mapper;
        _documentRepository = documentRepository;
        _configuration = configuration;
        _groupRepository = groupRepository;
    }
    
    /// <summary>
    /// Get a file
    /// </summary>
    /// <remarks>
    /// Gets the details of the file.
    ///
    /// For example, with cURL
    ///
    ///     curl --request GET \
    ///       --url http://localhost:5001/API/File/640afad4-0a3d-416a-b6f0-22cb85e0d638 \
    ///       --header 'Authorization: Bearer $TOKEN'
    /// 
    /// </remarks>
    /// <param name="fileId" example="1ee4eac9-6d56-4665-bb78-6986dd6bf7a2">The file identifier</param>
    /// <returns>The file</returns>
    /// <response code="200">Returns the file</response>
    /// <response code="401">Action is not authorized</response>
    /// <response code="404">File does not exists</response>
    [HttpGet("{fileId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(APIFileDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Produces("application/json")]
    public async Task<IActionResult> Details(Guid fileId)
    {
        var currentUser = await GetCurrentUser();
        try
        {
            var file = await _documentRepository.GetFileAsync(AmbientContext, fileId);
            return Ok(_mapper.Map<APIFileDetails>(file));
        }
        catch (UnauthorizedOperationException)
        {
            _logger.Log(LogLevel.Warning,
                EventIDs.DetailsFileFailed,
                new LogEvent(
                        $"User '{currentUser.UserName}' attempted to view details of file '{fileId}' without legitimate rights.")
                    .AddUser(currentUser)
                    .AddHttpContext(_accessor.HttpContext)
                    .AddProperty("file.id", fileId),
                null,
                LogEvent.Formatter);

            return Unauthorized();
        }
        catch (NotFoundEntityException)
        {
            _logger.Log(LogLevel.Warning,
                EventIDs.DetailsFileFailed,
                new LogEvent(
                        $"User '{currentUser.UserName}' attempted to view details of a non-existing file '{fileId}'.")
                    .AddUser(currentUser)
                    .AddHttpContext(_accessor.HttpContext)
                    .AddProperty("file.id", fileId),
                null,
                LogEvent.Formatter);

            return NotFound();
        }
    }
    
    /// <summary>
    /// Download a file
    /// </summary>
    /// <remarks>
    /// Gets the details of the file.
    ///
    /// For example, with cURL
    ///
    ///     curl --request GET \
    ///       --url http://localhost:5001/API/File/640afad4-0a3d-416a-b6f0-22cb85e0d638/Download \
    ///       --header 'Authorization: Bearer $TOKEN'
    /// 
    /// </remarks>
    /// <param name="fileId" example="1ee4eac9-6d56-4665-bb78-6986dd6bf7a2">The file identifier</param>
    /// <returns>The file</returns>
    /// <response code="200">Returns the (binary) file</response>
    /// <response code="401">Action is not authorized</response>
    /// <response code="404">File does not exists</response>
    [HttpGet("{fileId}/Download")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(APIFileDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(Guid fileId)
    {
        var currentUser = await GetCurrentUser();
        try
        {
            var documentFile = await _documentRepository.GetFileAsync(AmbientContext, fileId);
            var mimetype = documentFile.MimeType;

            var filepath = Path.Combine(_configuration.DocFolder, documentFile.Filepath);
            if (!System.IO.File.Exists(filepath))
            {
                _logger.Log(LogLevel.Warning,
                    EventIDs.DownloadFailed,
                    new LogEvent($"User '{currentUser.UserName}' attempted to download non-existing document '{fileId}'.")
                        .AddUser(currentUser)
                        .AddHttpContext(_accessor.HttpContext)
                        .AddProperty("file.id", fileId),
                    null,
                    LogEvent.Formatter);
                
                return NotFound();
            }

            var contentDisposition = new ContentDisposition
            {
                FileName = Path.GetFileName(documentFile.Filename),
                Inline = true
            };
            Response.Headers.Add("Content-Disposition", contentDisposition.ToString());
            Response.Headers.Add("X-Content-Type-Options", "nosniff");
            
            // The stream is disposed by the framework.
            var stream = new FileStream(filepath, FileMode.Open);
            return File(stream, mimetype);
        }
        catch (UnauthorizedOperationException)
        {
            _logger.Log(LogLevel.Warning,
                EventIDs.DownloadFailed,
                new LogEvent(
                        $"User '{currentUser.UserName}' attempted to download '{fileId}' without legitimate rights.")
                    .AddUser(currentUser)
                    .AddHttpContext(_accessor.HttpContext)
                    .AddProperty("file.id", fileId),
                null,
                LogEvent.Formatter);
            return Unauthorized();
        }
        catch (NotFoundEntityException)
        {
            _logger.Log(LogLevel.Warning,
                EventIDs.DownloadFailed,
                new LogEvent($"User '{currentUser.UserName}' attempted to download non-existing document '{fileId}'.")
                    .AddUser(currentUser)
                    .AddHttpContext(_accessor.HttpContext)
                    .AddProperty("file.id", fileId),
                null,
                LogEvent.Formatter);
            return NotFound();
        }
    }
    
    /// <summary>
    /// Get the files
    /// </summary>
    /// <remarks>
    /// Get the facets
    ///
    /// For example, with cURL
    ///
    ///     curl --request GET \
    ///       --url http://localhost:5001/API/Document/1ee4eac9-6d56-4665-bb78-6986dd6bf7a2/Files \
    ///       --header 'Authorization: Bearer $TOKEN'
    ///
    /// </remarks>
    /// <param name="documentId" example="1ee4eac9-6d56-4665-bb78-6986dd6bf7a2">The document identifier</param>
    /// <returns>The files</returns>
    /// <response code="200">Returns the files</response>
    /// <response code="401">Action is not authorized</response>
    /// <response code="404">Document does not exists</response>
    [HttpGet("/API/Document/{documentId}/Files")]
    [ProducesResponseType(StatusCodes.Status200OK, Type=typeof(APIFileDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [Produces("application/json")]
    [SwaggerOperation(Tags=new [] { "File", "Document" })]
    public async Task<IActionResult> GetFiles([FromRoute] Guid documentId)
    {
        var currentUser = await GetCurrentUser();

        try
        {
            var results = await _documentRepository.GetAsync(AmbientContext, documentId, new [] { "Files" });
            
            _logger.Log(LogLevel.Information,
                EventIDs.ListFilesSuccessful,
                new LogEvent(
                        $"User '{currentUser.UserName}' successfully listed files of '{documentId}'.")
                    .AddUser(currentUser)
                    .AddHttpContext(_accessor.HttpContext),
                null,
                LogEvent.Formatter);

                return Ok(_mapper.Map<IEnumerable<APIFileDetails>>(results.Files));
        }
        catch (UnauthorizedOperationException)
        {
            _logger.Log(LogLevel.Warning,
                EventIDs.ListFilesFailed,
                new LogEvent(
                        $"User '{currentUser.UserName}' attempted to list files of '{documentId}' without legitimate rights.")
                    .AddUser(currentUser)
                    .AddHttpContext(_accessor.HttpContext),
                null,
                LogEvent.Formatter);

            return Unauthorized();
        }
        
    }


    /// <summary>
    /// Upload a file
    /// </summary>
    /// <remarks>
    /// Upload a new file to a document.
    ///
    /// For example, with cURL (for a file named `report.pdf`)
    ///
    ///     curl --request POST \
    ///       --url http://localhost:5001/API/Document/08a7474f-1912-4617-9ec4-b0bae39ed84a/Files \
    ///       --header 'Authorization: Bearer $TOKEN' \
    ///       --header 'Content-Type: application/pdf' \
    ///       --data-binary @report.pdf
    /// 
    /// </remarks>
    /// <param name="documentId" example="1ee4eac9-6d56-4665-bb78-6986dd6bf7a2">The document identifier</param>
    /// <returns>The uploaded file, as recorded</returns>
    /// <response code="200">Returns the newly uploaded file</response>
    /// <response code="401">Action is not authorized</response>
    /// <response code="400">Submitted value is invalid</response>
    [HttpPost("/API/Document/{documentId}/Files")]
    [ProducesResponseType(StatusCodes.Status200OK, Type=typeof(APIFileDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [Produces("application/json")]
    [SwaggerOperation(Tags=new [] { "File", "Document" })]
    public async Task<IActionResult> Create([FromRoute] Guid documentId)
    {
        var currentUser = await GetCurrentUser();

        try
        {
            if (Request.ContentLength > 0)
            {
                var document = await _documentRepository.GetAsync(AmbientContext, documentId);
                var submittedFile = new DocumentFile()
                {
                    Title = "test",
                    DocumentId = document.DocumentId,
                    Document = document,
                    Visible = true,
                    Preview = true
                };

                // Request.Body does not allow to get length and/or rewind. Copy to memory is easier, but might not be
                // necessary. To be investigated.
                using var sr = new MemoryStream();
                await Request.Body.CopyToAsync(sr);
                var result = await _documentRepository.AddFile(AmbientContext, submittedFile, sr,
                    document.ReleasableTo.ToHashSet(), document.EyesOnly.ToHashSet());

                await AmbientContext.DatabaseContext.SaveChangesAsync();

                _logger.Log(LogLevel.Information,
                    EventIDs.CreateFileSuccessful,
                    new LogEvent(
                            $"User '{currentUser.UserName}' successfully uploaded a new file '{result.FileId}' for '{documentId}'.")
                        .AddUser(currentUser)
                        .AddHttpContext(_accessor.HttpContext)
                        .AddFile(result),
                    null,
                    LogEvent.Formatter);

                return Ok(_mapper.Map<APIFileDetails>(result));
            }

            _logger.Log(LogLevel.Information,
                EventIDs.CreateFileFailed,
                new LogEvent(
                        $"User '{currentUser.UserName}' attempted to upload an empty file for '{documentId}'.")
                    .AddUser(currentUser)
                    .AddHttpContext(_accessor.HttpContext),
                null,
                LogEvent.Formatter);

            return BadRequest(ModelState);
        }
        catch (UnauthorizedOperationException)
        {
            _logger.Log(LogLevel.Warning,
                EventIDs.CreateFileFailed,
                new LogEvent(
                        $"User '{currentUser.UserName}' attempted to upload a new file for '{documentId}' without legitimate rights.")
                    .AddUser(currentUser)
                    .AddHttpContext(_accessor.HttpContext),
                null,
                LogEvent.Formatter);

            return Unauthorized();
        }
        catch (InvalidArgumentException e)
        {
            ModelState.Clear();
            foreach (var kv in e.Errors)
            foreach (var errorMessage in kv.Value)
                ModelState.AddModelError(kv.Key, errorMessage);

            _logger.Log(LogLevel.Information,
                EventIDs.CreateFileFailed,
                new LogEvent($"User '{currentUser.UserName}' attempted to upload a new file with an invalid model.")
                    .AddUser(currentUser)
                    .AddHttpContext(_accessor.HttpContext),
                null,
                LogEvent.Formatter);

            return BadRequest(ModelState);
        }
    }
    
    /// <summary>
    /// Update a file
    /// </summary>
    /// <remarks>
    /// Updates a file
    ///
    /// For example, with cURL
    ///
    ///     curl --request PATCH \
    ///       --url http://localhost:5001/API/File/21b6ff94-fed8-4569-9d03-352941bdd59d \
    ///       --header 'Authorization: Bearer $TOKEN' \
    ///       --header 'Content-Type: application/json' \
    ///       --data '{
    ///     	"title": "My custom report 2"
    ///     }'
    /// 
    /// </remarks>
    /// <param name="fileId" example="640afad4-0a3d-416a-b6f0-22cb85e0d638">The file identifier</param>
    /// <param name="submittedFile">The updated file</param>
    /// <returns>The updated file</returns>
    /// <response code="200">Returns the newly updated file</response>
    /// <response code="401">Action is not authorized</response>
    /// <response code="404">The file does not exist</response>
    [HttpPatch("{fileId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type=typeof(APIFileDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Produces("application/json")]
    public async Task<IActionResult> Edit([FromRoute] Guid fileId, [FromBody] APIFile submittedFile)
    {
        var currentUser = await GetCurrentUser();

        try
        {
            var file = await _documentRepository.GetFileAsync(AmbientContext, fileId);
            
            if (ModelState.IsValid)
            {
                file = _mapper.Map(submittedFile, file);
                
                var defaultGroup = _groupRepository.GetDefaultGroups(AmbientContext).Select(g => g.GroupId).ToArray();

                if (submittedFile.ReleasableToId != null)
                {
                    var filteredRelTo = (ISet<Group>) await _groupRepository
                        .GetAllAsync(AmbientContext, new GroupQuery {Id = submittedFile.ReleasableToId.ToArray()})
                        .Where(_ => !defaultGroup.Contains(_.GroupId)).ToHashSetAsync();
                    file.ReleasableTo = filteredRelTo;
                }

                if (submittedFile.EyesOnlyId != null)
                {
                    var filteredEyes = (ISet<Group>)await _groupRepository
                        .GetAllAsync(AmbientContext, new GroupQuery { Id = submittedFile.EyesOnlyId.ToArray() })
                        .ToHashSetAsync();
                    file.EyesOnly = filteredEyes;
                }

                file = await _documentRepository.UpdateFile(AmbientContext, file);
                await _context.SaveChangesAsync();

                _logger.Log(LogLevel.Information,
                    EventIDs.UpdateFileSuccessful,
                    new LogEvent($"User '{currentUser.UserName}' successfully edit file '{file.Title}'.")
                        .AddUser(currentUser)
                        .AddHttpContext(_accessor.HttpContext)
                        .AddFile(file),
                    null,
                    LogEvent.Formatter);

                return Ok(_mapper.Map<APIFile>(file));
            }

            throw new InvalidArgumentException(ModelState);
        }
        catch (UnauthorizedOperationException)
        {
            _logger.Log(LogLevel.Warning,
                EventIDs.UpdateFileFailed,
                new LogEvent(
                        $"User '{currentUser.UserName}' attempted to edit file '{fileId}' without legitimate rights.")
                    .AddUser(currentUser)
                    .AddHttpContext(_accessor.HttpContext)
                    .AddProperty("file.id", fileId),
                null,
                LogEvent.Formatter);

            return Unauthorized();
        }
        catch (NotFoundEntityException)
        {
            _logger.Log(LogLevel.Warning,
                EventIDs.UpdateFileFailed,
                new LogEvent(
                        $"User '{currentUser.UserName}' attempted to edit a non-existing file '{fileId}'.")
                    .AddUser(currentUser)
                    .AddHttpContext(_accessor.HttpContext)
                    .AddProperty("file.id", fileId),
                null,
                LogEvent.Formatter);

            return NotFound();
        }
        catch (InvalidArgumentException e)
        {
            ModelState.Clear();
            foreach (var kv in e.Errors)
            foreach (var errorMessage in kv.Value)
                ModelState.AddModelError(kv.Key, errorMessage);

            _logger.Log(LogLevel.Information,
                EventIDs.UpdateFileFailed,
                new LogEvent(
                        $"User '{currentUser.UserName}' attempted to edit file '{fileId}' with an invalid model.")
                    .AddUser(currentUser)
                    .AddHttpContext(_accessor.HttpContext)
                    .AddProperty("file.id", fileId),
                null,
                LogEvent.Formatter);

            return BadRequest(ModelState);
        }
    }
    
    /// <summary>
    /// Deletes a file
    /// </summary>
    /// <remarks>
    /// Deletes the specified file,
    ///
    /// For example, with cURL
    ///
    ///     curl --request DELETE \
    ///       --url http://localhost:5001/API/File/6e7635a0-27bb-495d-a218-15b54cb938fd \
    ///       --header 'Authorization: Bearer $TOKEN'
    ///
    /// </remarks>
    /// <param name="fileId" example="6e7635a0-27bb-495d-a218-15b54cb938fd">The file identifier</param>
    /// <response code="200">The file is deleted</response>
    /// <response code="401">Action is not authorized</response>
    /// <response code="404">The file does not exist</response>
    [HttpDelete("{fileId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Produces("application/json")]
    public async Task<IActionResult> Delete(Guid fileId)
    {
        var currentUser = await GetCurrentUser();
        try
        {
            await _documentRepository.DeleteFile(AmbientContext, fileId);
            await _context.SaveChangesAsync();

            _logger.Log(LogLevel.Information,
                EventIDs.DeleteFileSuccessful,
                new LogEvent($"User '{currentUser.UserName}' successfully deleted file '{fileId}'.")
                    .AddUser(currentUser)
                    .AddHttpContext(_accessor.HttpContext)
                    .AddProperty("file.id", fileId),
                null,
                LogEvent.Formatter);

            return Ok();
        }
        catch (UnauthorizedOperationException)
        {
            _logger.Log(LogLevel.Warning,
                EventIDs.DeleteFileFailed,
                new LogEvent(
                        $"User '{currentUser.UserName}' attempted to delete a new file '{fileId}' without legitimate rights.")
                    .AddUser(currentUser)
                    .AddHttpContext(_accessor.HttpContext)
                    .AddProperty("file.id", fileId),
                null,
                LogEvent.Formatter);

            return Unauthorized();
        }
        catch (NotFoundEntityException)
        {
            _logger.Log(LogLevel.Warning,
                EventIDs.DeleteFileFailed,
                new LogEvent($"User '{currentUser.UserName}' attempted to delete a non-existing file '{fileId}'.")
                    .AddUser(currentUser)
                    .AddHttpContext(_accessor.HttpContext)
                    .AddProperty("file.id", fileId),
                null,
                LogEvent.Formatter);

            return NotFound();
        }
    }
}