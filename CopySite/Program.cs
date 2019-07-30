/* Copyright © 2019 Softel vdm, Inc. - https://yetawf.com/Documentation/YetaWF/Licensing */

using Ionic.Zip;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;

namespace CopySite {

    /// <summary>
    /// This is a hacky little program that is typically used during deployment of YetaWF to
    /// copy files from symlinks, which dotnet publish doesn't address.
    /// </summary>
    /// <remarks>The code could be prettier. This is a dev tool that ended up being needed for docker build on linux. Oh well.
    ///
    /// One redeeming feature it has is that it prunes node_modules and only deploys what is actually referenced by the website.
    /// </remarks>
    class Program {

        public const string MarkerMVC6 = "wwwroot";

        public enum BlueGreenDeployEnum {
            None = 0,
            Blue = 1,
            Green = 2,
        }

        public class DB {
            public string NameDev { get; set; }
            public string NameProd { get; set; }
            public string Server { get; set; }
            public string UserName { get; set; }
            public string UserPassword { get; set; }
            public string BackupFileName { get; set; }
        }
        public class Folder {
            public string Path { get; set; }
        }
        public class Upload {
            public string FtpAddress { get; set; }
            public string UserName { get; set; }
            public string Password { get; set; }
            public string SourceFile { get; set; }
            public string TargetFile { get; set; }
        }
        public class UploadFolder {
            public string FtpAddress { get; set; }
            public string UserName { get; set; }
            public string Password { get; set; }
            public string SourceFolder { get; set; }
            public string TargetFolder { get; set; }
        }
        public class RunCmd {
            public string Command { get; set; }
            public bool IgnoreError { get; set; }
            public bool Startup { get; set; }
        }

        private string BaseDirectory;
        public string MaintenancePage { get; private set; }
        public bool UpdateIndicator { get; set; }
        public List<DB> BackupDBs { get; private set; }
        public List<DB> RestoreDBs { get; private set; }
        public string SiteLocation { get; set; }
        public string ConfigFile { get; set; }
        public string ZipFileName { get; set; }
        public string ConfigParm { get; set; }
        public string PublishOutput { get; set; }
        public BlueGreenDeployEnum BlueGreenDeploy { get; set; }

        public string TargetFolder { get; set; }
        public ZipFile TargetZip { get; set; }

        public List<Folder> Folders { get; private set; }
        public List<Upload> Uploads { get; set; }
        public List<RunCmd> RunCmds { get; set; }
        public List<UploadFolder> UploadFolders { get; set; }
        public List<string> Echos { get; set; }

        public CopyAction Action { get; set; }
        public bool IsMVC6 { get; set; }

        public enum CopyAction {
            Backup = 1,
            Restore = 2,
        }

        public const string UNZIPFOLDER = "TEMP";
        public const string ZIPFOLDER = UNZIPFOLDER;
        public const string DBFOLDER = "DBs";
        public const string DATAFOLDER = "Data";
        public const string DBDATAFOLDER = "Data";
        public const string MAINTENANCEFOLDER = "Maintenance";
        public const string UPDATEINDICATORFILE = "UpdateIndicator.txt";
        public const string APPOFFLINEPAGE = "App_Offline.htm";
        public const string DONTDEPLOY = "dontdeploy.txt";

        private const string USAGE = "Usage: {0} {{Backup|Restore}} \"..file with deploy config data.txt\"  [{{Blue|Green}}] ";

        static int Main(string[] args) {
            Console.Title = "YetaWF Copy Site";
            Program prog = new CopySite.Program();
            return prog.Run(args);
        }

        private int Run(string[] args) {
            BaseDirectory = null;
            BackupDBs = new List<DB>();
            RestoreDBs = new List<DB>();
            SiteLocation = null;
            MaintenancePage = null;
            UpdateIndicator = false;
            Folders = new List<Folder>();
            Uploads = new List<Upload>();
            UploadFolders = new List<UploadFolder>();
            RunCmds = new List<RunCmd>();
            Echos = new List<string>();
            ConfigFile = null;
            PublishOutput = null;

            Assembly asm = Assembly.GetExecutingAssembly();

            int argCount = args.Count();
            if (argCount < 2 || argCount > 3) {
                Console.WriteLine(USAGE, asm.ManifestModule.Name);
                return -1;
            }
            if (args[0] == "Backup")
                Action = CopyAction.Backup;
            else if (args[0] == "Restore")
                Action = CopyAction.Restore;
            else {
                Console.WriteLine(USAGE, asm.ManifestModule.Name);
                return -1;
            }
            BlueGreenDeploy = BlueGreenDeployEnum.None;
            if (argCount > 2) {
                if (string.Compare(args[2], "Blue", true) == 0) {
                    BlueGreenDeploy = BlueGreenDeployEnum.Blue;
                } else if (string.Compare(args[2], "Green", true) == 0) {
                    BlueGreenDeploy = BlueGreenDeployEnum.Green;
                } else {
                    Console.WriteLine(USAGE, asm.ManifestModule.Name);
                    return -1;
                }
            }

            ParseCopySiteParms(args[1]);

            CleanAll();
            if (Action == CopyAction.Backup) {
                PerformBackupDBs();
                CopyAllFilesToTarget();
                if (Action == CopyAction.Backup) {
                    foreach (UploadFolder uploadFolder in UploadFolders) {
                        UploadDirectory(uploadFolder);
                    }
                    foreach (Upload upload in Uploads) {
                        UploadFile(upload);
                    }
                }
            } else {
                foreach (RunCmd run in RunCmds) {
                    if (run.Startup)
                        RunCommand(run);
                }
                ExtractAllFiles();
                if (Directory.Exists(Path.Combine(BaseDirectory, UNZIPFOLDER, MarkerMVC6)))
                    IsMVC6 = true;
                if (IsMVC6)
                    Console.WriteLine("ASP.NET Core Site");
                else
                    Console.WriteLine("ASP.NET 4 Site");
                SetMaintenanceMode();
                SetUpdateIndicator();
                PerformRestoreDBs();
                CopyAllFilesToSite();
                foreach (RunCmd run in RunCmds) {
                    if (!run.Startup)
                        RunCommand(run);
                }
                ClearMaintenanceMode();
            }

            foreach (string s in Echos) {
                Console.WriteLine(s);
            }
            return 0;
        }

        // PARSE COPYSITE FILE
        // PARSE COPYSITE FILE
        // PARSE COPYSITE FILE

        private void ParseCopySiteParms(string fileName) {
            string[] lines = File.ReadAllLines(fileName);

            lines = ReplaceBlueGreen(lines);

            BaseDirectory = Path.GetDirectoryName(Path.GetFullPath(fileName));

            int lineCount = 0;
            foreach (string l in lines) {
                ++lineCount;
                string line = l.Trim();
                if (line.StartsWith("#")) continue; // comment
                if (string.IsNullOrWhiteSpace(line)) continue;
                string[] s = line.Split(new char[] { ' ' }, 2);
                string item = s[0];
                line = "";
                if (s.Length > 1 && !string.IsNullOrWhiteSpace(s[1]))
                    line = s[1];
                if (item == "DevDB")
                    HandleDevDB(lineCount, line);
                else if (item == "ProdDB")
                    HandleProdDB(lineCount, line);
                else if (item == "SiteLocation")
                    HandleSiteLocation(lineCount, line);
                else if (item == "ConfigFile")
                    HandleConfigFile(lineCount, line);
                else if (item == "ZipFile")
                    HandleZipFile(lineCount, line);
                else if (item == "ConfigParm")
                    HandleConfigParm(lineCount, line);
                else if (item == "MaintenancePage")
                    HandleMaintenancePage(lineCount, line);
                else if (item == "UpdateIndicator")
                    HandleUpdateIndicator(lineCount, line);
                else if (item == "CopyFolder")
                    HandleCopyFolder(lineCount, line);
                else if (item == "Upload")
                    HandleUpload(lineCount, line);
                else if (item == "UploadFolder")
                    HandleUploadFolder(lineCount, line);
                else if (item == "Run")
                    HandleRun(lineCount, line);
                else if (item == "Exec")
                    HandleExec(lineCount, line);
                else if (item == "RunFirst")
                    HandleRun(lineCount, line, Startup: true);
                else if (item == "RunFirstIgnore")
                    HandleRun(lineCount, line, Startup: true, IgnoreError: true);
                else if (item == "RunIgnore")
                    HandleRun(lineCount, line, IgnoreError: true);
                else if (item == "TargetFolder")
                    HandleTargetFolder(lineCount, line);
                else if (item == "PublishOutput")
                    HandlePublishOutput(lineCount, line);
                else if (item == "Echo")
                    HandleEcho(lineCount, line);
                else
                    throw new Error("Error on line {0}: Invalid statement {1}", lineCount, line);
            }
            if (string.IsNullOrWhiteSpace(SiteLocation))
                throw new Error("Required SiteLocation statement not provided");

            if (Action == CopyAction.Backup) {
                if (Directory.Exists(Path.Combine(SiteLocation, MarkerMVC6)))
                    IsMVC6 = true;
                if (IsMVC6)
                    Console.WriteLine("ASP.NET Core Site");
                else
                    Console.WriteLine("ASP.NET 4 Site");
            } else {
                ; // we can't yet determine whether this is an ASP.NET Core/4 site (wait until after unzip)
            }

            if (Action == CopyAction.Backup) {
                if (string.IsNullOrWhiteSpace(ConfigFile)) {
                    if (!IsMVC6)
                        throw new Error("Required ConfigFile statement not provided");
                    if (string.IsNullOrWhiteSpace(PublishOutput))
                        throw new Error("Required PublishOutput statement not provided");
                } else {
                    ConfigFile = Path.Combine(SiteLocation, ConfigFile);
                    if (!File.Exists(ConfigFile))
                        throw new Error("ConfigFile statement references a nonexistent file {0}", ConfigFile);
                    if (!string.IsNullOrWhiteSpace(PublishOutput))
                        throw new Error("The PublishOutput statement is not valid for ASP.NET 4 sites");
                }
            }
            if (Action != CopyAction.Backup) {
                if (!string.IsNullOrWhiteSpace(TargetFolder))
                    throw new Error("TargetFolder statement can only used when backing up a development system");
                if (!string.IsNullOrWhiteSpace(PublishOutput))
                    throw new Error("PublishOutput statement can only used when backing up a development system");
            }

            if (string.IsNullOrWhiteSpace(ZipFileName) && string.IsNullOrWhiteSpace(TargetFolder))
                throw new Error("Required ZipFile or TargetFolder statements not provided");
            if (Action == CopyAction.Restore && BlueGreenDeploy == BlueGreenDeployEnum.None && string.IsNullOrWhiteSpace(MaintenancePage))
                throw new Error("Required MaintenancePage statement not provided");

            ConfigParm = string.IsNullOrWhiteSpace(ConfigParm) ? "" : ConfigParm + ".";
        }

        private string[] ReplaceBlueGreen(string[] lines) {
            List<string> newLines = new List<string>();
            foreach (string l in lines) {
                string newLine = l;
                if (BlueGreenDeploy != BlueGreenDeployEnum.None) {
                    newLine = newLine.Replace("{bluegreen}", BlueGreenDeploy == BlueGreenDeployEnum.Blue ? "blue" : "green");
                    newLine = newLine.Replace("{BLUEGREEN}", BlueGreenDeploy == BlueGreenDeployEnum.Blue ? "Blue" : "Green");
                    newLine = newLine.Replace("{-BLUEGREEN}", BlueGreenDeploy == BlueGreenDeployEnum.Blue ? "-Blue" : "-Green");
                    newLine = newLine.Replace("{BLUEGREEN-OTHER}", BlueGreenDeploy == BlueGreenDeployEnum.Blue ? "Green" : "Blue");
                    newLine = newLine.Replace("{-BLUEGREEN-OTHER}", BlueGreenDeploy == BlueGreenDeployEnum.Blue ? "-Green" : "-Blue");
                } else {
                    if (l.Contains("{bluegreen}") || l.Contains("{BLUEGREEN}") || l.Contains("{-BLUEGREEN}") || l.Contains("{BLUEGREEN-OTHER}") || l.Contains("{-BLUEGREEN-OTHER}"))
                        throw new Error("BLUEGREEN variable found but this is not a blue-green deploy");
                }
                newLines.Add(newLine);
            }
            return newLines.ToArray();
        }

        private void HandleDevDB(int lineNum, string line) {
            if (Action == CopyAction.Restore)
                throw new Error("DevDB statement indicates development system, but your command line specified Restore (implying production system) - (line {0}): {1}", lineNum, line);
            string[] s = line.Split(new char[] { ',' });
            if (s.Length != 5)
                throw new Error("Invalid statement options (line {0}): {1}", lineNum, line);
            DB db = new DB {
                NameDev = s[0].Trim(),
                NameProd = s[1].Trim(),
                Server = s[2].Trim(),
                UserName = s[3].Trim(),
                UserPassword = s[4].Trim(),
            };
            BackupDBs.Add(db);
        }
        private void HandleProdDB(int lineNum, string line) {
            if (Action == CopyAction.Backup)
                throw new Error("ProdDB statement indicates production system, but your command line specified Backup (implying development system) - (line {0}): {1}", lineNum, line);
            string[] s = line.Split(new char[] { ',' });
            if (s.Length != 5)
                throw new Error("Invalid statement options (line {0}): {1}", lineNum, line);
            DB db = new DB {
                NameDev = s[0].Trim(),
                NameProd = s[1].Trim(),
                Server = s[2].Trim(),
                UserName = s[3].Trim(),
                UserPassword = s[4].Trim(),
            };
            RestoreDBs.Add(db);
        }
        private void HandleSiteLocation(int lineNum, string line) {
            if (!string.IsNullOrWhiteSpace(SiteLocation))
                throw new Error("More than one SiteLocation statement - line {0}", lineNum);
            if (string.IsNullOrWhiteSpace(line))
                throw new Error("SiteLocation statement has no arguments - line {0}", lineNum);
            if (File.Exists(line))
                SiteLocation = line;
            else
                SiteLocation = Path.Combine(BaseDirectory, line);
            if (!Directory.Exists(SiteLocation))
                throw new Error("SiteLocation statement on line {0} references a nonexistent location {1} - For first time deployment the folder must be explicitly created", lineNum, SiteLocation);
        }
        private void HandleConfigFile(int lineNum, string line) {
            if (Action == CopyAction.Restore)
                throw new Error("ConfigFile statement indicates development system, but your command line specified Restore (implying production system) - (line {0}): {1}", lineNum, line);
            if (!string.IsNullOrWhiteSpace(ConfigFile))
                throw new Error("More than one ConfigFile statement - line {0}", lineNum);
            if (string.IsNullOrWhiteSpace(line))
                throw new Error("ConfigFile statement has no arguments - line {0}", lineNum);
            ConfigFile = line;
        }
        private void HandleMaintenancePage(int lineNum, string line) {
            if (Action == CopyAction.Backup)
                throw new Error("MaintenancePage statement indicates production system, but your command line specified Backup (implying development system) - (line {0}): {1}", lineNum, line);
            if (!string.IsNullOrWhiteSpace(MaintenancePage))
                throw new Error("More than one MaintenancePage statement - line {0}", lineNum);
            if (string.IsNullOrWhiteSpace(line))
                throw new Error("MaintenancePage statement has no arguments - line {0}", lineNum);
            if (Action == CopyAction.Backup && !File.Exists(Path.Combine(SiteLocation, MAINTENANCEFOLDER, line)))
                throw new Error("Maintenance statement on line {0} references a nonexistent file {1}", lineNum, line);
            MaintenancePage = line;
        }
        private void HandleUpdateIndicator(int lineNum, string line) {
            if (Action == CopyAction.Backup)
                throw new Error("UpdateIndicator statement indicates production system, but your command line specified Backup (implying development system) - (line {0}): {1}", lineNum, line);
            if (UpdateIndicator)
                throw new Error("More than one UpdateIndicator statement - line {0}", lineNum);
            if (!string.IsNullOrWhiteSpace(line))
                throw new Error("UpdateIndicator statement has arguments which are not allowed - line {0}", lineNum);
            UpdateIndicator = true;
        }
        private void HandleZipFile(int lineNum, string line) {
            if (!string.IsNullOrWhiteSpace(ZipFileName))
                throw new Error("More than one ZipFile statement - line {0}", lineNum);
            if (string.IsNullOrWhiteSpace(line))
                throw new Error("ZipFile statement has no arguments - line {0}", lineNum);
            string page = Path.Combine(BaseDirectory, line);
            if (Action == CopyAction.Restore && !File.Exists(page))
                throw new Error("ZipFile statement on line {0} references a nonexistent file {1}", lineNum, page);
            ZipFileName = page;
        }
        private void HandleConfigParm(int lineNum, string line) {
            if (Action == CopyAction.Restore)
                throw new Error("ConfigParm statement indicates development system, but your command line specified Restore (implying production system) - (line {0}): {1}", lineNum, line);
            if (!string.IsNullOrWhiteSpace(ConfigParm))
                throw new Error("More than one ConfigParm statement - line {0}", lineNum);
            if (string.IsNullOrWhiteSpace(line))
                throw new Error("ConfigParm statement has no arguments - line {0}", lineNum);
            ConfigParm = line;
        }
        private void HandleCopyFolder(int lineNum, string line) {
            if (Action == CopyAction.Backup) {
                string sourceFolder = line.Trim();
                if (sourceFolder.StartsWith("."))
                    sourceFolder = Path.Combine(BaseDirectory, sourceFolder);
                if (!Directory.Exists(sourceFolder))
                    throw new Error("CopyFolder statement on line {0} references a nonexistent folder {1}", lineNum, sourceFolder);
                Folder folder = new Folder {
                    Path = sourceFolder,
                };
                Folders.Add(folder);
            } else { // Action == CopyAction.Restore)
                string targetFolder = line.Trim();
                if (targetFolder.StartsWith("."))
                    targetFolder = Path.Combine(BaseDirectory, targetFolder);
                Folder folder = new Folder {
                    Path = targetFolder,
                };
                Folders.Add(folder);
            }
        }
        private void HandleUpload(int lineNum, string line) {
            if (Action == CopyAction.Restore)
                throw new Error("Upload statement indicates development system, but your command line specified Restore (implying production system) - (line {0}): {1}", lineNum, line);
            string[] s = line.Split(new char[] { ',' });
            if (s.Length != 5)
                throw new Error("Invalid Upload options (line {0}): {1}", lineNum, line);
            Upload upload = new Upload {
                FtpAddress = s[0].Trim(),
                UserName = s[1].Trim(),
                Password = s[2].Trim(),
                SourceFile = Path.Combine(BaseDirectory, s[3].Trim()),
                TargetFile = s[4].Trim(),
            };
            Uploads.Add(upload);
        }
        private void HandleUploadFolder(int lineNum, string line) {
            if (Action == CopyAction.Restore)
                throw new Error("Upload statement indicates development system, but your command line specified Restore (implying production system) - (line {0}): {1}", lineNum, line);
            string[] s = line.Split(new char[] { ',' });
            if (s.Length != 5)
                throw new Error("Invalid UploadFolder options (line {0}): {1}", lineNum, line);
            UploadFolder upload = new UploadFolder {
                FtpAddress = s[0].Trim(),
                UserName = s[1].Trim(),
                Password = s[2].Trim(),
                SourceFolder = Path.Combine(BaseDirectory, s[3].Trim()),
                TargetFolder = s[4].Trim(),
            };
            UploadFolders.Add(upload);
        }
        private void HandleRun(int lineNum, string line, bool IgnoreError = false, bool Startup = false) {
            if (Action != CopyAction.Restore)
                throw new Error("Run statement indicates production system, but your command line specified Backup (implying development system) - (line {0}): {1}", lineNum, line);
            RunCmd run = new RunCmd {
                Command = line.Trim(),
                IgnoreError = IgnoreError,
                Startup = Startup,
            };
            RunCmds.Add(run);
        }
        private void HandleExec(int lineNum, string line, bool IgnoreError = false, bool Startup = false) {
            string command = line.Trim();
            RunCmd run = new RunCmd {
                Command = command,
                IgnoreError = false,
            };
            RunCommand(run);
        }
        private void HandleTargetFolder(int lineNum, string line) {
            if (Action != CopyAction.Backup)
                throw new Error("The TargetFolder statement is only supported when performing a backup operation - (line {0}): {1}", lineNum, line);
            if (!string.IsNullOrWhiteSpace(TargetFolder))
                throw new Error("More than one TargetFolder statement - line {0}", lineNum);
            TargetFolder = line.Trim();
        }
        private void HandlePublishOutput(int lineNum, string line) {
            if (Action != CopyAction.Backup)
                throw new Error("The PublishOutput statement is only supported when performing a backup operation - (line {0}): {1}", lineNum, line);
            if (!string.IsNullOrWhiteSpace(PublishOutput))
                throw new Error("More than one PublishOutput statement - line {0}", lineNum);
            PublishOutput = line.Trim();
            if (!Directory.Exists(PublishOutput))
                throw new Error("Folder {0} specified using PublishOutput statement not found - line {0}", lineNum);
        }
        private void HandleEcho(int lineCount, string line) {
            Echos.Add(line);
        }


        [Serializable]
        private class Error : System.Exception {
            public Error(string message) {
                Console.WriteLine(message);
                throw new ApplicationException(message);
            }
            public Error(string format, params object[] args) {
                string message = string.Format(format, args);
                Console.WriteLine(message);
                throw new ApplicationException(message);
            }
        }
        private void CleanAll() {
            string folder = Path.Combine(BaseDirectory, ZIPFOLDER, DBFOLDER);
            DeleteFolder(folder);
            //if (!File.Exists(Path.Combine(BaseDirectory, "UNZIPDONE"))) {
            folder = Path.Combine(BaseDirectory, UNZIPFOLDER);
            DeleteFolder(folder);
            //}

            if (!string.IsNullOrWhiteSpace(TargetFolder))
                DeleteFolder(TargetFolder);
        }

        private void DeleteFolder(string targetFolder) {
            if (!Directory.Exists(targetFolder)) return;// avoid exception spam

            int retry = 50; // folder occasionally are in use to we'll just wait a bit
            while (retry > 0) {
                try {
                    Directory.Delete(targetFolder, true);
                    return;
                } catch (Exception exc) {
                    Console.WriteLine($"Couldn't delete {targetFolder}");
                    if (exc is DirectoryNotFoundException)
                        return;// done
                    if (retry <= 1)
                        throw;
                }
                System.Threading.Thread.Sleep(1000); // wait a bit
                --retry;
            }
        }

        // BACKUP
        // BACKUP
        // BACKUP

        private void PerformBackupDBs() {
            foreach (DB db in BackupDBs)
                PerformBackupDB(db);
        }
        private void PerformBackupDB(DB db) {

            string dbName = Action == CopyAction.Backup ? db.NameDev : db.NameProd;
            Console.WriteLine("Backing up DB {0}", dbName);

            string dbFileName = Path.Combine(BaseDirectory, ZIPFOLDER, DBFOLDER, string.Format("{0}.bak", db.NameDev));

            string path = Path.GetDirectoryName(dbFileName);
            Directory.CreateDirectory(path);

            // Connection
            string connectionString = String.Format("Data Source={0};Initial Catalog={1};User ID={2};Password={3};", db.Server, dbName, db.UserName, db.UserPassword);

            using (SqlConnection sqlConnection = new SqlConnection(connectionString)) {
                Server server = new Server(new ServerConnection(sqlConnection));

                Backup backup = new Backup();
                backup.Action = BackupActionType.Database;
                backup.Database = dbName;
                backup.Incremental = false;
                backup.Initialize = true;
                backup.LogTruncation = BackupTruncateLogType.Truncate;

                // Backup Device
                BackupDeviceItem backupItemDevice = new BackupDeviceItem(dbFileName, DeviceType.File);
                backup.Devices.Add(backupItemDevice);

                // Start Backup
                backup.SqlBackup(server);
                db.BackupFileName = dbFileName;
            }
        }

        List<string> FileListExcludedFiles = new List<string> {
            @".*\.html", @".*\.swf", @".*\.fla",
            @"\.npm.*", @"authors\.txt", @"bower\.json", @"Changes\.md", @"Changelog\.md", @"License.*\.md", @"License.*\.txt", @"package\.json", @"Readme.*\.md",
            @"Gruntfile\.coffee",
        };
        List<string> FileListExcludedFolders = new List<string> {
            @"\.github", @"bin", @"demo", @"test",
        };

        private void CopyAllFilesToTarget() {

            Console.WriteLine("Copying files...");

            if (!string.IsNullOrWhiteSpace(ZipFileName)) {
                string path = Path.GetDirectoryName(ZipFileName);
                Directory.CreateDirectory(path);
                File.Delete(ZipFileName);

                TargetZip = new Ionic.Zip.ZipFile(ZipFileName);
            }

            // Add Dbs
            foreach (DB db in BackupDBs) {
                if (TargetZip != null) {
                    ZipEntry ze = TargetZip.AddFile(db.BackupFileName);
                    ze.FileName = Path.Combine(DBFOLDER, Path.GetFileName(db.BackupFileName));
                }
                if (!string.IsNullOrWhiteSpace(TargetFolder)) {
                    Directory.CreateDirectory(Path.Combine(TargetFolder, DBFOLDER));
                    File.Copy(db.BackupFileName, Path.Combine(TargetFolder, DBFOLDER, Path.GetFileName(db.BackupFileName)));
                }
            }
            // Add explicit folders
            int folderCount = 0;
            foreach (Folder folder in Folders) {
                AddFilesToTargetAndRecurse(folder.Path, "Folder_" + folderCount.ToString());
                ++folderCount;
            }
            // Add folders
            if (IsMVC6) {

                AddPublishOutput();
                AddPublishOutputFiles("*.deps.json");
                AddPublishOutputFiles("*.exe.config");

                AddAllFilesToTarget(DATAFOLDER, ExcludeFiles: new List<string> { @"AppSettings\..*", @"NLog\..*", @"UpgradeLogFile\.txt", @".*\.mdf", @".*\.ldf" });
                AddConfigFileToTarget(Path.Combine(DATAFOLDER, "AppSettings.{0}json"), Path.Combine(DATAFOLDER, "AppSettings.json"));
                AddConfigFileToTarget(Path.Combine(DATAFOLDER, "NLog.{0}config"), Path.Combine(DATAFOLDER, "NLog.config"));
                AddAllFilesToTarget("Localization");
                AddAllFilesToTarget("LocalizationCustom", Optional: true);
                AddFilesToTargetFromFileList("node_modules", ExcludeFiles: FileListExcludedFiles, ExcludeFolders: FileListExcludedFolders);
                AddFilesToTargetFromFileList("bower_components", ExcludeFiles: FileListExcludedFiles, ExcludeFolders: FileListExcludedFolders);
                AddAllFilesToTarget("Sites", ExcludeFiles: new List<string> { @"Backup .*\.zip" }, ExcludeFolders: new List<string> { "TempFiles" }, Optional: true);
                AddAllFilesToTarget("SiteTemplates", Optional: true);
                //AddAllFilesToPublishFolder("VaultPrivate");
                AddConfigFileToTarget("app.{0}config", "app.config");
                AddConfigFileToTarget("hosting.{0}json", "hosting.json");
                if (!string.IsNullOrWhiteSpace(ConfigFile))
                    AddFileToTarget(ConfigFile, "Web.config");
                else
                    AddConfigFileToTarget("Web.{0}config", "Web.config");

                AddAllFilesToTarget(Path.Combine("wwwroot", "Addons"));
                AddAllFilesToTarget(Path.Combine("wwwroot", "AddonsCustom"), Optional: true);
                AddAllFilesToTarget(Path.Combine("wwwroot", "lib"), Optional: true);
                AddAllFilesToTarget(Path.Combine("wwwroot", "Maintenance"));
                AddAllFilesToTarget(Path.Combine("wwwroot", "SiteFiles"), Optional: true);
                //AddAllFilesToTarget(Path.Combine("wwwroot", "Vault"));// never
                AddFileToTarget(Path.Combine("wwwroot", "logo.jpg"));
                AddFileToTarget(Path.Combine("wwwroot", "robots.txt"));

            } else {

                AddAllFilesToTarget("Addons");
                AddAllFilesToTarget("AddonsCustom", Optional: true);
                AddAllFilesToTarget("bin", ExcludeFiles: new List<string> { @".*\.pdb", @".*\.xml" });
                AddFilesToTargetFromFileList("node_modules", ExcludeFiles: FileListExcludedFiles, ExcludeFolders: FileListExcludedFolders);
                AddFileToTarget(Path.Combine("node_modules", "Web.config"));
                AddFilesToTargetFromFileList("bower_components", ExcludeFiles: FileListExcludedFiles, ExcludeFolders: FileListExcludedFolders);
                AddFileToTarget(Path.Combine("bower_components", "Web.config"));
                AddAllFilesToTarget(DATAFOLDER, ExcludeFiles: new List<string> { @"AppSettings\..*", @"NLog\..*", @"UpgradeLogFile\.txt", @".*\.mdf", @".*\.ldf" });
                AddConfigFileToTarget(Path.Combine(DATAFOLDER, "AppSettings.{0}json"), Path.Combine(DATAFOLDER, "AppSettings.json"));
                AddConfigFileToTarget(Path.Combine(DATAFOLDER, "NLog.{0}config"), Path.Combine(DATAFOLDER, "NLog.config"));
                AddAllFilesToTarget(MAINTENANCEFOLDER);
                AddAllFilesToTarget("Localization");
                AddAllFilesToTarget("LocalizationCustom", Optional: true);
                AddAllFilesToTarget("SiteFiles");
                AddAllFilesToTarget("Sites", ExcludeFiles: new List<string> { @"Backup .*\.zip" }, ExcludeFolders: new List<string> { @"TempFiles" });
                AddAllFilesToTarget("SiteTemplates");
                AddFileToTarget("Global.asax");
                AddFileToTarget("logo.jpg");
                AddFileToTarget("robots.txt");

                if (!string.IsNullOrWhiteSpace(ConfigFile))
                    AddFileToTarget(ConfigFile, "Web.config");
                else
                    AddConfigFileToTarget("Web.{0}config", "Web.config");
                //AddAllFilesToTarget("Vault"); // never
            }

            if (TargetZip != null) {
                Console.WriteLine("Creating Zip file...");
                TargetZip.Save();
                Console.WriteLine("Zip file completed");
            }
        }
        private void AddPublishOutput() {
            AddFilesToTargetAndRecurse(PublishOutput, "", ExcludeFiles: new List<string> { @".*\.json", @".*\.config" }, ExcludeFolders: new List<string> { @"wwwroot" });
        }
        private void AddPublishOutputFiles(string match) {
            string[] files = Directory.GetFiles(PublishOutput, match);
            foreach (string file in files) {
                string filename = Path.GetFileName(file);
                Console.WriteLine("Copying {0}", file);
                if (TargetZip != null) {
                    string relFile = Path.Combine("", filename);
                    ZipEntry ze = TargetZip.AddFile(file);
                    ze.FileName = relFile;
                }
                if (!string.IsNullOrWhiteSpace(TargetFolder)) {
                    string relFile = Path.Combine("", filename);
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(TargetFolder, relFile)));
                    File.Copy(file, Path.Combine(TargetFolder, relFile));
                }
            }
        }
        private void AddAllFilesToTarget(string folder, List<string> ExcludeFiles = null, List<string> ExcludeFolders = null, bool Optional = false) {
            string absPath = Path.Combine(SiteLocation, folder);
            string relPath = folder;
            AddFilesToTargetAndRecurse(absPath, relPath, ExcludeFiles, ExcludeFolders, Optional);
        }
        private void AddFilesToTargetAndRecurse(string absPath, string relPath, List<string> ExcludeFiles = null, List<string> ExcludeFolders = null, bool Optional = false) {
            if (ExcludeFiles == null)
                ExcludeFiles = new List<string>();
            ExcludeFiles.Add(@".*\.lastcodeanalysissucceeded");
            if (ExcludeFolders == null)
                ExcludeFolders = new List<string>();
            ExcludeFolders.Add(@"\.git");

            if (File.Exists(Path.Combine(absPath, DONTDEPLOY))) return;

            if (!Directory.Exists(absPath)) {
                if (Optional)
                    return;
                throw new Error($"Folder {absPath} does not exist");
            }

            string[] files = Directory.GetFiles(absPath);
            foreach (string file in files) {
                bool exclude = false;
                string filename = Path.GetFileName(file);
                foreach (string excludeFile in ExcludeFiles) {
                    if (LikeString(filename, excludeFile)) {
                        exclude = true;
                        break;
                    }
                }
                if (!exclude) {
                    Console.WriteLine("Copying {0}", file);

                    // Check for minimal length (most files should be > 0 (or > 3 Unicode)
                    long length = new System.IO.FileInfo(file).Length;
                    if (length <= 3) {
                        if ((file.EndsWith(".ts") && !file.EndsWith(".d.ts")) || file.EndsWith(".css") || file.EndsWith(".js"))
                            throw new Error($"File {file} is empty");
                    }
                    // Check for stray .js and .css files without filelistJS/CSS.txt in Addons folder
                    if (file.EndsWith(".css") && ((!file.Contains(@"/node_modules/") && file.Contains(@"/Addons/")) || (!file.Contains(@"\\node_modules\\") && file.Contains(@"\\Addons\\")))) {
                        string dir = file;
                        int maxLevels = 3;
                        for (;;) {
                            dir = Path.GetDirectoryName(dir);
                            if (File.Exists(Path.Combine(dir, "filelistCSS.txt")))
                                break;
                            --maxLevels;
                            if (maxLevels == 0)
                                throw new Error($"File {file} found without filelistCSS.txt");
                        }
                    }
                    if (file.EndsWith(".js") && ((!file.Contains(@"/node_modules/") && file.Contains(@"/Addons/")) || (!file.Contains(@"\node_modules\") && file.Contains(@"\Addons\")))) {
                        string dir = file;
                        int maxLevels = 3;
                        for (;;) {
                            dir = Path.GetDirectoryName(dir);
                            if (File.Exists(Path.Combine(dir, "filelistJS.txt")))
                                break;
                            --maxLevels;
                            if (maxLevels == 0)
                                throw new Error($"File {file} found without FilelistJS.txt");
                        }
                    }

                    if (TargetZip != null) {
                        string relFile = Path.Combine(relPath, filename);
                        string searchFile = relFile.Replace("\\", "/");
                        ZipEntry found = (from e in TargetZip.Entries where e.FileName == searchFile select e).FirstOrDefault();
                        if (found == null) {
                            ZipEntry ze = TargetZip.AddFile(file);
                            ze.FileName = relFile;
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(TargetFolder)) {
                        string relFile = Path.Combine(relPath, filename);
                        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(TargetFolder, relFile)));
                        File.Copy(file, Path.Combine(TargetFolder, relFile));
                    }
                }
            }
            string[] dirs = Directory.GetDirectories(absPath);
            foreach (string dir in dirs) {
                bool exclude = false;
                string folder = Path.GetFileName(dir);
                if (ExcludeFolders != null) {
                    foreach (string excludeFolder in ExcludeFolders) {
                        if (LikeString(folder, excludeFolder)) {
                            exclude = true;
                            break;
                        }
                    }
                }
                if (!exclude) {
                    Console.WriteLine("Copying folder {0}", folder);
                    AddFilesToTargetAndRecurse(dir, Path.Combine(relPath, folder), ExcludeFiles, ExcludeFolders);
                }
            }
            if (files.Length == 0 && dirs.Length == 0) {
                // no files or folders, just add the folder in the ZIP file
                // some modules/data providers check folder existence to determine whether the module is installed
                if (TargetZip != null) {
                    TargetZip.AddDirectoryByName(relPath);
                    string absFolder = Path.Combine(SiteLocation, relPath);
                }
                if (!string.IsNullOrWhiteSpace(TargetFolder)) {
                    string absFolder = Path.Combine(TargetFolder, relPath);
                    Directory.CreateDirectory(absFolder);
                }
            }
        }
        private bool LikeString(string fileName, string pattern) {
            Regex re = new Regex($"^{pattern}$", RegexOptions.IgnoreCase);
            return re.IsMatch(fileName);
        }

        private void AddFilesToTargetFromFileList(string folder, List<string> ExcludeFiles = null, List<string> ExcludeFolders = null) {
            // Find all filelist*.txt files and extract folders that are used
            List<string> sourceFolders = new List<string>();
            List<string> allLists = new List<string>();
            if (IsMVC6) {
                allLists.AddRange(FindAllFileLists(Path.Combine(SiteLocation, "wwwroot", "Addons")));
                allLists.AddRange(FindAllFileLists(Path.Combine(SiteLocation, "wwwroot", "AddonsCustom")));
            } else {
                allLists.AddRange(FindAllFileLists(Path.Combine(SiteLocation, "Addons")));
                allLists.AddRange(FindAllFileLists(Path.Combine(SiteLocation, "AddonsCustom")));
            }
            allLists = allLists.Distinct().ToList().OrderBy((x) => x.Length).ToList();

            // eliminate subfolders if there are folders that contain them
            List<string> fileLists = new List<string>();
            foreach (string dir in allLists) {
                if ((from f in fileLists where dir.StartsWith(f) select f).FirstOrDefault() != null) {
                    // already have this folder
                } else {
                    fileLists.Add(dir);
                }
            }
            // add folders
            foreach (string fileList in fileLists) {
                if (fileList.StartsWith(folder))
                    AddAllFilesToTarget(fileList, ExcludeFiles: ExcludeFiles, ExcludeFolders: ExcludeFolders);
            }
        }
        internal class PathComparer : IEqualityComparer<string> {
            public bool Equals(string x, string y) {
                if (x == y) {
                    return true;
                } else {
                    if (x.StartsWith(y) || y.StartsWith(x))
                        return true;
                    else
                        return false;
                }
            }
            private List<string> Seen = new List<string>();
            public int GetHashCode(string s) {
                int i = Seen.IndexOf(s);
                if (i < 0) {
                    Seen.Add(s);
                    i = Seen.Count() - 1;
                }
                return i;
            }
        }

        private List<string> FindAllFileLists(string folder) {
            List<string> fileLists = new List<string>();
            if (!Directory.Exists(folder)) return fileLists;
            if (Path.GetFileName(folder) == "node_modules") return fileLists;
            if (Path.GetFileName(folder) == "bin") return fileLists;

            List<string> files = Directory.GetFiles(folder, "filelist*.txt").ToList();
            foreach (string file in files)
                fileLists.AddRange(ExtractContentPaths(file));

            List<string> folders = Directory.GetDirectories(folder).ToList();
            foreach (string f in folders)
                fileLists.AddRange(FindAllFileLists(f));
            return fileLists;
        }
        private List<string> ExtractContentPaths(string file) {

            string contentsFile = Path.GetFileNameWithoutExtension(file).ToLower();

            List<string> paths = new List<string>();
            List<string> lines = File.ReadAllLines(file).ToList();
            lines = (from l in lines where !l.Trim().StartsWith("#") select l).ToList();// remove comments
            foreach (string line in lines) {
                string path = line.Split(new char[] { ',' }).FirstOrDefault();

                if (path.StartsWith("MVC6 ")) {
                    if (!IsMVC6)
                        continue;
                    path = path.Substring(4).Trim();
                } else if (path.StartsWith("MVC5 ")) {
                    if (IsMVC6)
                        continue;
                    path = path.Substring(4).Trim();
                }

                if (!string.IsNullOrWhiteSpace(path)) {
                    path = path.Trim();

                    if (path.Contains("\\"))
                        throw new Error($"File {file} contains a \\ (backslash)");

                    if (contentsFile == "filelistdeploy") {
                        if (!path.StartsWith("/node_modules/") && !path.StartsWith("bower_components"))
                            throw new Error($"Only files/folders in node_modules or bower_components folders can be deployed");
                        string realPath = SiteLocation + FileToPhysical(path);
                        if (!Directory.Exists(realPath))
                            throw new Error($"File {file} contains reference to folder {realPath} that does not exist");
                        path = path.Substring(1);// remove leading '\'
                        paths.Add(FileToPhysical(path));
                        continue;
                    } else {
                        if (path.StartsWith("/node_modules/") || path.StartsWith("bower_components")) {
                            if (!path.EndsWith(".js") && !path.EndsWith(".css"))
                                throw new Error($"File {file} contains reference to {path} which isn't .js or .css");
                            path = Path.GetDirectoryName(path);
                            path = PhysicalToFile(path);
                            path = path.Replace("/{0}", ""); // for some composite paths like jqueryui themes
                            string realPath = SiteLocation + FileToPhysical(path);
                            if (!Directory.Exists(realPath))
                                throw new Error($"File {file} contains reference to folder {realPath} that does not exist");
                            path = path.Substring(1);// remove leading '/'
                            paths.Add(FileToPhysical(path));
                            continue;
                        }
                        if (path.StartsWith("Folder ")) {
                            path = path.Substring(6).Trim();
                            path = path.Replace("/{0}", ""); // for some composite paths like jqueryui themes
                            string realPath = SiteLocation + FileToPhysical(path);
                            if (!Directory.Exists(realPath))
                                throw new Error($"File {file} contains reference to folder {realPath} that does not exist");
                            path = path.Substring(1);// remove leading '\'
                            paths.Add(FileToPhysical(path));
                            continue;
                        }
                        if (path.Contains("node_modules") || path.Contains("bower_components"))
                            throw new Error($"File {file} contains reference to node_modules which should start with \"\\node_modules\" or \"\\bower_components\"");
                    }
                }
            }
            return paths;
        }
        public static string FileToPhysical(string file) {
#if MVC6
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                file = file.Replace('/', '\\');
#else
            file = file.Replace('/', '\\');
#endif
            return file;
        }
        public static string PhysicalToFile(string file) {
#if MVC6
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                file = file.Replace('\\', '/');
#else
            file = file.Replace('\\', '/');
#endif
            return file;
        }
        private List<string> AllowSubstitutionFiles = new List<string> {
            "data/appsettings.json",
            "data/appsettings.prod.json",
            "web.config",
            "web.prod.config",
        };

        private void AddFileToTarget(string relFile, string newName = null) {
            if (newName == null) newName = relFile;
            string absFile = Path.Combine(SiteLocation, relFile);
            Console.WriteLine("Copying {0} from {1}", newName, absFile);

            if (BlueGreenDeploy != BlueGreenDeployEnum.None && AllowSubstitutionFiles.Contains(PhysicalToFile(newName).ToLower())) {
                string contents = File.ReadAllText(absFile);
                contents = contents.Replace("{bluegreen}", BlueGreenDeploy == BlueGreenDeployEnum.Blue ? "blue" : "green");
                contents = contents.Replace("{BLUEGREEN}", BlueGreenDeploy == BlueGreenDeployEnum.Blue ? "Blue" : "Green");
                contents = contents.Replace("{-BLUEGREEN}", BlueGreenDeploy == BlueGreenDeployEnum.Blue ? "-Blue" : "-Green");
                contents = contents.Replace("{BLUEGREEN-OTHER}", BlueGreenDeploy == BlueGreenDeployEnum.Blue ? "Green" : "Blue");
                contents = contents.Replace("{-BLUEGREEN-OTHER}", BlueGreenDeploy == BlueGreenDeployEnum.Blue ? "-Green" : "-Blue");
                if (TargetZip != null) {
                    ZipEntry ze = TargetZip.AddEntry(newName, contents);
                }
                if (!string.IsNullOrWhiteSpace(TargetFolder)) {
                    File.WriteAllText(Path.Combine(TargetFolder, newName), contents);
                }
            } else {
                if (TargetZip != null) {
                    ZipEntry ze = TargetZip.AddFile(absFile);
                    ze.FileName = newName;
                }
                if (!string.IsNullOrWhiteSpace(TargetFolder)) {
                    File.Copy(absFile, Path.Combine(TargetFolder, newName));
                }
            }
        }
        private void AddConfigFileToTarget(string relFile, string newName) {
            string f, fileName;
            if (!string.IsNullOrWhiteSpace(ConfigParm)) {
                fileName = string.Format(relFile, ConfigParm);
                f = Path.Combine(SiteLocation, fileName);
                if (File.Exists(f)) {
                    AddFileToTarget(fileName, newName);
                    return;
                }
            }
            fileName = string.Format(relFile, "");
            f = Path.Combine(SiteLocation, fileName);
            if (File.Exists(f)) {
                AddFileToTarget(fileName, newName);
                return;
            }
            throw new Error("Config file {0} not found", relFile);
        }
        private void UploadFile(Upload upload) {
            Console.WriteLine("Uploading {0} ...", upload.SourceFile);

            FtpWebRequest request = (FtpWebRequest)WebRequest.Create(upload.FtpAddress + upload.TargetFile);
            request.Method = WebRequestMethods.Ftp.UploadFile; //MakeDirectory
            request.UsePassive = false;
            request.Credentials = new NetworkCredential(upload.UserName, upload.Password);
            request.Timeout = 30 * 60 * 1000; // 30 minutes
            request.ReadWriteTimeout = 10 * 60 * 1000; // 10 minutes
            request.UseBinary = true;
            request.KeepAlive = false;

            byte[] fileContents;
            if (BlueGreenDeploy != BlueGreenDeployEnum.None && !ReservedFileType(upload.SourceFile)) {
                string contents = File.ReadAllText(upload.SourceFile);
                contents = contents.Replace("{BLUEGREEN}", BlueGreenDeploy == BlueGreenDeployEnum.Blue ? "Blue" : "Green");
                contents = contents.Replace("{-BLUEGREEN}", BlueGreenDeploy == BlueGreenDeployEnum.Blue ? "-Blue" : "-Green");
                contents = contents.Replace("{BLUEGREEN-OTHER}", BlueGreenDeploy == BlueGreenDeployEnum.Blue ? "Green" : "Blue");
                contents = contents.Replace("{-BLUEGREEN-OTHER}", BlueGreenDeploy == BlueGreenDeployEnum.Blue ? "-Green" : "-Blue");
                fileContents = System.Text.Encoding.UTF8.GetBytes(contents);
            } else {
                fileContents = File.ReadAllBytes(upload.SourceFile);
            }
            request.ContentLength = fileContents.Length;

            // If the following fails, you probably are using a server name that is not in the hosts file or is not resolvable via DNS lookup
            // (this can happen when you access a Windows server by name on your local network - add it to your hosts file to fix this case)
            Stream requestStream = request.GetRequestStream();
            requestStream.Write(fileContents, 0, fileContents.Length);
            requestStream.Close();

            FtpWebResponse response = (FtpWebResponse)request.GetResponse();
            Console.WriteLine("Upload File Complete, status {0}", response.StatusDescription);
            response.Close();
        }

        private bool ReservedFileType(string file) {
            string ext = Path.GetExtension(file);
            if (ext == ".dll" || ext == ".exe" || ext == ".pdb" || ext == ".manifest" || ext == ".zip")
                return true;
            return false;
        }

        private void UploadDirectory(UploadFolder uploadFolder, bool makeFolder = true) {
            Console.WriteLine("Uploading folder {0} ...", uploadFolder.SourceFolder);

            if (makeFolder) {
                FtpWebRequest request = (FtpWebRequest)WebRequest.Create(uploadFolder.FtpAddress + uploadFolder.TargetFolder);
                request.Method = WebRequestMethods.Ftp.MakeDirectory;
                request.Credentials = new NetworkCredential(uploadFolder.UserName, uploadFolder.Password);
                try {
                    using (var resp = (FtpWebResponse)request.GetResponse()) {
                        Console.WriteLine(resp.StatusCode);
                    }
                } catch (Exception) { }
            }
            // transfer files
            {
                foreach (string filePath in Directory.GetFiles(uploadFolder.SourceFolder)) {
                    string file = Path.GetFileName(filePath);
                    Upload upload = new Upload {
                        FtpAddress = uploadFolder.FtpAddress,
                        Password = uploadFolder.Password,
                        UserName = uploadFolder.UserName,
                        TargetFile = Path.Combine(uploadFolder.TargetFolder, file),
                        SourceFile = filePath,
                    };
                    UploadFile(upload);
                }
                foreach (string dir in Directory.GetDirectories(uploadFolder.SourceFolder)) {
                    UploadFolder uf = new UploadFolder() {
                        FtpAddress = uploadFolder.FtpAddress,
                        Password = uploadFolder.Password,
                        UserName = uploadFolder.UserName,
                        TargetFolder = Path.Combine(uploadFolder.TargetFolder, Path.GetFileName(dir)),
                        SourceFolder = dir,
                    };
                    UploadDirectory(uf, true);
                }
            }
        }

        // RESTORE
        // RESTORE
        // RESTORE

        private void ExtractAllFiles() {
            //if (File.Exists(Path.Combine(BaseDirectory, "UNZIPDONE"))) {
            //    Console.WriteLine("Skipped extracting Zip file");
            //} else {
            Console.WriteLine("Extracting Zip file...");
            ZipFile zipFile = ZipFile.Read(ZipFileName);
            zipFile.ExtractAll(Path.Combine(BaseDirectory, UNZIPFOLDER));
            //File.WriteAllText(Path.Combine(BaseDirectory, "UNZIPDONE"), "Done");
            //}
        }
        private void SetMaintenanceMode() {
            if (!string.IsNullOrWhiteSpace(MaintenancePage)) {
                Console.WriteLine($"Setting Maintenance Mode... {MaintenancePage}");
                Console.WriteLine("Setting Maintenance Mode...");
                string filename = Path.Combine(BaseDirectory, UNZIPFOLDER, MAINTENANCEFOLDER, MaintenancePage);
                if (File.Exists(filename)) {
                    if (Directory.Exists(SiteLocation)) {
                        string targetFile = Path.Combine(SiteLocation, APPOFFLINEPAGE);
                        File.Delete(targetFile);
                        File.Copy(filename, targetFile);
                    }
                    return;
                }
                filename = Path.Combine(BaseDirectory, UNZIPFOLDER, "wwwroot", MAINTENANCEFOLDER, MaintenancePage);
                if (File.Exists(filename)) {
                    if (Directory.Exists(Path.Combine(SiteLocation, "wwwroot"))) {
                        string targetFile = Path.Combine(SiteLocation, APPOFFLINEPAGE);
                        File.Delete(targetFile);
                        File.Copy(filename, targetFile);
                    }
                    return;
                }
                throw new Error("Maintenance page {0} not found at {1} or {2}", MaintenancePage, Path.Combine(BaseDirectory, UNZIPFOLDER, MAINTENANCEFOLDER), Path.Combine(BaseDirectory, UNZIPFOLDER, "wwwroot", MAINTENANCEFOLDER));
            }
        }
        private void SetUpdateIndicator() {
            if (UpdateIndicator) {
                Console.WriteLine("Setting Update Indicator...");
                if (Directory.Exists(SiteLocation)) {
                    string targetFile = Path.Combine(SiteLocation, UPDATEINDICATORFILE);
                    File.WriteAllText(targetFile, "Update");
                }
            }
        }
        private void ClearMaintenanceMode() {
            if (BlueGreenDeploy == BlueGreenDeployEnum.None) {
                Console.WriteLine("MAKE SURE YOU REMOVE the page /App_Offline.htm and P:YetaWF_Core:LOCKED-FOR-IP (if present) in your site's AppSettings.json file");

                //Console.WriteLine("Clearing Maintenance Mode...");
                //string targetFile = Path.Combine(SiteLocation, APPOFFLINEPAGE);
                //File.Delete(targetFile);
            }
        }

        private void PerformRestoreDBs() {

            if (BlueGreenDeploy == BlueGreenDeployEnum.None) {
                Console.WriteLine("You could restart IIS/SQL Server, run SQL procs, etc. if necessary - the site is already in maintenance mode (displaying Maintenance page)");
                do {
                    Console.WriteLine("Hit Return to continue...");
                } while (Console.ReadKey().KeyChar != '\r');
            }
            foreach (DB db in RestoreDBs) {
                PerformRestoreDB(db, "");
            }
        }
        private void PerformRestoreDB(DB db, string prefix) {

            string dbName = Action == CopyAction.Backup ? db.NameDev : db.NameProd;
            Console.WriteLine("Restoring DB {0}", dbName);

            string dbFileName = Path.Combine(BaseDirectory, UNZIPFOLDER, DBFOLDER, string.Format("{0}.bak", db.NameDev));

            string path = Path.GetDirectoryName(dbFileName);

            // Connection
            ServerConnection conn = new ServerConnection();
            conn.ServerInstance = db.Server;
            Server server = new Server(conn);

            // Restore
            Restore restore = new Restore();
            restore.Action = RestoreActionType.Database;
            restore.Database = dbName;
            restore.NoRecovery = false;
            restore.ReplaceDatabase = true;
            restore.Devices.AddDevice(dbFileName, DeviceType.File);

            string dataFolder = Path.Combine(SiteLocation, "..", DBDATAFOLDER);
            Directory.CreateDirectory(dataFolder);

            RelocateFile dataFile = new RelocateFile();
            dataFile.LogicalFileName = restore.ReadFileList(server).Rows[0][0].ToString();
            Console.WriteLine("dataFile.LogicalFileName {0}", restore.ReadFileList(server).Rows[0][0].ToString());
            dataFile.PhysicalFileName = Path.Combine(dataFolder, prefix + dbName + ".mdf");

            RelocateFile logFile = new RelocateFile();
            logFile.LogicalFileName = restore.ReadFileList(server).Rows[1][0].ToString();
            Console.WriteLine("dataFile.LogicalFileName {0}", restore.ReadFileList(server).Rows[1][0].ToString());
            logFile.PhysicalFileName = Path.Combine(dataFolder, prefix + dbName + ".ldf");

            restore.RelocateFiles.Add(dataFile);
            restore.RelocateFiles.Add(logFile);

            restore.SqlRestore(server);

            conn.Disconnect();
        }
        private void CopyAllFilesToSite() {

            Console.WriteLine("Copying files...");

            // Add explicit folders
            int folderCount = 0;
            foreach (Folder folder in Folders) {
                string unzipPath = Path.Combine(BaseDirectory, UNZIPFOLDER, "Folder_" + folderCount.ToString());
                AddFilesToSiteAndRecurse(unzipPath, folder.Path);
                ++folderCount;
            }

            // Add folders
            if (IsMVC6) {
                AddAllFilesToSite(Path.Combine("wwwroot", "Addons"));
                AddAllFilesToSite(Path.Combine("wwwroot", "AddonsCustom"));
                AddAllFilesToSite(Path.Combine("wwwroot", MAINTENANCEFOLDER));
                AddAllFilesToSite(Path.Combine("wwwroot", "lib"));
                AddAllFilesToSite(Path.Combine("wwwroot", "SiteFiles"));
                AddAllFilesToSite(Path.Combine("wwwroot", "Addons"));
                AddAllFilesToSite(Path.Combine("wwwroot", "Addons"));
                //AddAllFilesToSite(Path.Combine("wwwroot", "Vault"));
                AddFileToSite(Path.Combine("wwwroot", "logo.jpg"));
                AddFileToSite(Path.Combine("wwwroot", "robots.txt"));

                DeleteFolder(Path.Combine(SiteLocation, "Areas"));
                AddAllFilesToSite("bower_components");
                AddAllFilesToSite(DATAFOLDER, ExcludeFiles: new List<string> { @".*\.mdf", @".*\.ldf" });
                AddAllFilesToSite("Localization");
                AddAllFilesToSite("LocalizationCustom");
                AddAllFilesToSite("node_modules");
                AddAllFilesToSite("Sites", ExcludeFiles: new List<string> { @"Backup .*\.zip" });
                AddAllFilesToSite("SiteTemplates");
                //AddAllFilesToSite("Vault"); // is never copied

                AddFilesToSite(@"*.dll", ExcludeFolders: new List<string> { @"wwwroot" });
                AddFilesToSite(@"*.exe", ExcludeFolders: new List<string> { @"wwwroot" });
                AddFilesToSite(@"*.pdb", ExcludeFolders: new List<string> { @"wwwroot" });
                AddFilesToSite(@"*.json", ExcludeFolders: new List<string> { @"wwwroot" });
                AddFilesToSite(@"*.config", ExcludeFolders: new List<string> { @"wwwroot" });

                Directory.CreateDirectory(Path.Combine(SiteLocation, "logs")); // make a log folder

            } else {
                AddAllFilesToSite("Addons");
                AddAllFilesToSite("AddonsCustom");
                DeleteFolder(Path.Combine(SiteLocation, "Areas"));
                AddAllFilesToSite("bower_components");
                AddAllFilesToSite("bin");
                AddAllFilesToSite(DATAFOLDER, ExcludeFiles: new List<string> { @".*\.mdf", @".*\.ldf" });
                AddAllFilesToSite(MAINTENANCEFOLDER);
                AddAllFilesToSite("Localization");
                AddAllFilesToSite("LocalizationCustom");
                AddAllFilesToSite("node_modules");
                AddAllFilesToSite("Sites", ExcludeFiles: new List<string> { @"Backup .*\.zip" });
                AddAllFilesToSite("SiteFiles");
                AddAllFilesToSite("SiteTemplates");
                //AddAllFilesToSite("Vault"); // is never copied
                AddFileToSite("logo.jpg");
                AddFileToSite("Global.asax");
                AddFileToSite("robots.txt");
                AddFileToSite("Web.config");
                AddAllFilesToSite("Content");// used to remove target folder - we don't distribute files in this folder
                AddAllFilesToSite("Scripts");// used to remove target folder - we don't distribute files in this folder
            }

            string deployMarker = Path.Combine(SiteLocation, "node_modules");
            Directory.SetLastWriteTimeUtc(deployMarker, DateTime.UtcNow);

            Console.WriteLine("All files copied");
        }
        private void AddFilesToSite(string match, List<string> ExcludeFolders = null) {
            string unzipPath = Path.Combine(BaseDirectory, UNZIPFOLDER);
            string targetPath = Path.Combine(SiteLocation);
            AddFilesToSiteAndRecurse(unzipPath, targetPath, match, ExcludeFolders: ExcludeFolders);
        }
        private void AddAllFilesToSite(string folder, List<string> ExcludeFiles = null) {
            string unzipPath = Path.Combine(BaseDirectory, UNZIPFOLDER, folder);
            string targetPath = Path.Combine(SiteLocation, folder);
            if (ExcludeFiles != null) {
                RemoveFolderContents(targetPath, ExcludeFiles);
            } else {
                DeleteFolder(targetPath);
            }
            AddFilesToSiteAndRecurse(unzipPath, targetPath);
        }
        private void RemoveFolderContents(string targetPath, List<string> ExcludeFiles = null) {
            if (!Directory.Exists(targetPath)) return;
            string[] files = Directory.GetFiles(targetPath);
            foreach (string file in files) {
                bool exclude = false;
                foreach (string excludeFile in ExcludeFiles) {
                    if (LikeString(file, excludeFile)) {
                        exclude = true;
                        break;
                    }
                }
                if (!exclude) {
                    Console.WriteLine("Removing file {0}", file);
                    File.Delete(file);
                }
            }
            string[] dirs = Directory.GetDirectories(targetPath);
            foreach (string dir in dirs) {
                RemoveFolderContents(dir, ExcludeFiles);
                if (Directory.GetFiles(dir).Count() == 0 && Directory.GetDirectories(dir).Count() == 0) {
                    Console.WriteLine("Removing folder {0}", dir);
                    Directory.Delete(dir, false);
                }
            }
        }

        private void AddFilesToSiteAndRecurse(string unzipPath, string targetPath, string match = "*.*", List<string> ExcludeFolders = null) {
            if (!Directory.Exists(unzipPath)) return;
            string[] files = Directory.GetFiles(unzipPath, match);
            foreach (string file in files) {
                string filename = Path.GetFileName(file);
                string unzipFile = Path.Combine(unzipPath, filename);
                AddFileToSite(unzipFile, Path.Combine(targetPath, filename));
            }
            string[] dirs = Directory.GetDirectories(unzipPath);
            foreach (string dir in dirs) {
                if (ExcludeFolders != null && ExcludeFolders.Contains(Path.GetFileName(dir))) return;
                string path = Path.Combine(targetPath, Path.GetFileName(dir));
                Console.WriteLine("Creating folder {0}", path);
                Directory.CreateDirectory(path);
                AddFilesToSiteAndRecurse(dir, path);
            }
        }
        private void AddFileToSite(string unzipFile, string targetFile) {
            Console.WriteLine("Copying {0}", targetFile);
            string path = Path.GetDirectoryName(targetFile);
            Directory.CreateDirectory(path);
            File.Copy(unzipFile, targetFile, true);
        }
        private void AddFileToSite(string file) {
            string unzipFile = Path.Combine(BaseDirectory, UNZIPFOLDER, file);
            string targetFile = Path.Combine(SiteLocation, file);
            Console.WriteLine("Copying {0}", targetFile);
            File.Delete(targetFile);
            File.Copy(unzipFile, targetFile, true);
        }
        private void RunCommand(RunCmd run) {

            Process p = new Process();

            Console.WriteLine($"{run.Command}");

            p.StartInfo.FileName = "cmd.exe";
            p.StartInfo.Arguments = "/C " + run.Command;

            p.StartInfo.CreateNoWindow = true;
            p.EnableRaisingEvents = true;

            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.UseShellExecute = false;
            p.OutputDataReceived += new DataReceivedEventHandler(process_OutputDataReceived);
            p.Exited += new EventHandler(process_Exited);

            if (p.Start()) {

                p.BeginOutputReadLine();
                p.WaitForExit();

            } else {
                throw new Error(string.Format("Failed to start {0}", run.Command));
            }

            if (p.ExitCode != 0) {
                if (!run.IgnoreError)
                    throw new Error(string.Format("{0} failed", run.Command));
            }
        }

        private void process_Exited(object sender, EventArgs e) {
            Console.WriteLine("\r\nDone\r\n");
        }

        void process_OutputDataReceived(object sender, DataReceivedEventArgs e) {
            Console.WriteLine(e.Data);
        }
    }
}
