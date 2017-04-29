using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SignService.SigningTools;

namespace SignService.Controllers
{
    [Authorize]
    [RequireHttps]
    [Route("[controller]")]
    public class SignController : Controller
    {
        readonly ISigningToolAggregate codeSignService;
        readonly ILogger<SignController> logger;



        public SignController(ISigningToolAggregate codeSignService, ILogger<SignController> logger)
        {
            this.codeSignService = codeSignService;
            this.logger = logger;
        }



        [HttpPost("singleFile")]
        public async Task<IActionResult> SignSingleFile(IFormFile source, HashMode hashMode, string name, string description, string descriptionUrl)
        {
            var dataDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            Directory.CreateDirectory(dataDir);
            var fileName = Path.Combine(dataDir, source.FileName);
            try
            {
                if (source.Length > 0)
                {
                    using (var fs = new FileStream(fileName, FileMode.Create))
                    {
                        await source.CopyToAsync(fs);
                    }
                }

                // Do work and then load the file into memory so we can delete it before the response is complete
                var fi = new FileInfo(fileName);

                await codeSignService.Submit(hashMode, name, description, descriptionUrl, new[] {fileName});


                byte[] buffer;
                using (var ms = new MemoryStream(new byte[fi.Length]))
                {
                    using (var fs = fi.OpenRead())
                    {
                        await fs.CopyToAsync(ms);
                    }

                    buffer = ms.ToArray();
                }

                return File(buffer, "application/octet-stream", source.FileName);
            }
            finally
            {
                Directory.Delete(dataDir, true);
            }
        }

        [HttpPost("zipFile")]
        public async Task<IActionResult> SignZipFile(IFormFile source, IFormFile filelist, HashMode hashMode, string name, string description, string descriptionUrl)
        {
            if (source == null)
            {
                return BadRequest();
            }

            var dataDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dataDir);

            // this might have two files, one containing the file list
            // The first will be the package and the second is the filter

            var inputFileName = Path.Combine(dataDir, source.FileName);
            try
            {
                if (source.Length > 0)
                {
                    using (var fs = new FileStream(inputFileName, FileMode.Create))
                    {
                        await source.CopyToAsync(fs);
                    }
                }

                var filter = string.Empty;
                if (filelist != null)
                {
                    using (var sr = new StreamReader(filelist.OpenReadStream()))
                    {
                        filter = await sr.ReadToEndAsync();
                        filter = filter.Replace("\r\n", "\n").Replace("/", "\\").Trim();
                    }
                }

                // Do work and then load the file into memory so we can delete it before the response is complete
                using (var zipFile = new TemporaryZipFile(inputFileName, filter, logger))
                {
                    // This will block until it's done
                    await codeSignService.Submit(hashMode, name, description, descriptionUrl, zipFile.FilteredFilesInDirectory); 
                    zipFile.Save();
                }

                var fi = new FileInfo(inputFileName);
                byte[] buffer;
                using (var ms = new MemoryStream(new byte[fi.Length]))
                {
                    using (var fs = fi.OpenRead())
                    {
                        await fs.CopyToAsync(ms);
                    }

                    buffer = ms.ToArray();
                }

                // Send it back with the original file name
                return File(buffer, "application/octet-stream", source.FileName);
            }
            finally
            {
                Directory.Delete(dataDir, true);
            }
        }
    }
}