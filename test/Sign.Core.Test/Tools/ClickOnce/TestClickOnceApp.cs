// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Extensions.Logging;
using Moq;
using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    internal class TestClickOnceApp
    {
        private const string AppName = "App";
        private const string AppDescription = "AppDescription";
        private const string AppProcessorArchitecture = "msil";
        private const string AppPublisher = "AppPublisher";
        private const string AppVersion = "1.0.0.0";

        static TestClickOnceApp()
        {
            MsBuildLocatorHelper.EnsureInitialized();
        }

        internal DeployManifest DeploymentManifest { get; }
        internal FileInfo DeploymentManifestFile { get; }
        internal ApplicationManifest ApplicationManifest { get; }
        internal FileInfo ApplicationManifestFile { get; }
        internal string Name => AppName;
        internal string Description => AppDescription;
        internal string Publisher => AppPublisher;

        private TestClickOnceApp(
            DeployManifest deploymentManifest,
            FileInfo deploymentManifestFile,
            ApplicationManifest applicationManifest,
            FileInfo applicationManifestFile)
        {
            DeploymentManifest = deploymentManifest;
            DeploymentManifestFile = deploymentManifestFile;
            ApplicationManifest = applicationManifest;
            ApplicationManifestFile = applicationManifestFile;
        }

        internal static TestClickOnceApp Create(
            DirectoryInfo directory,
            string? applicationRelativeDirectoryPath = null,
            bool mapFileExtensions = true,
            Action<DirectoryInfo>? additionalFilesCreator = null)
        {
            DirectoryInfo applicationDirectory;

            if (string.IsNullOrEmpty(applicationRelativeDirectoryPath))
            {
                applicationDirectory = directory;
            }
            else
            {
                applicationDirectory = new DirectoryInfo(Path.Combine(directory.FullName, applicationRelativeDirectoryPath));

                applicationDirectory.Create();
            }

            if (additionalFilesCreator is not null)
            {
                additionalFilesCreator(applicationDirectory);
            }

            ILogger logger = Mock.Of<ILogger>();
            FileInfo[] additionalFiles = applicationDirectory.GetFiles();
            FileInfo appFile = CreateApp(applicationDirectory);
            FileInfo applicationManifestFile = CreateApplicationManifest(
                appFile,
                additionalFiles,
                mapFileExtensions);
            FileInfo deploymentManifestFile = CreateDeploymentManifest(
                directory,
                applicationManifestFile,
                mapFileExtensions,
                logger);

            return Read(deploymentManifestFile, applicationManifestFile, logger);
        }

        private static TestClickOnceApp Read(
            FileInfo deploymentManifestFile,
            FileInfo applicationManifestFile,
            ILogger logger)
        {
            IManifestReader manifestReader = new ManifestReaderAdapter();
            Assert.True(ClickOnceApp.TryReadManifest(deploymentManifestFile, logger, out DeployManifest? deploymentManifest, manifestReader));
            Assert.True(ClickOnceApp.TryReadManifest(applicationManifestFile, logger, out ApplicationManifest? applicationManifest, manifestReader));

            return new TestClickOnceApp(
                deploymentManifest!,
                deploymentManifestFile,
                applicationManifest!,
                applicationManifestFile);
        }

        private static FileInfo CreateDeploymentManifest(
            DirectoryInfo directory,
            FileInfo applicationManifestFile,
            bool mapFileExtensions,
            ILogger logger)
        {
            DeployManifest deploymentManifest = new()
            {
                Description = AppDescription,
                MapFileExtensions = mapFileExtensions,
                Product = AppName,
                Publisher = AppPublisher
            };
            FileInfo deploymentManifestFile = new(Path.Combine(directory.FullName, $"{AppName}.application"));

            IManifestReader manifestReader = new ManifestReaderAdapter();
            Assert.True(ClickOnceApp.TryReadManifest(
                applicationManifestFile,
                logger,
                out ApplicationManifest? applicationManifest,
                manifestReader));
            Assert.NotNull(applicationManifest);
            string? targetPath = Path.GetRelativePath(
                deploymentManifestFile.Directory!.FullName,
                applicationManifestFile.FullName);

            deploymentManifest.AssemblyIdentity.Name = AppName;
            deploymentManifest.AssemblyIdentity.Version = AppVersion;
            deploymentManifest.AssemblyIdentity.ProcessorArchitecture = AppProcessorArchitecture;

            AssemblyReference assemblyReference = new(applicationManifestFile.FullName)
            {
                AssemblyIdentity = applicationManifest.AssemblyIdentity,
                ReferenceType = AssemblyReferenceType.ClickOnceManifest,
                TargetPath = targetPath
            };
            deploymentManifest.EntryPoint = assemblyReference;

            deploymentManifest.AssemblyReferences.Clear();
            deploymentManifest.AssemblyReferences.Add(assemblyReference);
            deploymentManifest.ResolveFiles();
            deploymentManifest.UpdateFileInfo();

            using (FileStream stream = deploymentManifestFile.OpenWrite())
            {
                ManifestWriter.WriteManifest(deploymentManifest, stream);
            }

            return deploymentManifestFile;
        }

        private static FileInfo CreateApplicationManifest(
            FileInfo appFile,
            FileInfo[] additionalFiles,
            bool mapFileExtensions)
        {
            ApplicationManifest applicationManifest = new()
            {
                Description = AppDescription,
                Product = AppName,
                Publisher = AppPublisher
            };

            applicationManifest.AssemblyIdentity.Name = AppName;
            applicationManifest.AssemblyIdentity.Version = AppVersion;
            applicationManifest.AssemblyIdentity.ProcessorArchitecture = AppProcessorArchitecture;

            AssemblyReference assemblyReference = new(appFile.FullName)
            {
                ReferenceType = AssemblyReferenceType.ManagedAssembly
            };

            applicationManifest.AssemblyReferences.Add(assemblyReference);
            applicationManifest.EntryPoint = assemblyReference;

            foreach (FileInfo additionalFile in additionalFiles)
            {
                if (string.Equals(".dll", additionalFile.Extension, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(".exe", additionalFile.Extension, StringComparison.OrdinalIgnoreCase))
                {
                    assemblyReference = new(additionalFile.FullName)
                    {
                        ReferenceType = AssemblyReferenceType.ManagedAssembly
                    };

                    applicationManifest.AssemblyReferences.Add(assemblyReference);
                }
                else
                {
                    FileReference fileReference = new(additionalFile.FullName);

                    applicationManifest.FileReferences.Add(fileReference);
                }
            }

            applicationManifest.ResolveFiles(new[] { appFile.Directory!.FullName });
            applicationManifest.UpdateFileInfo();

            FileInfo applicationManifestFile = new(Path.Combine(appFile.Directory.FullName, $"{AppName}.exe.manifest"));

            using (FileStream stream = applicationManifestFile.OpenWrite())
            {
                ManifestWriter.WriteManifest(applicationManifest, stream);
            }

            if (mapFileExtensions)
            {
                File.Move(appFile.FullName, appFile.FullName + ".deploy", overwrite: true);

                foreach (FileInfo additionalFile in additionalFiles)
                {
                    File.Move(additionalFile.FullName, additionalFile.FullName + ".deploy", overwrite: true);
                }
            }

            return applicationManifestFile;
        }

        private static FileInfo CreateApp(DirectoryInfo directory)
        {
            string sourceCode = "class App { static void Main() { } }";
            string appFileName = $"{AppName}.exe";

            FileInfo appFile = new(Path.Combine(directory.FullName, appFileName));
            CreateAssembly(sourceCode, OutputKind.ConsoleApplication, appFile);

            return appFile;
        }

        internal static void CreateAssembly(string sourceCode, OutputKind outputKind, FileInfo file)
        {
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            string frameworkPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "Microsoft.NET",
                "Framework",
                "v4.0.30319");
            PortableExecutableReference[] references = new[]
            {
                MetadataReference.CreateFromFile(Path.Combine(frameworkPath, "mscorlib.dll")),
                MetadataReference.CreateFromFile(Path.Combine(frameworkPath, "System.dll"))
            };
            string assemblyName = Path.GetFileNameWithoutExtension(file.Name);
            CSharpCompilation compilation = CSharpCompilation.Create(
                assemblyName,
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(outputKind));
            EmitResult result = compilation.Emit(file.FullName);

            Assert.True(result.Success);
        }
    }
}
