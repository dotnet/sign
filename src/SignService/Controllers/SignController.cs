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
using Microsoft.Extensions.Options;

namespace SignService.Controllers
{
    [Authorize]
    [RequireHttps]
    [Route("[controller]")]
    public class SignController : Controller
    {
        readonly ISigningToolAggregate codeSignAggregate;
        readonly IOptionsSnapshot<Settings> settings;
        readonly ILogger<SignController> logger;



        public SignController(ISigningToolAggregate codeSignAggregate, IOptionsSnapshot<Settings> settings, ILogger<SignController> logger)
        {
            this.codeSignAggregate = codeSignAggregate;
            this.settings = settings;
            this.logger = logger;
        }



        [HttpPost("singleFile")]
        public async Task<IActionResult> SignSingleFile(IFormFile source, HashMode hashMode, string name, string description, string descriptionUrl)
        {
            return await SignFile(source, null, hashMode, name, description, descriptionUrl);
        }

        [HttpPost]
        public async Task<IActionResult> SignFile(IFormFile source, IFormFile filelist, HashMode hashMode, string name, string description, string descriptionUrl)
        {
            if (source == null)
            {
                return BadRequest();
            }

            // If we're in Key Vault enabled mode, don't allow dual since SHA-1 isn't supported
            if (settings.Value.CertificateInfo.UseKeyVault)
            {
                if (hashMode == HashMode.Sha1 || hashMode == HashMode.Dual)
                {
                    ModelState.AddModelError(nameof(hashMode), "Azure Key Vault does not support SHA-1. Use sha256");
                    return BadRequest(ModelState);
                }
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

                // This will block until it's done
                await codeSignAggregate.Submit(hashMode, name, description, descriptionUrl, new[] { inputFileName }, filter);

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

        [HttpPost("zipFile")]
        public async Task<IActionResult> SignZipFile(IFormFile source, IFormFile filelist, HashMode hashMode, string name, string description, string descriptionUrl)
        {
            return await SignFile(source, filelist, hashMode, name, description, descriptionUrl);
        }
    }
}