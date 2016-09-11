using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Refit;
using SignServiceClient;

namespace SignClient
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // Args
            // 0 sourceFile
            // 1 outputFile
            // 2 url
            // 3 type: zip/file
            // 4 clientId
            // 5 clientSecret
            // 6 name
            // 7 description
            // 8 descriptionUrl

            DoMain(args).Wait();
        }

        static async Task DoMain(string[] args)
        {
            var client = RestService.For<ISignService>(args[2]);

            var input = new FileInfo(args[0]);
            var output = new FileInfo(args[1]);
            Directory.CreateDirectory(output.DirectoryName);

        
                var mpContent = new MultipartFormDataContent("-----Boundary----");
                var content = new StreamContent(input.OpenRead());
                mpContent.Add(content, "source", input.Name);

            HttpResponseMessage response;
            if (args[3] == "file")
            {
                 response = await client.SignSingleFile(mpContent, args[6], args[7], args[8]);
            }
            else if (args[3] == "zip")
            {
                response = await client.SignZipFile(mpContent, args[6], args[7], args[8]);
            }
            else
            {
                throw new ArgumentException("type must be either zip or file");
            }

            var str = await response.Content.ReadAsStreamAsync();

                using (var fs = output.OpenWrite())
                {
                    await str.CopyToAsync(fs);
                }
            
        }
    }
}
