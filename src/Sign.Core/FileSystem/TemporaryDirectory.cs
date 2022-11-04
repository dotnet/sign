namespace Sign.Core
{
    internal sealed class TemporaryDirectory : ITemporaryDirectory
    {
        private readonly IDirectoryService _directoryService;

        public DirectoryInfo Directory { get; }

        internal TemporaryDirectory(IDirectoryService directoryService)
        {
            ArgumentNullException.ThrowIfNull(directoryService, nameof(directoryService));

            Directory = directoryService.CreateTemporaryDirectory();

            _directoryService = directoryService;
        }

        public void Dispose()
        {
            _directoryService.Delete(Directory);
        }
    }
}