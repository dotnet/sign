using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace SignNuGetBinaries
{
    class Program
    {
        static int Main(string[] args)
        {

            if (args.Length != 2)
            {
                Console.WriteLine("Usage: SignNuGetBinaries.exe <inputpath> <outputpath>");
                return -1;
            }

            var inputPath = args[0];
            var outputPath = args[1];

            if (!Directory.Exists(inputPath))
            {
                Console.WriteLine("The specified directory does not exist.");
                return -2;
            }

            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            var temp = Path.Combine(Path.GetTempPath(), "Sign_" + DateTime.Now.ToString("yyyyMMddHHmmss"));

            Console.WriteLine("Creating temporary folder " + temp);
            Directory.CreateDirectory(temp);

            ICodeSignService service = new SigntoolCodeSignService(Path.Combine(temp, "out"), "http://timestamp.digicert.com", "Reactive Extensions for .NET", "http://reactivex.io/");

            var packagesTemp = Path.Combine(temp, "packages");
            Directory.CreateDirectory(packagesTemp);

            var packages = Directory.GetFiles(inputPath, "*.nupkg");

            var jobs = new List<Task>();

            foreach (var package in packages)
            {
                var packageName = Path.GetFileName(package);
                Console.WriteLine("Extracting NuGet package " + packageName);

                var target = Path.Combine(packagesTemp, packageName);
                Directory.CreateDirectory(target);

                ZipFile.ExtractToDirectory(package, target);

                var lib = Path.Combine(target, "lib");

                if (Directory.Exists(lib))
                {
                    var allLibJobs = new List<Task>();

                    foreach (var libFolder in Directory.GetDirectories(lib))
                    {
                        var libFolderName = Path.GetFileName(libFolder);

                        var files = new List<string>();

                        foreach (var library in Directory.GetFiles(libFolder, "*.dll"))
                        {
                            files.Add(library);
                        }

                        if (files.Count > 0)
                        {
                            var jobName = packageName + " - " + libFolderName;

                            Console.WriteLine("Submitting job {0}", jobName);

                            var job = service.Submit(jobName, files.ToArray());

                            allLibJobs.Add(job.ContinueWith(t =>
                            {
                                Console.WriteLine("Completed job {0}", jobName);

                                var completionPath = t.Result;

                                foreach (var file in Directory.GetFiles(completionPath))
                                {
                                    var name = Path.GetFileName(file);
                                    var dest = Path.Combine(libFolder, name);
                                    File.Copy(file, dest, true);
                                }
                            }));
                        }
                    }

                    jobs.Add(Task.WhenAll(allLibJobs.ToArray()).ContinueWith(_ =>
                    {
                        Console.WriteLine("Building signed NuGet package " + packageName);

                        var signedPackageFile = Path.Combine(outputPath, packageName);
                        //ZipFile giving a strange bug - shell out to 7z for now
                        //ZipFile.CreateFromDirectory(target, signedPackageFile);
                        // Hack in 7z call
                        Process zip = new Process();
                        zip.StartInfo.WorkingDirectory = outputPath;
                        zip.StartInfo.FileName = @"C:\Program Files\7-Zip\7z.exe";
                        zip.StartInfo.UseShellExecute = false;
                        zip.StartInfo.RedirectStandardError = false;
                        zip.StartInfo.RedirectStandardOutput = false;
                        zip.StartInfo.Arguments = String.Format(@"a -tzip -r {0}.zip ""{1}\*.*""", signedPackageFile, target);
                        Console.WriteLine(@"""{0}"" {1}", zip.StartInfo.FileName, zip.StartInfo.Arguments);
                        zip.Start();
                        zip.WaitForExit();
                        if (zip.ExitCode != 0)
                        {
                            Console.Error.WriteLine("Error: 7z returned {0}", zip.ExitCode);
                        }
                        zip.Close();
                        File.Move(signedPackageFile + ".zip", signedPackageFile);

                    }));
                }
            }

            Task.WaitAll(jobs.ToArray());

            return 0;
        }


        private static void CreateZip(string targetDirectory, string signedPackageFile)
        {
            using (var stream = File.Create(signedPackageFile))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var file in Directory.GetFiles(targetDirectory, "*", SearchOption.AllDirectories))
                {
                    // Remove the target directory to get the entry name
                    var entryName = file.Substring(targetDirectory.Length).Replace('\\', '/');

                    zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
                }
            }
        }

    }

    interface ICodeSignService
    {
        Task<string> Submit(string name, string[] files);
    }

    class MockCodeSignService : ICodeSignService
    {
        private readonly string _completionPath;

        public MockCodeSignService(string completionPath)
        {
            _completionPath = completionPath;

            Directory.CreateDirectory(completionPath);
        }

        public Task<string> Submit(string name, string[] files)
        {
            Console.WriteLine("Signing job {0} with {1} files", name, files.Length);

            var outPath = Path.Combine(_completionPath, Guid.NewGuid().ToString());
            Directory.CreateDirectory(outPath);

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                var outFile = Path.Combine(outPath, fileName);
                File.Copy(file, outFile);
                File.Encrypt(outFile);
            }

            return Task.FromResult(outPath);
        }
    }
}
