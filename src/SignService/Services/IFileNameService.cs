namespace SignService.Services
{
    public interface IFileNameService
    {
        string GetFileName(string path);
        void RegisterFileName(string original, string local);
    }
}