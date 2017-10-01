using System.Collections.Generic;
using System.Threading.Tasks;

namespace SignService.Services
{
    public interface IGraphHttpService
    {
        Task<List<T>> Get<T>(string url);
        Task<TOutput> Post<TInput, TOutput>(string url, TInput item);
        Task Patch<TInput>(string url, TInput item);
        Task Delete(string url);
    }
}