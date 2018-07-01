using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Refit;

namespace SignClient
{
    [Headers("Authorization: Bearer")]
    public interface ISignService
    {
        [Multipart]
        [Post("/sign?hashMode={hashMode}&name={name}&description={description}&descriptionUrl={descriptionUrl}")]
        Task<HttpResponseMessage> SignFile(FileInfo source, FileInfo filelist, HashMode hashMode, string name, string description, string descriptionUrl);
    }

    public enum HashMode
    {
        Sha256,
        Dual,
        Sha1
    }
}
