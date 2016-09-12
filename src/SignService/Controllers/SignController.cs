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

namespace SignService.Controllers
{
    [Authorize]
    [RequireHttps]
    [Route("[controller]")]
    public class SignController : Controller
    {
        readonly ICodeSignService codeSignService;
        readonly ILogger<SignController> logger;

        readonly string ziptoolPath;

        public SignController(IHostingEnvironment environment, ICodeSignService codeSignService, ILogger<SignController> logger)
        {
            this.codeSignService = codeSignService;
            this.logger = logger;
            ziptoolPath = Path.Combine(environment.ContentRootPath, "tools\\7za.exe");
        }



        [HttpPost("singleFile")]
        public async Task<IActionResult> SignSingleFile(IFormFile source, string name, string description, string descriptionUrl)
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

                codeSignService.Submit(name, description, descriptionUrl, new[] {fileName});


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
        public async Task<IActionResult> SignZipFile(IFormFile source, string name, string description, string descriptionUrl)
        {
            var dataDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());


            var inputDir = Path.Combine(dataDir, "input");
            var outputDir = Path.Combine(dataDir, "output");


            Directory.CreateDirectory(inputDir);
            Directory.CreateDirectory(outputDir);

            var inputFileName = Path.Combine(dataDir, source.FileName);
            var outputFilename = Path.Combine(dataDir, source.FileName + "-signed");
            try
            {
                if (source.Length > 0)
                {
                    using (var fs = new FileStream(inputFileName, FileMode.Create))
                    {
                        await source.CopyToAsync(fs);
                    }
                }

                // Do work and then load the file into memory so we can delete it before the response is complete

                logger.LogInformation($"Extracting zip file {inputFileName}");
                ZipFile.ExtractToDirectory(inputFileName, outputDir);

                var filesToSign = Directory.EnumerateFiles(outputDir, "*.*", SearchOption.AllDirectories)
                                           .Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                                                       f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                                           .ToList();

                
                // This will block until it's done
                codeSignService.Submit(name, description, descriptionUrl, filesToSign); 
               

                // They were signed in-place, now zip them back up
                // We need to use 7-Zip because the Fx zip doesn't create valid nuget archives

                logger.LogInformation($"Building signed {inputFileName}");

                //ZipFile giving a strange bug - shell out to 7z for now
                //ZipFile.CreateFromDirectory(target, signedPackageFile);
                // Hack in 7z call
                var zip = new Process
                {
                    StartInfo =
                    {
                        WorkingDirectory = outputDir,
                        FileName = ziptoolPath,
                        UseShellExecute = false,
                        RedirectStandardError = false,
                        RedirectStandardOutput = false,
                        Arguments = string.Format(@"a -tzip -r ""{0}"" ""{1}\*.*""", outputFilename, outputDir)
                    }
                };

                logger.LogInformation(@"""{0}"" {1}", zip.StartInfo.FileName, zip.StartInfo.Arguments);

                zip.Start();
                zip.WaitForExit();
                if (zip.ExitCode != 0)
                {
                    logger.LogError("Error: 7z returned {0}", zip.ExitCode);
                }
                zip.Dispose();


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