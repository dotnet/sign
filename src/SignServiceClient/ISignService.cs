using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Refit;

namespace SignServiceClient
{
    public interface ISignService
    {
        [Post("/sign/singleFile")]
        Task<HttpResponseMessage> SignSingleFile(HttpContent source, string name, string description, string descriptionUrl);
    }
}
