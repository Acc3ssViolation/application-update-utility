using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ApplicationUpdateServer.Controllers
{
    [Route("api/v1/download")]
    [ApiController]
    public class FileController : ControllerBase
    {
        [HttpGet("{*path}")]
        public async Task<IActionResult> GetFile(string path, [FromServices] IWebHostEnvironment environment)
        {
            var root = environment.ContentRootPath;
            var fullPath = Path.Combine(root, path);

            if (Path.GetRelativePath(root, fullPath) == fullPath)
            {
                return NotFound();
            }

            if (System.IO.File.Exists(fullPath))
            {
                if (!new FileExtensionContentTypeProvider().TryGetContentType(fullPath, out var contentType))
                {
                    contentType = "application/octet-stream";
                }

                var fileStream = System.IO.File.OpenRead(fullPath);
                return File(fileStream, contentType);
            }

            return NotFound();
        }
    }
}
