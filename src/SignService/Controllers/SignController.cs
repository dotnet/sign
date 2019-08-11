using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.Extensions.Logging;
using SignService.SigningTools;
using SignService.Utils;

namespace SignService.Controllers
{
    [Authorize(AuthenticationSchemes = "AzureADJwtBearer")]
    [RequireHttps]
    [Route("[controller]")]
    public class SignController : Controller
    {
        readonly ISigningToolAggregate codeSignAggregate;
        readonly ILogger<SignController> logger;

        public SignController(ISigningToolAggregate codeSignAggregate, ILogger<SignController> logger)
        {
            this.codeSignAggregate = codeSignAggregate;
            this.logger = logger;
        }

        [HttpPost]
        [RequestFormLimits(MultipartBodyLengthLimit = 4294967295)]
        [RequestSizeLimit(4294967295)]       
        
        public async Task<IActionResult> SignFile(IFormFile source, IFormFile filelist, HashMode hashMode, string name, string description, string descriptionUrl)
        {
            if (source == null)
            {
                return BadRequest();
            }

            // If we're in Key Vault enabled mode, don't allow dual since SHA-1 isn't supported
            if (hashMode == HashMode.Sha1 || hashMode == HashMode.Dual)
            {
                ModelState.AddModelError(nameof(hashMode), "Azure Key Vault does not support SHA-1. Use sha256");
                return BadRequest(ModelState);
            }

            var dataDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dataDir);

            // this might have two files, one containing the file list
            // The first will be the package and the second is the filter

            var inputFileName = Path.Combine(dataDir, source.FileName);
            var sendingResponse = false;
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
                        filter = filter.Replace("\r\n", "\n").Trim();
                    }
                }

                // This will block until it's done
                await codeSignAggregate.Submit(hashMode, name, description, descriptionUrl, new[] { inputFileName }, filter);            

                Response.OnCompleted(() =>
                {
                    DirectoryUtility.SafeDelete(dataDir);
                    return Task.CompletedTask;
                });

                sendingResponse = true;
                // Send it back with the original file name
                return PhysicalFile(inputFileName, "application/octet-stream", source.FileName);
            }
            finally
            {
                // There was some other error, make sure to clean up
                if(!sendingResponse)
                {
                    DirectoryUtility.SafeDelete(dataDir);
                }                
            }
        }
    }
}
