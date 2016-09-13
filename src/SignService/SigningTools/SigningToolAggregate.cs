using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace SignService.SigningTools
{
    public interface ISigningToolAggregate
    {
        Task Submit(string name, string description, string descriptionUrl, IList<string> files);
    }

    public class SigningToolAggregate : ISigningToolAggregate
    {
        readonly ICodeSignService defaultCodeSignService;
        readonly IDictionary<string, ICodeSignService> codeSignServices;


        public SigningToolAggregate(IList<ICodeSignService> services)
        {
            // pe files
            defaultCodeSignService = services.Single(c => c.IsDefault);

            var list = from cs in services
                       from ext in cs.SupportedFileExtensions
                       where !cs.IsDefault
                       select new { cs, ext };

            this.codeSignServices = list.ToDictionary(k => k.ext.ToLowerInvariant(), v => v.cs);
        }



        public async Task Submit(string name, string description, string descriptionUrl, IList<string> files)
        {
            // split by code sign service and fallback to default

            var grouped = (from kvp in codeSignServices
                      join file in files on kvp.Key equals Path.GetExtension(file).ToLowerInvariant()
                      group file by kvp.Value into g
                      select g).ToList();

            // get all files and exclude existing; create default group
            var defaultFiles = files.Except(grouped.SelectMany(g => g))
                                    .Where(IsPeFile)
                                    .Select(f => new {defaultCodeSignService,f })
                                    .GroupBy(a => a.defaultCodeSignService, k => k.f)
                                    .SingleOrDefault(); // one group here
            
            if(defaultFiles != null)
                grouped.Add(defaultFiles);


            await Task.WhenAll(grouped.Select(g => g.Key.Submit(name, description, descriptionUrl, g.ToList())));
        }

        static bool IsPeFile(string file)
        {
            using (var str = File.OpenRead(file))
            {
                var buffer = new byte[2];
                if (str.CanRead)
                {
                    var read = str.Read(buffer, 0, 2);
                    if (read == 2)
                    {
                        // Look for the magic MZ header 
                        return (buffer[0] == 0x4d && buffer[1] == 0x5a);
                    }
                }
            }

            return false;
        }
    }
}
