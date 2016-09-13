using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Refit;

namespace SignServiceClient
{
    [Headers("Authorization: Bearer")]
    public interface ISignService
    {
        [Post("/sign/singleFile")]
        Task<HttpResponseMessage> SignSingleFile(HttpContent source, HashMode hashMode, string name, string description, string descriptionUrl);

        [Post("/sign/zipFile")]
        Task<HttpResponseMessage> SignZipFile(HttpContent source, HashMode hashMode, string name, string description, string descriptionUrl);
    }

    public enum HashMode
    {
        Sha256,
        Dual
    }
}
