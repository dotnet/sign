using System.Collections.Generic;
using System.Threading.Tasks;

namespace SignService.Services
{
    public interface IGraphHttpService
    {
        Task<List<T>> Get<T>(string url);
        Task<T> GetScalar<T>(string url);
        Task<T> GetValue<T>(string url);
        Task<TOutput> Post<TInput, TOutput>(string url, TInput item, bool accessAsUser = false);
        Task Patch<TInput>(string url, TInput item, bool accessAsUser = false);
        Task Patch(string url, string contentBody, bool accessAsUser = false);
        Task Delete(string url, bool accessAsUser = false);
    }
}
