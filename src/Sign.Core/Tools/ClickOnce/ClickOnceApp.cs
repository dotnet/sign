// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using Microsoft.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using Manifest = Microsoft.Build.Tasks.Deployment.ManifestUtilities.Manifest;

namespace Sign.Core
{
    internal sealed class ClickOnceApp : IClickOnceApp
    {
        private const string DeployExtension = ".deploy";

        public IDeployManifest DeploymentManifest { get; }
        public FileInfo DeploymentManifestFile { get; }
        public IApplicationManifest? ApplicationManifest { get; }
        public FileInfo? ApplicationManifestFile { get; }

        private ClickOnceApp(
            IDeployManifest deploymentManifest,
            FileInfo deploymentManifestFile,
            IApplicationManifest? applicationManifest,
            FileInfo? applicationManifestFile)
        {
            DeploymentManifest = deploymentManifest;
            DeploymentManifestFile = deploymentManifestFile;
            ApplicationManifest = applicationManifest;
            ApplicationManifestFile = applicationManifestFile;
        }

        internal static bool TryReadFromDeploymentManifest(
            FileInfo deploymentManifestFile,
            ILogger logger,
            IManifestReader manifestReader,
            [NotNullWhen(true)] out IClickOnceApp? clickOnceApp)
        {
            ArgumentNullException.ThrowIfNull(deploymentManifestFile, nameof(deploymentManifestFile));
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));
            ArgumentNullException.ThrowIfNull(manifestReader, nameof(manifestReader));

            clickOnceApp = null;

            using FileStream stream = deploymentManifestFile.OpenRead();
            if (manifestReader.TryReadDeployManifest(stream, out IDeployManifest? deploymentManifest))
            {
                IApplicationManifest? applicationManifest = null;

                if (TryGetApplicationManifestFile(
                    deploymentManifest,
                    deploymentManifestFile,
                    logger,
                    out FileInfo? applicationManifestFile))
                {
                    using FileStream appStream = applicationManifestFile.OpenRead();
                    manifestReader.TryReadApplicationManifest(appStream, out applicationManifest);
                }

                clickOnceApp = new ClickOnceApp(
                    deploymentManifest,
                    deploymentManifestFile,
                    applicationManifest,
                    applicationManifestFile);
            }

            return clickOnceApp is not null;
        }

        public IEnumerable<FileInfo> GetPayloadFiles()
        {
            if (ApplicationManifest is null)
            {
                yield break;
            }

            string applicationManifestDirectoryPath = ApplicationManifestFile!.Directory!.FullName;

            foreach (FileInfo file in GetPayloadFiles(
                ApplicationManifest.AssemblyReferences,
                DeploymentManifest.MapFileExtensions,
                applicationManifestDirectoryPath))
            {
                yield return file;
            }

            foreach (FileInfo file in GetPayloadFiles(
                ApplicationManifest.FileReferences,
                DeploymentManifest.MapFileExtensions,
                applicationManifestDirectoryPath))
            {
                yield return file;
            }
        }

        private static IEnumerable<FileInfo> GetPayloadFiles(
            System.Collections.IEnumerable baseReferences,
            bool mapFileExtensions,
            string baseDirectoryPath)
        {
            foreach (BaseReference baseReference in baseReferences)
            {
                string? sourcePath = null;

                // When mapFileExtensions is true, prefer constructing from TargetPath to get .deploy files
                if (mapFileExtensions && !string.IsNullOrEmpty(baseDirectoryPath) && !string.IsNullOrEmpty(baseReference.TargetPath))
                {
                    string relativeFilePath = baseReference.TargetPath.Replace('/', Path.DirectorySeparatorChar);

                    if (!relativeFilePath.EndsWith(DeployExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        relativeFilePath += DeployExtension;
                    }

                    string candidatePath = Path.Combine(baseDirectoryPath, relativeFilePath);

                    if (File.Exists(candidatePath))
                    {
                        sourcePath = candidatePath;
                    }
                }

                // If we haven't found via TargetPath, try ResolvedPath
                if (string.IsNullOrEmpty(sourcePath) && !string.IsNullOrEmpty(baseReference.ResolvedPath) && File.Exists(baseReference.ResolvedPath))
                {
                    // When mapFileExtensions is true, prefer ResolvedPath if it has .deploy suffix
                    if (mapFileExtensions)
                    {
                        if (baseReference.ResolvedPath.EndsWith(DeployExtension, StringComparison.OrdinalIgnoreCase))
                        {
                            sourcePath = baseReference.ResolvedPath;
                        }
                        else
                        {
                            // Try adding .deploy to ResolvedPath
                            string candidateWithDeploy = baseReference.ResolvedPath + DeployExtension;
                            if (File.Exists(candidateWithDeploy))
                            {
                                sourcePath = candidateWithDeploy;
                            }
                            else
                            {
                                // Fall back to ResolvedPath even without .deploy
                                sourcePath = baseReference.ResolvedPath;
                            }
                        }
                    }
                    else
                    {
                        sourcePath = baseReference.ResolvedPath;
                    }
                }

                // Final fallback: construct from TargetPath without .deploy check
                if (string.IsNullOrEmpty(sourcePath) && !string.IsNullOrEmpty(baseDirectoryPath) && !string.IsNullOrEmpty(baseReference.TargetPath))
                {
                    string relativeFilePath = baseReference.TargetPath.Replace('/', Path.DirectorySeparatorChar);
                    string candidatePath = Path.Combine(baseDirectoryPath, relativeFilePath);

                    if (File.Exists(candidatePath))
                    {
                        sourcePath = candidatePath;
                    }
                }

                if (!string.IsNullOrEmpty(sourcePath))
                {
                    yield return new FileInfo(sourcePath);
                }
            }
        }

        // Non-private for testing purposes.
        internal static bool TryReadManifest<T>(
            FileInfo manifestFile,
            ILogger logger,
            [NotNullWhen(true)] out T? manifest,
            IManifestReader manifestReader)
            where T : Manifest
        {
            manifest = null;

            using (FileStream stream = File.OpenRead(manifestFile.FullName))
            {
                manifest = manifestReader.ReadManifest(stream, preserveStream: false) as T;
            }

            if (manifest is not null)
            {
                bool hasErrors = LogOutputMessages(manifest, logger);

                if (hasErrors)
                {
                    manifest = null;

                    return false;
                }

                manifest.ReadOnly = false;

                return true;
            }

            return false;
        }

        internal static bool LogOutputMessages(Manifest manifest, ILogger logger)
        {
            return LogOutputMessages(manifest.OutputMessages, logger);
        }

        internal static bool LogOutputMessages(IApplicationManifest applicationManifest, ILogger logger)
        {
            return LogOutputMessages(applicationManifest.OutputMessages, logger);
        }

        internal static bool LogOutputMessages(IDeployManifest deployManifest, ILogger logger)
        {
            return LogOutputMessages(deployManifest.OutputMessages, logger);
        }

        private static bool LogOutputMessages(OutputMessageCollection outputMessages, ILogger logger)
        {
            bool hasErrors = false;

            foreach (OutputMessage outputMessage in outputMessages)
            {
                string[] args = outputMessage.GetArguments();
                string messageFormat = "[{ID}]  {message}";

                if (args.Length > 0)
                {
                    messageFormat += " Arguments: {arguments}";
                }

                string arguments = string.Join(", ", args);

                switch (outputMessage.Type)
                {
                    case OutputMessageType.Info:
                        logger.LogInformation(messageFormat, outputMessage.Name, outputMessage.Text, arguments);
                        break;

                    case OutputMessageType.Warning:
                        logger.LogWarning(messageFormat, outputMessage.Name, outputMessage.Text, arguments);
                        break;

                    case OutputMessageType.Error:
                    default:
                        logger.LogError(messageFormat, outputMessage.Name, outputMessage.Text, arguments);
                        hasErrors = true;
                        break;
                }
            }

            return hasErrors;
        }

        private static bool TryGetApplicationManifestFile(
            IDeployManifest deploymentManifest,
            FileInfo deployManifestFile,
            ILogger logger,
            [NotNullWhen(true)] out FileInfo? appManifestFile)
        {
            appManifestFile = null;

            string? targetPath = deploymentManifest.EntryPoint?.TargetPath;

            if (string.IsNullOrEmpty(targetPath))
            {
                return false;
            }

            string deploymentManifestDirectoryPath = deployManifestFile.DirectoryName!;

            deploymentManifest.ResolveFiles([deployManifestFile.DirectoryName!]);

            bool hasErrors = LogOutputMessages(deploymentManifest, logger);
            deploymentManifest.OutputMessages.Clear();

            if (hasErrors)
            {
                return false;
            }

            string? appManifestFilePath = deploymentManifest.EntryPoint?.ResolvedPath;

            if (string.IsNullOrEmpty(appManifestFilePath))
            {
                return false;
            }

            appManifestFile = new FileInfo(appManifestFilePath);

            return true;
        }
    }
}
