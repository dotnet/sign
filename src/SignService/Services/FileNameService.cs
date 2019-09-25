using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SignService.Services
{
    public class FileNameService : IFileNameService
    {
        readonly Dictionary<string, string> localToOriginal = new Dictionary<string, string>();

        public void RegisterFileName(string original, string local)
        {
            localToOriginal[local] = $"{Path.GetTempPath()}\\{original}";
        }

        public string GetFileName(string path)
        {
            // Loop over the keys and replace parts of the string that match
            foreach (var local in localToOriginal)
            {
                path = path.Replace(local.Key, local.Value);
            }

            return path;
        }
    }
}
