using OpenVsixSignTool.Core;

namespace Sign.Core
{
    internal interface IOpenVsixSignTool : ITool
    {
        Task<bool> SignAsync(FileInfo file, SignConfigurationSet configuration, SignOptions options);
    }
}