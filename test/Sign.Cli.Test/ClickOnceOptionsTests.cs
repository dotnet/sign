// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.Globalization;
using NSubstitute;
using Sign.Core;

namespace Sign.Cli.Test
{
    public sealed class ClickOnceOptionsTests
    {
        private const string Command = "code azure-key-vault -kvu https://keyvault.test -kvc certificate file.application";

        [Theory]
        [InlineData("", 1)]
        [InlineData("--clickonce-signing-version 1", 1)]
        [InlineData("--clickonce-signing-version 2", 2)]
        [InlineData("--no-sign-clickonce-deps false", 1)]
        [InlineData("--no-update-clickonce-manifest false", 1)]
        public void Version_ValidValueHasExpectedEffectiveVersion(string options, int expected)
        {
            SignCommand command = Program.CreateCommand();
            CodeCommand code = Assert.IsType<CodeCommand>(command.Subcommands.Single());
            ParseResult result = command.Parse($"{Command} {options}");

            Assert.Empty(result.Errors);
            Assert.Equal(expected, result.GetValue(code.ClickOnceSigningVersionOption));
        }

        [Theory]
        [InlineData("--clickonce-signing-version")]
        [InlineData("--clickonce-signing-version --no-sign-clickonce-deps")]
        [InlineData("--clickonce-signing-version ''")]
        [InlineData("--clickonce-signing-version abc")]
        [InlineData("--clickonce-signing-version 1.0")]
        [InlineData("--clickonce-signing-version +1")]
        [InlineData("--clickonce-signing-version 01")]
        [InlineData("--clickonce-signing-version 0")]
        [InlineData("--clickonce-signing-version -1")]
        [InlineData("--clickonce-signing-version 3")]
        [InlineData("--clickonce-signing-version 2147483647")]
        [InlineData("--clickonce-signing-version 999999999999999999999")]
        [InlineData("--no-sign-clickonce-deps")]
        [InlineData("--no-update-clickonce-manifest")]
        [InlineData("--clickonce-signing-version 1 --no-sign-clickonce-deps")]
        [InlineData("--clickonce-signing-version 1 --no-update-clickonce-manifest")]
        [InlineData("--clickonce-signing-version 1 --clickonce-signing-version 1")]
        [InlineData("--clickonce-signing-version 2 --clickonce-signing-version 2")]
        [InlineData("--clickonce-signing-version 1 --clickonce-signing-version 2")]
        [InlineData("--clickonce-signing-version 2 --clickonce-signing-version 3 --no-sign-clickonce-deps")]
        [InlineData("--clickonce-signing-version 2 --clickonce-signing-version 2 --no-update-clickonce-manifest")]
        [InlineData("--clickonce-signing-version --clickonce-signing-version 2")]
        [InlineData("--clickonce-signing-version=2 --clickonce-signing-version=2")]
        [InlineData("--no-sign-clickonce-deps=invalid")]
        [InlineData("--no-update-clickonce-manifest=invalid")]
        [InlineData("--no-sign-clickonce-deps true --no-sign-clickonce-deps false")]
        public async Task InvalidOptions_FailBeforeServicesOrSigningAreCreated(string options)
        {
            IServiceProviderFactory factory = Substitute.For<IServiceProviderFactory>();
            SignCommand command = Program.CreateCommand(factory);
            ParseResult result = command.Parse($"{Command} {options}");
            using StringWriter output = new();
            using StringWriter error = new();

            Assert.NotEmpty(result.Errors);
            int exitCode = await result.InvokeAsync(new InvocationConfiguration { Output = output, Error = error });

            Assert.NotEqual(ExitCode.Success, exitCode);
            factory.DidNotReceiveWithAnyArgs().Create(default, default, default);
        }

        [Theory]
        [InlineData("")]
        [InlineData("--clickonce-signing-version 1")]
        [InlineData("--clickonce-signing-version 2")]
        public void DependencyOptionsTogether_HaveExactSpecifiedDiagnostic(string version)
        {
            SignCommand command = Program.CreateCommand();
            ParseResult result = command.Parse($"{Command} {version} --no-sign-clickonce-deps --no-update-clickonce-manifest");

            const string Expected = "The '--no-sign-clickonce-deps' and '--no-update-clickonce-manifest' options cannot be combined.";
            Assert.Contains(result.Errors, error => error.Message == Expected);
            Assert.Equal(Expected, Resources.ClickOnceDependencyOptionsCannotBeCombined);
        }

        [Theory]
        [InlineData("--no-sign-clickonce-deps")]
        [InlineData("--no-update-clickonce-manifest")]
        public void DependencyOptions_WithVersion2AreValid(string option)
        {
            SignCommand command = Program.CreateCommand();
            Assert.Empty(command.Parse($"{Command} --clickonce-signing-version 2 {option}").Errors);
        }

        [Theory]
        [InlineData("--no-sign-clickonce-deps")]
        [InlineData("--no-update-clickonce-manifest")]
        [InlineData("--clickonce-signing-version 1 --no-sign-clickonce-deps")]
        [InlineData("--clickonce-signing-version 1 --no-update-clickonce-manifest")]
        public void DependencyOptions_WithEffectiveVersion1HaveLocalizedDiagnostic(string options)
        {
            ParseResult result = Program.CreateCommand().Parse($"{Command} {options}");
            Assert.Contains(result.Errors,
                error => error.Message == Resources.ClickOnceDependencyOptionsRequireVersion2);
        }

        [Theory]
        [InlineData("code --clickonce-signing-version 2 --no-sign-clickonce-deps azure-key-vault -kvu https://keyvault.test -kvc certificate file.application")]
        [InlineData("code --no-sign-clickonce-deps azure-key-vault -kvu https://keyvault.test -kvc certificate file.application --clickonce-signing-version 2")]
        [InlineData("code --clickonce-signing-version 2 azure-key-vault -kvu https://keyvault.test -kvc certificate file.application --no-update-clickonce-manifest")]
        public void Version2Interactions_AreValidRegardlessOfOptionPosition(string command)
        {
            Assert.Empty(Program.CreateCommand().Parse(command).Errors);
        }

        [Theory]
        [InlineData("azure-key-vault -kvu https://keyvault.test -kvc certificate")]
        [InlineData("certificate-store")]
        [InlineData("artifact-signing -ase https://signing.test -asa account -ascp profile")]
        [InlineData("trusted-signing -tse https://signing.test -tsa account -tscp profile")]
        public void Validation_IsInheritedByEveryProviderCommand(string provider)
        {
            SignCommand command = Program.CreateCommand();
            ParseResult result = command.Parse($"code {provider} file.application --no-sign-clickonce-deps");
            Assert.Contains(result.Errors, error => error.Message == Resources.ClickOnceDependencyOptionsRequireVersion2);
        }

        [Fact]
        public async Task Help_DescribesValuesDefaultRequirementsAndMutualExclusionWithoutShortAliases()
        {
            SignCommand command = Program.CreateCommand();
            CodeCommand code = Assert.IsType<CodeCommand>(command.Subcommands.Single());
            foreach (Option option in new Option[]
            {
                code.ClickOnceSigningVersionOption,
                code.NoSignClickOnceDependenciesOption,
                code.NoUpdateClickOnceManifestOption
            })
            {
                Assert.Empty(option.Aliases);
                Assert.True(option.Recursive);
                Assert.False(option.Required);
            }
            Assert.Equal(ArgumentArity.ExactlyOne, code.ClickOnceSigningVersionOption.Arity);
            using StringWriter output = new(CultureInfo.InvariantCulture);
            int exitCode = await command.Parse("code azure-key-vault --help").InvokeAsync(
                new InvocationConfiguration { Output = output });
            string help = output.ToString();

            Assert.Equal(0, exitCode);
            Assert.Contains("--clickonce-signing-version", help);
            Assert.Contains("--no-sign-clickonce-deps", help);
            Assert.Contains("--no-update-clickonce-manifest", help);
            string normalizedHelp = System.Text.RegularExpressions.Regex.Replace(help, @"\s+", " ");
            Assert.Contains(Resources.ClickOnceSigningVersionOptionDescription, normalizedHelp);
            Assert.Contains(Resources.NoSignClickOnceDependenciesOptionDescription, normalizedHelp);
            Assert.Contains(Resources.NoUpdateClickOnceManifestOptionDescription, normalizedHelp);
            Assert.Contains("Allowed values are '1' (legacy) and '2' (manifest-driven).", code.ClickOnceSigningVersionOption.Description);
            Assert.Contains("Defaults to 1.", code.ClickOnceSigningVersionOption.Description);
            Assert.Contains("Requires --clickonce-signing-version 2.", code.NoSignClickOnceDependenciesOption.Description);
            Assert.Contains("Cannot be combined with --no-update-clickonce-manifest.", code.NoSignClickOnceDependenciesOption.Description);
            Assert.Contains("Requires --clickonce-signing-version 2.", code.NoUpdateClickOnceManifestOption.Description);
            Assert.Contains("Cannot be combined with --no-sign-clickonce-deps.", code.NoUpdateClickOnceManifestOption.Description);
            Assert.Contains("without resolving dependencies or updating file information", code.NoUpdateClickOnceManifestOption.Description);
            Assert.Contains("without signing dependent manifests or payloads", code.NoSignClickOnceDependenciesOption.Description);
        }
    }
}
