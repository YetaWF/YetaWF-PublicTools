/* Copyright © 2020 Softel vdm, Inc. - https://yetawf.com/Documentation/YetaWF/Licensing */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
#if MVC6
using Microsoft.Data.SqlClient;
#else
using System.Data.SqlClient;
#endif

namespace Softelvdm.Tools.DeploySite {

    public partial class Backup {

        public const string TEMPFOLDER = "TEMP";
        public const string PACKAGEMAP = "PackageMap.txt";
        public const string DONTDEPLOY = "dontdeploy.txt";

        private string BackupTargetFolder;// type = folder
        private BackupZipFile BackupTargetZip;// type = zip
        private string BackupTempFolder;

        private bool IsMVC6 { get; set; }

        private string BackupSiteLocation;

        public Backup() { }

        public void PerformBackup() {

            if (Program.YamlData.Deploy.Type == "zip") {

            } else if (Program.YamlData.Deploy.Type == "folder") {

            } else
                throw new Error($"Invalid deploy type {Program.YamlData.Deploy.Type} - only zip or folder are supported");

            BackupSiteLocation = Path.Combine(Program.YamlData.Deploy.BaseFolder, "Website");
            if (!Directory.Exists(BackupSiteLocation))
                throw new Error($"Website folder {BackupSiteLocation} not found");

            if (Directory.Exists(Path.Combine(BackupSiteLocation, Program.MARKERMVC6)))
                IsMVC6 = true;
            if (IsMVC6)
                Console.WriteLine("ASP.NET Core Site");
            else
                Console.WriteLine("ASP.NET 4 Site");

            // clean temp folder
            string folder = Path.Combine(Program.YamlData.Deploy.BaseFolder, TEMPFOLDER);
            IOHelper.DeleteFolder(folder);

            BackupTempFolder = folder;

            if (Program.YamlData.Deploy.Type == "zip") {

                string to = Path.Combine(Program.YamlData.Deploy.BaseFolder, Program.YamlData.Deploy.To);
                string path = Path.GetDirectoryName(to);
                Directory.CreateDirectory(path);
                File.Delete(to);

                BackupTargetZip = new BackupZipFile(to);

            } else {
                // clean target folder
                BackupTargetFolder = Path.Combine(Program.YamlData.Deploy.BaseFolder, Program.YamlData.Deploy.To);

                IOHelper.DeleteFolder(BackupTargetFolder);
                Directory.CreateDirectory(BackupTargetFolder);
            }

            // backup all dbs
            BackupDBs();

            // Add all files to zip file
            CopyAllFilesToTarget();

            // Upload everything
            UploadAll();

            // Local copies
            LocalCopy();

            IOHelper.DeleteFolder(BackupTempFolder);
        }

        private void BackupDBs() {
            if (Program.YamlData.Databases != null) {
                foreach (Database db in Program.YamlData.Databases) {
                    if (!string.IsNullOrWhiteSpace(db.Bacpac)) {
                        if (db.DevDB != null || db.DevServer != null || db.DevUsername != null || db.DevPassword != null)
                            throw new Error($"Can't mix bacpac and development DB information ({db.Bacpac})");
                        if (Program.YamlData.Site.Sqlcmd == null)
                            throw new Error($"Site.Sqlpackage in yaml file required for bacpac support");
                        if (Program.YamlData.Site.Sqlpackage == null)
                            throw new Error($"Site.Sqlpackage in yaml file required for bacpac support");
                        CopyBacpac(db.Bacpac);
                    } else if (!string.IsNullOrWhiteSpace(db.ubackup)) {
                        if (db.DevDB != null || db.DevServer != null || db.DevUsername != null || db.DevPassword != null)
                            throw new Error($"Can't mix ubackup and development DB information ({db.ubackup})");
                        // nothing to copy
                    } else {
                        if (db.DevDB == null || db.DevServer == null || db.DevUsername == null || db.DevPassword == null)
                            throw new Error($"Missing development DB information ({db.DevDB})");
                        BackupDB(db);
                    }
                }
            }
        }

        private void CopyBacpac(string bacpac) {

            Console.WriteLine($"Copying {bacpac} to {Program.DBFOLDER} folder");

            string bacpacFile = Path.Combine(Program.YamlData.Deploy.BaseFolder, bacpac);

            string dbFileName = Path.Combine(BackupTempFolder, Program.DBFOLDER, bacpac);
            string path = Path.GetDirectoryName(dbFileName);
            Directory.CreateDirectory(path);

            File.Copy(bacpacFile, dbFileName);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "None")]
        private void BackupDB(Database db) {

            Console.WriteLine("Backing up DB {0}", db.DevDB);

            string dbFileName = Path.Combine(BackupTempFolder, Program.DBFOLDER, $"{db.DevDB}.bak");
            string path = Path.GetDirectoryName(dbFileName);
            Directory.CreateDirectory(path);

            // Connection
            string connectionString = String.Format("Data Source={0};Initial Catalog={1};User ID={2};Password={3};", db.DevServer, db.DevDB, db.DevUsername, db.DevPassword);

            using (SqlConnection sqlConnection = new SqlConnection(connectionString)) {

                string SQLBackupQuery = $"BACKUP DATABASE [{db.DevDB}] TO DISK = '{dbFileName}'";

                sqlConnection.Open();
                using (SqlCommand cmd = new SqlCommand(SQLBackupQuery, sqlConnection)) {
                    cmd.ExecuteNonQuery();
                }
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

            // Add Dbs
            AddFilesToTargetAndRecurse(Path.Combine(BackupTempFolder, Program.DBFOLDER), Program.DBFOLDER, Optional: true);

            // Add folders
            if (IsMVC6) {

                if (string.IsNullOrWhiteSpace(Program.YamlData.Deploy.From)) {
                    throw new Error("The published output path created by Visual Studio Publish or dotnet publish must be defined using Deploy:From and is missing");
                }

                AddPublishOutput();
                AddPublishOutputFiles("*.deps.json");
                AddPublishOutputFiles("*.runtimeconfig.json");
                AddPublishOutputFiles("*.dll.config");
                AddPublishOutputFiles("*.exe.config");

                AddAllFilesToTarget(Program.DATAFOLDER,
                    ExcludeFiles: new List<string> { @"AppSettings\..*", @"NLog\..*", @"InitialInstall\.txt", @"UpgradeLogFile\.txt", @"StartupLogFile\.txt", @".*\.mdf", @".*\.ldf" },
                    ExcludeFolders: new List<string>() { "Sites" });
                AddConfigFileToTarget(Path.Combine(Program.DATAFOLDER, "AppSettings.{0}json"), Path.Combine(Program.DATAFOLDER, "AppSettings.json"));
                AddConfigFileToTarget(Path.Combine(Program.DATAFOLDER, "NLog.{0}config"), Path.Combine(Program.DATAFOLDER, "NLog.config"), Optional: true);
                if (Program.YamlData.Deploy.Localization) {
                    AddAllFilesToTarget("Localization");
                    AddAllFilesToTarget("LocalizationCustom", Optional: true);
                }
                AddAddonsFolders(Path.Combine("wwwroot", "Addons"));
                AddFilesToTargetFromFileList("node_modules", ExcludeFiles: FileListExcludedFiles, ExcludeFolders: FileListExcludedFolders);
                AddFilesToTargetFromFileList("bower_components", ExcludeFiles: FileListExcludedFiles, ExcludeFolders: FileListExcludedFolders);
                AddAllFilesToTarget("Sites", ExcludeFiles: new List<string> { @"Backup .*\.zip" }, ExcludeFolders: new List<string> { "TempFiles" });
                if (Program.YamlData.Deploy.SiteTemplates)
                    AddAllFilesToTarget("SiteTemplates", Optional: true);
                //AddAllFilesToPublishFolder("VaultPrivate");
                AddConfigFileToTarget("app.{0}config", "app.config");
                AddConfigFileToTarget("hosting.{0}json", "hosting.json", Optional: true);
                AddConfigFileToTarget("Web.{0}config", "Web.config");

                AddAllFilesToTarget(Path.Combine("wwwroot", "AddonsCustom"), Optional: true);
                AddAllFilesToTarget(Path.Combine("wwwroot", "Maintenance"));
                AddAllFilesToTarget(Path.Combine("wwwroot", "SiteFiles"), Optional: true);
                //AddAllFilesToTarget(Path.Combine("wwwroot", "Vault"));
                AddFileToTarget(Path.Combine("wwwroot", "logo.jpg"), Optional: true);
                AddFileToTarget(Path.Combine("wwwroot", "robots.txt"));

            } else {

                AddAddonsFolders(Path.Combine("wwwroot", "Addons"));
                AddAllFilesToTarget("AddonsCustom", Optional: true);
                if (Program.YamlData.Deploy.Debug)
                    AddAllFilesToTarget("bin", ExcludeFiles: new List<string> { @".*\.xml" });
                else
                    AddAllFilesToTarget("bin", ExcludeFiles: new List<string> { @".*\.pdb", @".*\.xml" });
                AddFilesToTargetFromFileList("node_modules", ExcludeFiles: FileListExcludedFiles, ExcludeFolders: FileListExcludedFolders);
                AddFileToTarget(Path.Combine("node_modules", "Web.config"));
                AddFilesToTargetFromFileList("bower_components", ExcludeFiles: FileListExcludedFiles, ExcludeFolders: FileListExcludedFolders);
                AddFileToTarget(Path.Combine("bower_components", "Web.config"));
                AddAllFilesToTarget(Program.DATAFOLDER, ExcludeFiles: new List<string> { @"AppSettings\..*", @"NLog\..*", @"UpgradeLogFile\.txt", @".*\.mdf", @".*\.ldf" });
                AddConfigFileToTarget(Path.Combine(Program.DATAFOLDER, "AppSettings.{0}json"), Path.Combine(Program.DATAFOLDER, "AppSettings.json"));
                AddConfigFileToTarget(Path.Combine(Program.DATAFOLDER, "NLog.{0}config"), Path.Combine(Program.DATAFOLDER, "NLog.config"), Optional: true);
                AddAllFilesToTarget(Program.MAINTENANCEFOLDER);
                if (Program.YamlData.Deploy.Localization) {
                    AddAllFilesToTarget("Localization");
                    AddAllFilesToTarget("LocalizationCustom", Optional: true);
                }
                AddAllFilesToTarget("SiteFiles", Optional: true);
                AddAllFilesToTarget("Sites", ExcludeFiles: new List<string> { @"Backup .*\.zip" }, ExcludeFolders: new List<string> { @"TempFiles" });
                if (Program.YamlData.Deploy.SiteTemplates)
                    AddAllFilesToTarget("SiteTemplates", Optional: true);
                AddFileToTarget("Global.asax");
                AddFileToTarget("logo.jpg", Optional: true);
                AddFileToTarget("robots.txt");
                AddConfigFileToTarget("Web.{0}config", "Web.config");
                //AddAllFilesToTarget("Vault");
            }

            if (BackupTargetZip != null) {
                Console.WriteLine("Creating Zip file...");
                BackupTargetZip.Save();
                BackupTargetZip.Dispose();
                Console.WriteLine("Zip file completed");
            }
        }

        private void AddPublishOutput() {
            AddFilesToTargetAndRecurse(Program.YamlData.Deploy.From, "", ExcludeFiles: new List<string> { @".*\.json", @".*\.config" }, ExcludeFolders: new List<string> { @"wwwroot" });
        }
        private void AddPublishOutputFiles(string match) {
            string[] files = Directory.GetFiles(Program.YamlData.Deploy.From, match);
            foreach (string file in files) {
                string filename = Path.GetFileName(file);
                Console.WriteLine("Copying {0}", file);
                string relFile = Path.Combine("", filename);
                if (BackupTargetZip != null) {
                    BackupTargetZip.AddFile(file, relFile);
                }
                if (BackupTargetFolder != null) {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(BackupTargetFolder, relFile)));
                    File.Copy(file, Path.Combine(BackupTargetFolder, relFile));
                }
            }
        }

        private void AddAddonsFolders(string addonsFolder) {
            // Check all addons folders against PackageMap.txt and only copy folders that are referenced
            string packageMap = File.ReadAllText(Path.Combine(BackupSiteLocation, Program.DATAFOLDER, PACKAGEMAP));
            List<string> domains = (from d in Directory.GetDirectories(Path.Combine(BackupSiteLocation, addonsFolder)) select Path.GetFileName(d)).ToList();
            foreach (string domain in domains) {
                string domainFolder = Path.Combine(BackupSiteLocation, addonsFolder, domain);
                List<string> products = (from d in Directory.GetDirectories(domainFolder) select Path.GetFileName(d)).ToList();
                foreach (string product in products) {

                    // some product names could look like this: YetaWF.Forms.MyName (more than 2 segments)
                    // in which case we only use the last segment as product name
                    string prodName = product;
                    int ix = product.LastIndexOf('.');
                    if (ix >= 0)
                        prodName = product.Substring(ix + 1);
                    string productFolder = Path.Combine(BackupSiteLocation, addonsFolder, domain, prodName);

                    bool remove = true;
                    Regex rePackage = new Regex($@"^{domain}(\.[^ ]*)*\.{prodName} ", RegexOptions.Multiline); // (note trailing space)
                    if (rePackage.IsMatch(packageMap))
                        remove = false;
                    //if (remove) {
                    //    // Special case for SQLDyn (folder is SQLDyn but package is YetaWF.DataProvider.SQL)
                    //    if (domain == "YetaWF.DataProvider" && prodName == "SQLDyn") {
                    //        rePackage = new Regex($@"^YetaWF(\.[^ ]*)*\.SQL ", RegexOptions.Multiline); // (note trailing space)
                    //        if (rePackage.IsMatch(packageMap))
                    //            remove = false;
                    //    }
                    //}
                    if (remove) {
                        // some packages use Softelvdm.{product} in package map but are located at YetaWF.{product} so allow for that
                        rePackage = new Regex($@"^Softelvdm(\.[^ ]*)*\.{prodName} ", RegexOptions.Multiline); // (note trailing space)
                        if (domain == "YetaWF" && rePackage.IsMatch(packageMap))
                            remove = false;
                    }
                    if (remove)
                        Directory.Delete(productFolder, false);// remove symlink
                }
            }
            AddAllFilesToTarget(addonsFolder);
        }

        private void AddAllFilesToTarget(string folder, List<string> ExcludeFiles = null, List<string> ExcludeFolders = null, bool Optional = false) {
            string absPath = Path.Combine(BackupSiteLocation, folder);
            string relPath = folder;
            AddFilesToTargetAndRecurse(absPath, relPath, ExcludeFiles, ExcludeFolders, Optional);
        }
        private void AddFilesToTargetAndRecurse(string absPath, string relPath, List<string> ExcludeFiles = null, List<string> ExcludeFolders = null, bool Optional = false) {
            if (ExcludeFiles == null)
                ExcludeFiles = new List<string>();
            ExcludeFiles.Add(@".*\.lastcodeanalysissucceeded");
            if (!Program.YamlData.Deploy.Debug)
                ExcludeFiles.Add(@".*\.pdb");
            ExcludeFiles.Add(@".*\.d\.ts");
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
                    if (length <= 3 && !file.Contains("node_modules\\") && !file.Contains("node_modules/")) {
                        if ((file.EndsWith(".ts") && !file.EndsWith(".d.ts")) || file.EndsWith(".css") || file.EndsWith(".js"))
                            throw new Error($"File {file} is empty");
                    }
                    // Check for stray .js and .css files without filelistJS/CSS.txt in Addons folder
                    if (file.EndsWith(".css") && ((!file.Contains(@"/node_modules/") && file.Contains(@"/Addons/")) || (!file.Contains(@"\\node_modules\\") && file.Contains(@"\\Addons\\")))) {
                        string dir = file;
                        int maxLevels = 3;
                        for (; ; ) {
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
                        for (; ; ) {
                            dir = Path.GetDirectoryName(dir);
                            if (File.Exists(Path.Combine(dir, "filelistJS.txt")))
                                break;
                            --maxLevels;
                            if (maxLevels == 0)
                                throw new Error($"File {file} found without FilelistJS.txt");
                        }
                    }

                    string relFile = Path.Combine(relPath, filename);
                    string searchFile = BackupZipFile.CleanFileName(relFile);//$$$$verify
                    if (BackupTargetZip != null) {
                        bool found = (from e in BackupTargetZip.Entries where e.RelativeName == searchFile select e).Any();
                        if (!found)
                            BackupTargetZip.AddFile(file, relFile);
                    }
                    if (BackupTargetFolder != null) {
                        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(BackupTargetFolder, relFile)));
                        File.Copy(file, Path.Combine(BackupTargetFolder, relFile));
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
                if (BackupTargetZip != null) {
                    //$$$$BackupTargetZip.AddDirectoryByName(relPath);
                }
                if (BackupTargetFolder != null) {
                    string absFolder = Path.Combine(BackupTargetFolder, relPath);
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
                allLists.AddRange(FindAllFileLists(Path.Combine(BackupSiteLocation, "wwwroot", "Addons")));
                allLists.AddRange(FindAllFileLists(Path.Combine(BackupSiteLocation, "wwwroot", "AddonsCustom")));
            } else {
                allLists.AddRange(FindAllFileLists(Path.Combine(BackupSiteLocation, "Addons")));
                allLists.AddRange(FindAllFileLists(Path.Combine(BackupSiteLocation, "AddonsCustom")));
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
                        string realPath = BackupSiteLocation + IOHelper.FileToPhysical(path);
                        if (!Directory.Exists(realPath))
                            throw new Error($"File {file} contains reference to folder {realPath} that does not exist");
                        path = path.Substring(1);// remove leading '\'
                        paths.Add(IOHelper.FileToPhysical(path));
                        continue;
                    } else {
                        if (path.StartsWith("/node_modules/") || path.StartsWith("bower_components")) {
                            if (!path.EndsWith(".js") && !path.EndsWith(".css"))
                                throw new Error($"File {file} contains reference to {path} which isn't .js or .css");
                            path = Path.GetDirectoryName(path);
                            path = IOHelper.PhysicalToFile(path);
                            path = path.Replace("/{0}", ""); // for some composite paths like jqueryui themes
                            string realPath = BackupSiteLocation + IOHelper.FileToPhysical(path);
                            if (!Directory.Exists(realPath))
                                throw new Error($"File {file} contains reference to folder {realPath} that does not exist");
                            path = path.Substring(1);// remove leading '/'
                            paths.Add(IOHelper.FileToPhysical(path));
                            continue;
                        }
                        if (path.StartsWith("Folder ")) {
                            path = path.Substring(6).Trim();
                            path = path.Replace("/{0}", ""); // for some composite paths like jqueryui themes
                            string realPath = BackupSiteLocation + IOHelper.FileToPhysical(path);
                            if (!Directory.Exists(realPath))
                                throw new Error($"File {file} contains reference to folder {realPath} that does not exist");
                            path = path.Substring(1);// remove leading '\'
                            paths.Add(IOHelper.FileToPhysical(path));
                            continue;
                        }
                        if (path.Contains("node_modules") || path.Contains("bower_components"))
                            throw new Error($"File {file} contains reference to node_modules which should start with \"\\node_modules\" or \"\\bower_components\"");
                    }
                }
            }
            return paths;
        }
        private List<string> AllowSubstitutionFiles = new List<string> {
            "data/appsettings.json",
            "data/appsettings.prod.json",
            "web.config",
            "web.prod.config",
        };

        private void AddFileToTarget(string relFile, string newName = null, bool Optional = false) {
            if (newName == null) newName = relFile;
            string absFile = Path.Combine(BackupSiteLocation, relFile);
            if (!Optional || File.Exists(absFile)) {
                Console.WriteLine("Copying {0} from {1}", newName, absFile);

                if (Program.BlueGreenDeploy != Program.BlueGreenDeployEnum.None && AllowSubstitutionFiles.Contains(IOHelper.PhysicalToFile(newName).ToLower())) {
                    string contents = File.ReadAllText(absFile);
                    contents = Program.ReplaceBlueGreen(contents);
                    if (BackupTargetZip != null) {
                        BackupTargetZip.AddData(contents, newName);
                    }
                    if (BackupTargetFolder != null) {
                        File.WriteAllText(Path.Combine(BackupTargetFolder, newName), contents);
                    }
                } else {
                    if (BackupTargetZip != null) {
                        BackupTargetZip.AddFile(absFile, newName);
                    }
                    if (BackupTargetFolder != null) {
                        File.Copy(absFile, Path.Combine(BackupTargetFolder, newName));
                    }
                }
            }
        }
        private void AddConfigFileToTarget(string relFile, string newName, bool Optional = false) {
            string f, fileName;
            if (!string.IsNullOrWhiteSpace(Program.YamlData.Deploy.ConfigParm)) {
                fileName = string.Format(relFile, $"{Program.YamlData.Deploy.ConfigParm}.");
                f = Path.Combine(BackupSiteLocation, fileName);
                if (File.Exists(f)) {
                    AddFileToTarget(fileName, newName);
                    return;
                }
            }
            fileName = string.Format(relFile, "");
            f = Path.Combine(BackupSiteLocation, fileName);
            if (File.Exists(f)) {
                AddFileToTarget(fileName, newName);
                return;
            }
            if (!Optional)
                throw new Error($"Config file {relFile} not found");
        }
    }
}
