namespace Sign.Core.Test
{
    internal sealed class AggregatingSignatureProviderSpy : IAggregatingSignatureProvider
    {
        internal List<FileInfo> FilesSubmittedForSigning { get; } = new();

        public bool CanSign(FileInfo file)
        {
            throw new NotImplementedException();
        }

        public Task SignAsync(IEnumerable<FileInfo> files, SignOptions options)
        {
            FilesSubmittedForSigning.AddRange(files);

            return Task.CompletedTask;
        }
    }
}