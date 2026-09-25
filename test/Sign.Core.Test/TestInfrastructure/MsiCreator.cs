// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using WixToolset.Dtf.Compression.Cab;
using WixToolset.Dtf.WindowsInstaller;
using FileAttributes = WixToolset.Dtf.WindowsInstaller.FileAttributes;
using Record = WixToolset.Dtf.WindowsInstaller.Record;

namespace Sign.Core.Test
{
    /// <summary>
    /// Creates minimal install packages for testing.  The packages are well-formed enough for
    /// <see cref="WixToolset.Dtf.WindowsInstaller.Package.InstallPackage"/> to extract and update
    /// them; they aren't installable.
    /// </summary>
    internal static class MsiCreator
    {
        private const string CreateDirectoryTable =
            "CREATE TABLE `Directory` (`Directory` CHAR(72) NOT NULL, `Directory_Parent` CHAR(72), " +
            "`DefaultDir` CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY `Directory`)";
        private const string CreateComponentTable =
            "CREATE TABLE `Component` (`Component` CHAR(72) NOT NULL, `ComponentId` CHAR(38), " +
            "`Directory_` CHAR(72) NOT NULL, `Attributes` SHORT NOT NULL, `Condition` CHAR(255), " +
            "`KeyPath` CHAR(72) PRIMARY KEY `Component`)";
        private const string CreateFileTable =
            "CREATE TABLE `File` (`File` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, " +
            "`FileName` CHAR(255) NOT NULL LOCALIZABLE, `FileSize` LONG NOT NULL, `Version` CHAR(72), " +
            "`Language` CHAR(20), `Attributes` SHORT, `Sequence` LONG NOT NULL PRIMARY KEY `File`)";
        private const string CreateMediaTable =
            "CREATE TABLE `Media` (`DiskId` SHORT NOT NULL, `LastSequence` LONG NOT NULL, " +
            "`DiskPrompt` CHAR(64) LOCALIZABLE, `Cabinet` CHAR(255), `VolumeLabel` CHAR(32), " +
            "`Source` CHAR(72) PRIMARY KEY `DiskId`)";
        private const string CreateMsiFileHashTable =
            "CREATE TABLE `MsiFileHash` (`File_` CHAR(72) NOT NULL, `Options` SHORT NOT NULL, " +
            "`HashPart1` LONG NOT NULL, `HashPart2` LONG NOT NULL, `HashPart3` LONG NOT NULL, " +
            "`HashPart4` LONG NOT NULL PRIMARY KEY `File_`)";

        /// <summary>
        /// Creates an install package whose files are compressed into a cabinet.
        /// </summary>
        /// <param name="directory">The directory to create the package in.</param>
        /// <param name="sourceFiles">The files to package, in File table sequence order.</param>
        /// <param name="embedCabinet">Whether the cabinet is embedded in the package.</param>
        internal static FileInfo Create(
            DirectoryInfo directory,
            IReadOnlyList<FileInfo> sourceFiles,
            bool embedCabinet)
        {
            FileInfo msiFile = new(Path.Combine(directory.FullName, "test.msi"));

            // The file keys double as the names of the entries in the cabinet.
            string[] fileKeys = sourceFiles.Select((file, index) => $"File{index}").ToArray();

            FileInfo cabFile = new(Path.Combine(directory.FullName, "test.cab"));

            new CabInfo(cabFile.FullName).PackFiles(
                sourceDirectory: null,
                sourceFiles.Select(file => file.FullName).ToList(),
                fileKeys);

            using (Database database = new(msiFile.FullName, DatabaseOpenMode.CreateDirect))
            {
                database.Execute(CreateDirectoryTable);
                database.Execute(CreateComponentTable);
                database.Execute(CreateFileTable);
                database.Execute(CreateMediaTable);
                database.Execute(CreateMsiFileHashTable);

                database.Execute(
                    "INSERT INTO `Directory` (`Directory`, `DefaultDir`) VALUES ('TARGETDIR', 'SourceDir')");
                database.Execute(
                    "INSERT INTO `Component` (`Component`, `Directory_`, `Attributes`) VALUES ('Component0', 'TARGETDIR', 0)");

                for (int i = 0; i < sourceFiles.Count; i++)
                {
                    database.Execute(
                        "INSERT INTO `File` (`File`, `Component_`, `FileName`, `FileSize`, `Attributes`, `Sequence`) " +
                        "VALUES ('{0}', 'Component0', '{1}', {2}, {3}, {4})",
                        fileKeys[i],
                        sourceFiles[i].Name,
                        sourceFiles[i].Length,
                        (int)FileAttributes.Compressed,
                        i + 1);
                }

                string cabinet = embedCabinet ? $"#{cabFile.Name}" : cabFile.Name;

                database.Execute(
                    "INSERT INTO `Media` (`DiskId`, `LastSequence`, `Cabinet`) VALUES (1, {0}, '{1}')",
                    sourceFiles.Count,
                    cabinet);

                if (embedCabinet)
                {
                    database.Execute("CREATE TABLE `_Stream` (`Name` CHAR(62) NOT NULL, `Data` OBJECT PRIMARY KEY `Name`)");

                    using (Record record = new(2))
                    {
                        record[1] = cabFile.Name;
                        record.SetStream(2, cabFile.FullName);

                        database.Execute("INSERT INTO `_Streams` (`Name`, `Data`) VALUES (?, ?)", record);
                    }
                }

                database.Commit();
            }

            using (SummaryInfo summaryInfo = new(msiFile.FullName, enableWrite: true))
            {
                // 0x2 means the files are compressed.
                summaryInfo.WordCount = 0x2;
                summaryInfo.Template = ";1033";
                summaryInfo.RevisionNumber = Guid.NewGuid().ToString("B").ToUpperInvariant();

                summaryInfo.Persist();
            }

            if (embedCabinet)
            {
                cabFile.Delete();
            }

            return msiFile;
        }
    }
}
