namespace Sign.Core
{
    internal interface ICliTool : ITool
    {
        Task<int> RunAsync(string? args);
    }
}