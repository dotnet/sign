using System;
using System.Collections.Generic;
using Microsoft.Extensions.CommandLineUtils;

namespace SignClient
{
    public class Program
    {
        public static int Main(string[] args)
        {
            var application = new CommandLineApplication(throwOnUnexpectedArg: false);
            var signCommand = application.Command("sign", throwOnUnexpectedArg: false, configuration: cfg =>
            {
                cfg.Description = "Signs a file or set of files";
                cfg.HelpOption("-? | -h | --help");
                var configFile  = cfg.Option("-c | --config", "Path to config json file", CommandOptionType.SingleValue);
                var inputFile   = cfg.Option("-i | --input", "Path to input file", CommandOptionType.SingleValue);
                var baseDirectory = cfg.Option("-b | --baseDirectory", "Base directory for files to override the working directory", CommandOptionType.SingleValue);
                var outputFile  = cfg.Option("-o | --output", "Path to output. May be same as input to overwrite", CommandOptionType.SingleValue);
                var fileList    = cfg.Option("-f | --filelist", "Full path to file containing paths of files to sign within an archive", CommandOptionType.SingleValue);
                var secret      = cfg.Option("-s | --secret", "Client Secret", CommandOptionType.SingleValue);
                var user        = cfg.Option("-r | --user", "Username", CommandOptionType.SingleValue);
                var name        = cfg.Option("-n | --name", "Name of project for tracking", CommandOptionType.SingleValue);
                var description = cfg.Option("-d | --description", "Description", CommandOptionType.SingleValue);
                var descUrl     = cfg.Option("-u | --descriptionUrl", "Description Url", CommandOptionType.SingleValue);
                var maxConcurrency = cfg.Option("-m | --maxConcurrency", "Maximum concurrency (default is 4)", CommandOptionType.SingleValue);
                var loggingLevel = cfg.Option("-l | --logLevel", "Logging level (default is warn)", CommandOptionType.SingleValue);
                
                cfg.OnExecute(() =>
                {
                    var sign = new SignCommand(application);
                    return sign.Sign(configFile, inputFile, baseDirectory, outputFile, fileList, secret, user, name, description, descUrl, maxConcurrency, loggingLevel);
                });
            });

            application.HelpOption("-? | -h | --help");
            application.VersionOption("-v | --version", typeof(Program).Assembly.GetName().Version.ToString(3));
            if (args.Length == 0)
            {
                application.ShowHelp();
            }
            return application.Execute(args);
        }
    }
}
