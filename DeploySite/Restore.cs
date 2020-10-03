/* Copyright © 2020 Softel vdm, Inc. - https://yetawf.com/Documentation/YetaWF/Licensing */

using ICSharpCode.SharpZipLib.Zip;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Softelvdm.Tools.DeploySite {

    public class Restore {

        public const string UNZIPFOLDER = "TEMP";
        public const string DBDATAFOLDER = "DBs";

        public const string OFFLINE = "Offline For Maintenance.html";

        private string RestoreTargetSite;
        private Variables Variables = new Variables();

        public Restore() { }

        public void PerformRestore() {

            if (Program.YamlData.Deploy.Type != "zip")
                throw new Error($"Invalid deploy type {Program.YamlData.Deploy.Type} - only zip deploys can be restored");
            if (Program.YamlData.Site == null)
                throw new Error($"No Site information provided in yaml file");

            if (Program.YamlData.Site.Include != null) {
                Variables = Variables.LoadVariables(Path.Combine(".", Program.YamlData.Site.Include));
                // perform variable substitution
                foreach (Database db in Program.YamlData.Databases) {
                    db.ProdServer = db.ProdServer.Replace("[Var,SQLServer]", Variables.SQLServer);
                    db.ProdUsername = db.ProdUsername.Replace("[Var,SQLUser]", Variables.SQLUser);
                    db.ProdPassword = db.ProdPassword.Replace("[Var,SQLPassword]", Variables.SQLPassword);
                }
                foreach (RunCommand run in Program.YamlData.Site.RunFirst) {
                    run.Command = run.Command.Replace("[Var,SQLServer]", Variables.SQLServer);
                    run.Command = run.Command.Replace("[Var,SQLUser]", Variables.SQLUser);
                    run.Command = run.Command.Replace("[Var,SQLPassword]", Variables.SQLPassword);
                    run.Command = run.Command.Replace("[Var,Preload]", Variables.Preload);
                }
                foreach (RunCommand run in Program.YamlData.Site.Run) {
                    run.Command = run.Command.Replace("[Var,SQLServer]", Variables.SQLServer);
                    run.Command = run.Command.Replace("[Var,SQLUser]", Variables.SQLUser);
                    run.Command = run.Command.Replace("[Var,SQLPassword]", Variables.SQLPassword);
                    run.Command = run.Command.Replace("[Var,Preload]", Variables.Preload);
                }
            }

            RestoreTargetSite = Program.YamlData.Site.Location;

            ExtractAllFiles();

            Console.WriteLine("ASP.NET Core Site");

            SetMaintenanceMode();

            RunFirstCommands();

            RestoreDBs();

            CopyAllFilesToSite();

            RunCommands();

            ClearMaintenanceMode();

            string folder = Path.Combine(RestoreTargetSite, UNZIPFOLDER);
            IOHelper.DeleteFolder(folder);
        }

        private void SetMaintenanceMode() {
            if (Program.YamlData.Site.Maintenance) {
                string maintSrcFile, maintTargetFile;
                maintSrcFile = Path.Combine(RestoreTargetSite, UNZIPFOLDER, "wwwroot", Program.MAINTENANCEFOLDER, OFFLINE);
                maintTargetFile = Path.Combine(RestoreTargetSite, "App_Offline.htm");
                File.Copy(maintSrcFile, maintTargetFile, true);
            }
        }

        private void ClearMaintenanceMode() {
            if (Program.YamlData.Site.Maintenance) {
                string maintTargetFile;
                maintTargetFile = Path.Combine(RestoreTargetSite, "App_Offline.htm");
                File.Delete(maintTargetFile);
            }
        }

        private void ExtractAllFiles() {
            //if (File.Exists(Path.Combine(BaseDirectory, "UNZIPDONE"))) {
            //    Console.WriteLine("Skipped extracting Zip file");
            //} else {

            string folder = Path.Combine(RestoreTargetSite, UNZIPFOLDER);

            Console.WriteLine("Extracting Zip file...");
            IOHelper.DeleteFolder(folder);
            Directory.CreateDirectory(folder);

            FastZip zipFile = new FastZip();
            zipFile.ExtractZip(Program.YamlData.Site.Zip, folder, null);

            //File.WriteAllText(Path.Combine(BaseDirectory, "UNZIPDONE"), "Done");
            //}
        }

        private void RestoreDBs() {
            if (Program.YamlData.Databases != null) {
                foreach (Database db in Program.YamlData.Databases) {
                    if (!string.IsNullOrWhiteSpace(db.Bacpac)) {
                        if (db.DevDB != null || db.DevServer != null || db.DevUsername != null || db.DevPassword != null)
                            throw new Error($"Can't mix bacpac and development DB information ({db.Bacpac})");
                        if (Program.YamlData.Site.Sqlcmd == null)
                            throw new Error($"Site.Sqlpackage in yaml file required for bacpac support");
                        if (Program.YamlData.Site.Sqlpackage == null)
                            throw new Error($"Site.Sqlpackage in yaml file required for bacpac support");
                        RestoreBacpac(db);
                    } else if (!string.IsNullOrWhiteSpace(db.ubackup)) {
                        if (db.DevDB != null || db.DevServer != null || db.DevUsername != null || db.DevPassword != null)
                            throw new Error($"Can't mix ubackup and development DB information ({db.ubackup})");
                        RestoreUbackup(db);
                    } else {
                        RestoreDB(db, $"{db.DevDB}.bak");
                    }
                }
            }
        }

        private void RestoreBacpac(Database db) {

            KillSQLConnections(db);

            Console.WriteLine("Restoring bacpac DB {0}", db.ProdDB);

            string bacpacFileName = Path.Combine(RestoreTargetSite, UNZIPFOLDER, Program.DBFOLDER, db.Bacpac);

            // Drop database
            string args = $@"-b -m-1 -V11 -S ""{db.ProdServer}"" -U ""{db.ProdUsername}"" -P ""{db.ProdPassword}"" -Q ""DROP DATABASE [{db.ProdDB}]"" ";
            RunCommand(Program.YamlData.Site.Sqlcmd, args, IgnoreErrors: true);

            // Create database
            args = $@"-b -m-1 -V11 -S ""{db.ProdServer}"" -U ""{db.ProdUsername}"" -P ""{db.ProdPassword}"" -Q ""CREATE DATABASE [{db.ProdDB}]""";
            RunCommand(Program.YamlData.Site.Sqlcmd, args);

            // Import database
            args = $@"/tsn:""{db.ProdServer}"" /tdn:""{db.ProdDB}"" /a:Import /tu:""{db.ProdUsername}"" /tp:""{db.ProdPassword}"" /sf:""{bacpacFileName}""";
            RunCommand(Program.YamlData.Site.Sqlpackage, args);
        }

        private void RestoreUbackup(Database db) {

            Console.WriteLine("Restoring ubackup DB {0}", db.ProdDB);

            if (!Variables.ubackupRestore) {
                Console.WriteLine($"Database {db.ProdDB} not restored - ubackup support not enabled");
                return;
            }
            // Find latest DB backup
            string dbZip = FindUbackupZip(db);
            // Unzip
            string dbBak = UnzipUbackup(dbZip);
            // Restore
            RestoreDB(db, dbBak);
        }

        private string FindUbackupZip(Database db) {
            List<string> files = Directory.GetFiles(Path.GetDirectoryName(db.ubackup), Path.GetFileName(db.ubackup)).ToList();
            string file = (from f in files orderby f select f).LastOrDefault();
            if (file == null)
                throw new Error($"No matching database found for {db.ubackup}");
            Console.WriteLine($"Found zip file {file} for {db.ProdDB}");
            return file;
        }

        private string UnzipUbackup(string dbZip) {

            string dbFolder = Path.Combine(RestoreTargetSite, UNZIPFOLDER, Program.DBFOLDER);

            Console.WriteLine("Extracting Zip file...");

            Directory.CreateDirectory(dbFolder);

            FastZip zipFile = new FastZip();
            zipFile.ExtractZip(dbZip, dbFolder, null);

            string bakFile = Path.Combine(dbFolder, Path.ChangeExtension(Path.GetFileName(dbZip), ".bak"));
            if (!File.Exists(bakFile))
                throw new Error($"Expected file {bakFile} not found");
            Console.WriteLine($"Existing bak file used: {bakFile}");

            bakFile = Path.GetFileName(bakFile);
            Console.WriteLine($"Found bak file {bakFile}");
            return bakFile;
        }

        private void KillSQLConnections(Database db) {
            // https://stackoverflow.com/questions/7197574/script-to-kill-all-connections-to-a-database-more-than-restricted-user-rollback

            Console.WriteLine($"Closing existing connections to {db.ProdDB}");

            string connectionString = String.Format("Data Source={0};User ID={1};Password={2};", db.ProdServer, db.ProdUsername, db.ProdPassword);

            using (SqlConnection sqlConnection = new SqlConnection(connectionString)) {

                sqlConnection.Open();

                string sqlKill = $@"
USE [master];

DECLARE @kill varchar(8000) = '';  
SELECT @kill = @kill + 'kill ' + CONVERT(varchar(5), session_id) + ';'
FROM sys.dm_exec_sessions
WHERE database_id  = db_id('{db.ProdDB}')

EXEC(@kill);";

                using (SqlCommand cmd = new SqlCommand(sqlKill, sqlConnection)) {
                    cmd.ExecuteNonQuery();
                }
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "None")]
        private void RestoreDB(Database db, string bakFile) {

            KillSQLConnections(db);

            Console.WriteLine($"Restoring DB {db.ProdDB}");

            string dbFileName = Path.Combine(RestoreTargetSite, UNZIPFOLDER, Program.DBFOLDER, bakFile);

            // Connection
            string connectionString = String.Format("Data Source={0};User ID={1};Password={2};", db.ProdServer, db.ProdUsername, db.ProdPassword);

            using (SqlConnection sqlConnection = new SqlConnection(connectionString)) {

                sqlConnection.Open();

                List<string> DataFiles = new List<string>();
                List<string> LogFiles = new List<string>();

                string SQLFileList = $"RESTORE FILELISTONLY FROM DISK = '{dbFileName}'";

                Console.WriteLine($"Restoring DB {db.ProdDB} - {SQLFileList}");

                using (SqlCommand cmd = new SqlCommand(SQLFileList, sqlConnection)) {
                    cmd.CommandTimeout = 30 * 60;// 1/2 hour for restores
                    using (SqlDataReader rdr = cmd.ExecuteReader()) {
                        while (rdr.Read()) {
                            string logicalName = rdr.GetString(0);
                            string type = rdr.GetString(2);
                            if (type == "D") {
                                DataFiles.Add(logicalName);
                            } else if (type == "L") {
                                LogFiles.Add(logicalName);
                            } else {
                                throw new Error($"Unexpected file type {type} received from query \"{SQLFileList}\"");
                            }
                        }
                        if (DataFiles.Count == 0 && LogFiles.Count == 0)
                            throw new Error($"Couldn't retrieve file lists with query \"{SQLFileList}\"");
                    }
                }

                string SQLBackupQuery = $"RESTORE DATABASE [{db.ProdDB}] FROM DISK = '{dbFileName}' WITH REPLACE ";

                string dataFolder = Path.Combine(RestoreTargetSite, DBDATAFOLDER);

                Directory.CreateDirectory(dataFolder);

                StringBuilder sb = new StringBuilder();
                sb.Append(SQLBackupQuery);
                foreach (string file in DataFiles) {
                    sb.Append($", MOVE '{file}' TO '{Path.Combine(dataFolder, db.ProdDB)}.mdf'");
                }
                foreach (string file in LogFiles) {
                    sb.Append($", MOVE '{file}' TO '{Path.Combine(dataFolder, db.ProdDB)}.ldf'");
                }

                using (SqlCommand cmd = new SqlCommand(sb.ToString(), sqlConnection)) {
                    Console.WriteLine($"Restoring DB - {sb.ToString()}");
                    cmd.CommandTimeout = 30 * 60;// 1/2 hour for restores
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private void CopyAllFilesToSite() {

            Console.WriteLine("Copying files...");

            // Add folders
            AddAllFilesToSite(Path.Combine("wwwroot", "Addons"));
            AddAllFilesToSite(Path.Combine("wwwroot", "AddonsCustom"));
            AddAllFilesToSite(Path.Combine("wwwroot", Program.MAINTENANCEFOLDER));
            AddAllFilesToSite(Path.Combine("wwwroot", "SiteFiles"));
            //AddAllFilesToSite(Path.Combine("wwwroot", "Vault"));
            AddFileToSite(Path.Combine("wwwroot", "logo.jpg"), Optional: true);
            AddFileToSite(Path.Combine("wwwroot", "robots.txt"));

            IOHelper.DeleteFolder(Path.Combine(RestoreTargetSite, "wwwroot", "Areas"));
            AddAllFilesToSite("bower_components");
            AddAllFilesToSite(Program.DATAFOLDER, ExcludeFiles: new List<string> { @".*\.mdf", @".*\.ldf" });
            AddAllFilesToSite("Localization");
            AddAllFilesToSite("LocalizationCustom");
            AddAllFilesToSite("node_modules");
            AddAllFilesToSite("Sites", ExcludeFiles: new List<string> { @"Backup .*\.zip" });
            AddAllFilesToSite("SiteTemplates");

            AddFilesToSiteDontRecurse(Path.Combine(RestoreTargetSite, UNZIPFOLDER), RestoreTargetSite, @"*.*");// dlls,exe, whatever is in the root folder

            Directory.CreateDirectory(Path.Combine(RestoreTargetSite, "logs")); // make a log folder

            string deployMarker = Path.Combine(RestoreTargetSite, "node_modules");//$$$DUBIOUS
            Directory.SetLastWriteTimeUtc(deployMarker, DateTime.UtcNow);

            Console.WriteLine("All files copied");
        }
        private void AddFilesToSite(string match, List<string> ExcludeFolders = null) {
            string unzipPath = Path.Combine(RestoreTargetSite, UNZIPFOLDER);
            AddFilesToSiteAndRecurse(unzipPath, RestoreTargetSite, match, ExcludeFolders: ExcludeFolders);
        }
        private void AddAllFilesToSite(string folder, List<string> ExcludeFiles = null) {
            string unzipPath = Path.Combine(RestoreTargetSite, UNZIPFOLDER, folder);
            string targetPath = Path.Combine(RestoreTargetSite, folder);
            if (ExcludeFiles != null) {
                RemoveFolderContents(targetPath, ExcludeFiles);
            } else {
                IOHelper.DeleteFolder(targetPath);
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
        private bool LikeString(string fileName, string pattern) {
            Regex re = new Regex($"^{pattern}$", RegexOptions.IgnoreCase);
            return re.IsMatch(fileName);
        }

        private void AddFilesToSiteDontRecurse(string unzipPath, string targetPath, string match = "*.*") {
            if (!Directory.Exists(unzipPath)) return;
            // delete all files without recursing
            string[] files = Directory.GetFiles(targetPath, match);
            foreach (string file in files) {
                if (Path.GetFileName(file).ToLower() != "app_offline.htm")
                    File.Delete(file);
            }
            // add new files
            files = Directory.GetFiles(unzipPath, match);
            foreach (string file in files) {
                string filename = Path.GetFileName(file);
                string unzipFile = Path.Combine(unzipPath, filename);
                AddFileToSite(unzipFile, Path.Combine(targetPath, filename));
            }
        }
        private void AddFilesToSiteAndRecurse(string unzipPath, string targetPath, string match = "*.*", List<string> ExcludeFolders = null) {
            if (!Directory.Exists(unzipPath)) return;
            Directory.CreateDirectory(targetPath);
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
        private void AddFileToSite(string unzipFile, string targetFile, bool Optional = false) {
            if (!Optional || File.Exists(unzipFile)) {
                Console.WriteLine("Copying {0}", targetFile);
                string path = Path.GetDirectoryName(targetFile);
                Directory.CreateDirectory(path);
                File.Copy(unzipFile, targetFile, true);
            }
        }
        private void AddFileToSite(string file, bool Optional = false) {
            string unzipFile = Path.Combine(RestoreTargetSite, UNZIPFOLDER, file);
            if (!Optional || File.Exists(unzipFile)) {
                string targetFile = Path.Combine(RestoreTargetSite, file);
                Console.WriteLine("Copying {0}", targetFile);
                File.Delete(targetFile);
                File.Copy(unzipFile, targetFile, true);
            }
        }

        private void RunFirstCommands() {
            if (Program.YamlData.Site.RunFirst != null) {
                foreach (RunCommand command in Program.YamlData.Site.RunFirst) {
                    RunCommand(command);
                }
            }
        }

        private void RunCommands() {
            if (Program.YamlData.Site.Run != null) {
                foreach (RunCommand command in Program.YamlData.Site.Run) {
                    RunCommand(command);
                }
            }
        }

        private void RunCommand(RunCommand command) {
            RunCommand("cmd.exe", $"/C {command.Command}", command.IgnoreErrors);
        }

        private void RunCommand(string command, string args, bool IgnoreErrors = false) {
            Process p = new Process();

            Console.WriteLine($"Running {command} {args}");

            p.StartInfo.FileName = command;
            p.StartInfo.Arguments = args;

            p.StartInfo.CreateNoWindow = true;
            p.EnableRaisingEvents = true;

            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.UseShellExecute = false;
            p.OutputDataReceived += new DataReceivedEventHandler(process_OutputDataReceived);
            p.ErrorDataReceived += new DataReceivedEventHandler(process_ErrorDataReceived);
            p.Exited += new EventHandler(process_Exited);

            if (p.Start()) {
                p.BeginOutputReadLine();
                p.WaitForExit();
            } else {
                throw new Error($"Failed to start {command}");
            }

            if (p.ExitCode != 0) {
                if (!IgnoreErrors)
                    throw new Error($"{command} failed (ExitCode = {p.ExitCode})");
            }
            Console.WriteLine($"Completion exit code {p.ExitCode}");
        }

        private void process_ErrorDataReceived(object sender, DataReceivedEventArgs e) {
            Console.WriteLine(e.Data);
        }

        private void process_Exited(object sender, EventArgs e) {
            Console.WriteLine("\r\nDone\r\n");
        }
        void process_OutputDataReceived(object sender, DataReceivedEventArgs e) {
            Console.WriteLine(e.Data);
        }
    }
}