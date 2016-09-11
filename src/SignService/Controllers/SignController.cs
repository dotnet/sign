using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace SignService.Controllers
{
#if !DEBUG
    [Authorize]
#endif

    [Route("[controller]")]
    public class SignController : Controller
    {
        readonly ICodeSignService codeSignService;
        IHostingEnvironment environment;

        public SignController(IHostingEnvironment environment, ICodeSignService codeSignService)
        {
            this.environment = environment;
            this.codeSignService = codeSignService;
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

                await codeSignService.Submit(name, description, descriptionUrl, new[] { fileName });


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
    }
}