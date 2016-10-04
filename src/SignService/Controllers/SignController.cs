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


            var inputDir = Path.Combine(dataDir, "input");
            var outputDir = Path.Combine(dataDir, "output");


            Directory.CreateDirectory(inputDir);
            Directory.CreateDirectory(outputDir);

            // this might have two files, one containing the file list
            // The first will be the package and the second is the filter


            

            var inputFileName = Path.Combine(dataDir, source.FileName);
            var outputFilename = Path.Combine(dataDir, source.FileName + "-s.zip");
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

                // Build an exclude list based on the output path
                var filterSet = new HashSet<string>(filter.Split('\n').Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => Path.Combine(outputDir, s)), StringComparer.OrdinalIgnoreCase);

                // Do work and then load the file into memory so we can delete it before the response is complete

                logger.LogInformation($"Extracting zip file {inputFileName}");
                ZipFile.ExtractToDirectory(inputFileName, outputDir);


                var filesInDir = Directory.EnumerateFiles(outputDir, "*.*", SearchOption.AllDirectories);
                if (filterSet.Count > 0)
                    filesInDir = filesInDir.Intersect(filterSet, StringComparer.OrdinalIgnoreCase);

                var filesToSign = filesInDir.ToList();
                
                // This will block until it's done
                await codeSignService.Submit(hashMode, name, description, descriptionUrl, filesToSign); 
               

                // They were signed in-place, now zip them back up
                logger.LogInformation($"Building signed {inputFileName}");

                ZipFile.CreateFromDirectory(outputDir, outputFilename, CompressionLevel.Optimal, false);

                var fi = new FileInfo(outputFilename);
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