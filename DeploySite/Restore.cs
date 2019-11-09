﻿/* Copyright © 2019 Softel vdm, Inc. - https://yetawf.com/Documentation/YetaWF/Licensing */

using ICSharpCode.SharpZipLib.Zip;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Softelvdm.Tools.DeploySite {

    public class Restore {

        public const string UNZIPFOLDER = "TEMP";
        public const string DBDATAFOLDER = "DBs";

        private string RestoreTargetSite;

        private bool IsMVC6 { get; set; }

        public Restore() { }

        public void PerformRestore() {

            if (Program.YamlData.Deploy.Type != "zip")
                throw new Error($"Invalid deploy type {Program.YamlData.Deploy.Type} - only zip deploys can be restored");

            RestoreTargetSite = Program.YamlData.Site.Location;

            RunFirstCommands();
            ExtractAllFiles();

            if (Directory.Exists(Path.Combine(RestoreTargetSite, UNZIPFOLDER, Program.MARKERMVC6)))
                IsMVC6 = true;
            if (IsMVC6)
                Console.WriteLine("ASP.NET Core Site");
            else
                Console.WriteLine("ASP.NET 4 Site");

            //SetMaintenanceMode();
            //SetUpdateIndicator();

            RestoreDBs();

            CopyAllFilesToSite();

            RunCommands();

            //ClearMaintenanceMode();

            string folder = Path.Combine(RestoreTargetSite, UNZIPFOLDER);
            IOHelper.DeleteFolder(folder);
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
                    RestoreDB(db);
                }
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "None")]
        private void RestoreDB(Database db) {

            Console.WriteLine("Restoring DB {0}", db.ProdDB);

            string dbFileName = Path.Combine(RestoreTargetSite, UNZIPFOLDER, Program.DBFOLDER, $"{db.DevDB}.bak");

            // Connection
            string connectionString = String.Format("Data Source={0};User ID={1};Password={2};", db.ProdServer, db.ProdUsername, db.ProdPassword);

            using (SqlConnection sqlConnection = new SqlConnection(connectionString)) {

                sqlConnection.Open();

                List<string> DataFiles = new List<string>();
                List<string> LogFiles = new List<string>();

                string SQLFileList = $"RESTORE FILELISTONLY FROM DISK = '{dbFileName}'";
                using (SqlCommand cmd = new SqlCommand(SQLFileList, sqlConnection)) {
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

                string dataFolder;
                if (IsMVC6)
                    dataFolder = Path.Combine(RestoreTargetSite, DBDATAFOLDER);
                else
                    dataFolder = Path.Combine(RestoreTargetSite, "..", DBDATAFOLDER);

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
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private void CopyAllFilesToSite() {

            Console.WriteLine("Copying files...");

            // Add folders
            if (IsMVC6) {
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

            } else {
                AddAllFilesToSite("Addons");
                AddAllFilesToSite("AddonsCustom");
                IOHelper.DeleteFolder(Path.Combine(RestoreTargetSite, "Areas"));
                AddAllFilesToSite("bower_components");
                AddAllFilesToSite("bin");
                AddAllFilesToSite(Program.DATAFOLDER, ExcludeFiles: new List<string> { @".*\.mdf", @".*\.ldf" });
                AddAllFilesToSite(Program.MAINTENANCEFOLDER);
                AddAllFilesToSite("Localization");
                AddAllFilesToSite("LocalizationCustom");
                AddAllFilesToSite("node_modules");
                AddAllFilesToSite("Sites", ExcludeFiles: new List<string> { @"Backup .*\.zip" });
                AddAllFilesToSite("SiteFiles");
                AddAllFilesToSite("SiteTemplates");
                //AddAllFilesToSite("Vault");
                AddFileToSite("logo.jpg", Optional: true);
                AddFileToSite("Global.asax");
                AddFileToSite("robots.txt");
                AddFileToSite("Web.config");
                AddAllFilesToSite("Content");// used to remove target folder - we don't distribute files in this folder
                AddAllFilesToSite("Scripts");// used to remove target folder - we don't distribute files in this folder
            }

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

        private void AddFilesToSiteDontRecurse(string unzipPath, string targetPath, string match = "*.*", List<string> ExcludeFolders = null) {
            if (!Directory.Exists(unzipPath)) return;
            string[] files = Directory.GetFiles(unzipPath, match);
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
            Process p = new Process();

            Console.WriteLine($"Running {command.Command}");

            p.StartInfo.FileName = "cmd.exe";
            p.StartInfo.Arguments = "/C " + command.Command;

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
                throw new Error(string.Format($"Failed to start {command.Command}"));
            }

            if (p.ExitCode != 0) {
                if (!command.IgnoreErrors)
                    throw new Error(string.Format($"{command.Command} failed"));
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