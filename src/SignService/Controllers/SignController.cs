using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
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
        readonly ILogger logger;

        public SignController(ISigningToolAggregate codeSignAggregate, ILogger<SignController> logger)
        {
            this.codeSignAggregate = codeSignAggregate;
            this.logger = logger;
        }

        [HttpPost]
        [RequestFormLimits(MultipartBodyLengthLimit = 4294967295)]
        [RequestSizeLimit(4294967295)]       
        
        public async Task SignFile(IFormFile source, IFormFile filelist, HashMode hashMode, string name, string description, string descriptionUrl)
        {
            if (source == null)
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            // If we're in Key Vault enabled mode, don't allow dual since SHA-1 isn't supported
            if (hashMode == HashMode.Sha1 || hashMode == HashMode.Dual)
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                if (Request.Protocol != "HTTP/2")
                {
                    // HTTP/2 no longer has reason phrases, so no point in setting it
                    ModelState.AddModelError(nameof(hashMode), "Azure Key Vault does not support SHA-1. Use sha256");
                    HttpContext.Features.Get<IHttpResponseFeature>().ReasonPhrase = ModelState.ToString();
                }

                return;
            }

            var dataDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            Directory.CreateDirectory(dataDir);
            Response.OnCompleted((o) => CleanUpTempDirectory(o), dataDir);

            // this might have two files, one containing the file list
            // The first will be the package and the second is the filter

            // Use a random filename rather than trusting source.FileName as it could be anything
            var inputFileName = Path.Combine(dataDir, Path.GetRandomFileName());
            // However check its extension as it might be important (e.g. zip, bundle, etc)
            var ext = Path.GetExtension(source.FileName).ToLowerInvariant();
            if (IsExtensionImportant(ext))
            {
                // Keep the input extenstion as it has significance.
                inputFileName = Path.ChangeExtension(inputFileName, ext);
            }

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

            // Send it back with the original file name, if it had one
            if (!string.IsNullOrEmpty(source.FileName))
            {
                var contentDisposition = new ContentDispositionHeaderValue("attachment");
                contentDisposition.SetHttpFileName(source.FileName);
                Response.Headers[HeaderNames.ContentDisposition] = contentDisposition.ToString();
            }

            Response.ContentType = "application/octet-stream";

            using (var fs = new FileStream(inputFileName, FileMode.Open, FileAccess.Read, FileShare.None, bufferSize: 1, FileOptions.SequentialScan | FileOptions.DeleteOnClose))
            {
                Response.StatusCode = StatusCodes.Status200OK;
                Response.ContentLength = fs.Length;
                // Output the signed file
                await fs.CopyToAsync(Response.Body);
            }

            Task CleanUpTempDirectory(object state)
            {
                DirectoryUtility.SafeDelete((string)state);
                return Task.CompletedTask;
            }
        }

        static bool IsExtensionImportant(string extension)
        {
            switch (extension)
            {
                // archives
                case ".zip":
                case ".nupkg":
                case ".snupkg":
                case ".vsix":
                case ".appxupload":
                case ".msixupload":
                // appxs
                case ".appx":
                case ".eappx":
                case ".msix":
                case ".emsix":
                // bundles
                case ".appxbundle":
                case ".eappxbundle":
                case ".msixbundle":
                case ".emsixbundle":
                    return true;
                default:
                    return false;
            }
        }
    }
}
