// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using WixToolset.Dtf.WindowsInstaller;
using WixToolset.Dtf.WindowsInstaller.Package;
using FileAttributes = WixToolset.Dtf.WindowsInstaller.FileAttributes;

namespace Sign.Core
{
    /// <summary>
    /// Reads the parts of an install package that live outside of it.
    /// </summary>
    internal static class MsiMedia
    {
        /// <summary>
        /// Gets the names of the cabinets that aren't embedded in the package.  They're located in
        /// the same directory as the package.
        /// </summary>
        internal static IReadOnlyList<string> GetExternalCabinetNames(Database database)
        {
            ArgumentNullException.ThrowIfNull(database, nameof(database));

            List<string> cabinetNames = new();

            if (!database.Tables.Contains("Media"))
            {
                return cabinetNames;
            }

            using (View view = database.OpenView("SELECT `Cabinet` FROM `Media`"))
            {
                view.Execute();

                for (Record record = view.Fetch(); record is not null; record = view.Fetch())
                {
                    using (record)
                    {
                        string cabinetName = record.GetString(1);

                        // A leading '#' means the cabinet is embedded in the package.
                        if (!string.IsNullOrEmpty(cabinetName) && !cabinetName.StartsWith('#'))
                        {
                            cabinetNames.Add(cabinetName);
                        }
                    }
                }
            }

            return cabinetNames;
        }

        /// <summary>
        /// Gets the source paths, relative to the package, of the files that aren't compressed into
        /// a cabinet.
        /// </summary>
        internal static IReadOnlyList<string> GetUncompressedFilePaths(InstallPackage package)
        {
            ArgumentNullException.ThrowIfNull(package, nameof(package));

            List<string> filePaths = new();

            if (!package.Tables.Contains("File"))
            {
                return filePaths;
            }

            bool defaultCompressed = (package.SummaryInfo.WordCount & 0x2) != 0;

            using (View view = package.OpenView("SELECT `File`, `Attributes` FROM `File`"))
            {
                view.Execute();

                for (Record record = view.Fetch(); record is not null; record = view.Fetch())
                {
                    using (record)
                    {
                        string fileKey = record.GetString(1);
                        int attributes = record.GetInteger(2);

                        if ((attributes & (int)FileAttributes.Compressed) != 0)
                        {
                            continue;
                        }

                        InstallPath installPath = package.Files[fileKey];

                        if (installPath is null)
                        {
                            continue;
                        }

                        if ((attributes & (int)FileAttributes.NonCompressed) != 0)
                        {
                            // Non-compressed files are located in the same directory as the
                            // package, without any path.
                            filePaths.Add(installPath.SourceName);
                        }
                        else if (!defaultCompressed)
                        {
                            filePaths.Add(installPath.SourcePath);
                        }
                    }
                }
            }

            return filePaths;
        }
    }
}
