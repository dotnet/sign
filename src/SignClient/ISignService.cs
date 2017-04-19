using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Refit;

namespace SignClient
{
    [Headers("Authorization: Bearer")]
    public interface ISignService
    {
        [Multipart]
        [Post("/sign/singleFile?hashMode={hashMode}&name={name}&description={description}&descriptionUrl={descriptionUrl}")]
        Task<HttpResponseMessage> SignSingleFile(FileInfo source, HashMode hashMode, string name, string description, string descriptionUrl);

        [Multipart]
        [Post("/sign/zipFile?hashMode={hashMode}&name={name}&description={description}&descriptionUrl={descriptionUrl}")]
        Task<HttpResponseMessage> SignZipFile(FileInfo source, FileInfo filelist, HashMode hashMode, string name, string description, string descriptionUrl);
    }

    public enum HashMode
    {
        Sha256,
        Dual
    }
}
