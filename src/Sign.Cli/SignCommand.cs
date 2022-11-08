using System.CommandLine;

namespace Sign.Cli
{
    internal sealed class SignCommand : Command
    {
        internal SignCommand()
            : base("sign", ".NET Sign CLI")
        {
            CodeCommand codeCommand = new();

            AddCommand(codeCommand);

            AzureKeyVaultCommand azureKeyVaultCommand = new(codeCommand);

            codeCommand.AddCommand(azureKeyVaultCommand);
        }
    }
}