namespace Sign.Core
{
    internal interface IToolConfigurationProvider
    {
        FileInfo Mage { get; }
        FileInfo MakeAppx { get; }
        FileInfo SignToolManifest { get; }
    }
}