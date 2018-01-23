using System.Collections.Generic;
using System.Threading.Tasks;
using SignService.SigningTools;

namespace SignService
{
    public interface ICodeSignService
    {
        Task Submit(HashMode hashMode, string name, string description, string descriptionUrl, IList<string> files, string filter);

        IReadOnlyCollection<string> SupportedFileExtensions { get; }

        bool IsDefault { get; }
    }
}
