// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sign.Core;

namespace Sign.Cli;

public class ExportCommand : Command
{
    internal Option<LogLevel> VerbosityOption { get; } = new(["--verbosity", "-v"], () => LogLevel.Warning, Resources.VerbosityOptionDescription);
    internal Option<string?> OutputOption { get; } = new(["--output", "-o"], Resources.ExportOutputOptionDescription);

    internal ExportCommand()
        : base("export", Resources.ExportCommandDescription)
    {
        OutputOption.IsRequired = true;

        AddGlobalOption(VerbosityOption);
        AddGlobalOption(OutputOption);
    }

    internal async Task HandleAsync(InvocationContext context, IServiceProviderFactory serviceProviderFactory,
        ISignatureProvider signatureProvider)
    {
        // Some of the options have a default value and that is why we can safely use
        // the null-forgiving operator (!) to simplify the code.
        LogLevel verbosity = context.ParseResult.GetValueForOption(VerbosityOption);
        string outputPath = context.ParseResult.GetValueForOption(OutputOption)!;

        IServiceProvider serviceProvider = serviceProviderFactory.Create(
            verbosity,
            addServices: services =>
            {
                services.AddSingleton(signatureProvider.GetCertificateProvider);
            });

        IExporter exporter = serviceProvider.GetRequiredService<IExporter>();

        context.ExitCode = await exporter.ExportAsync(outputPath);
    }
}
