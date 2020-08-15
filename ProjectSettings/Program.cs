/* Copyright © 2020 Softel vdm, Inc. - https://yetawf.com/Documentation/YetaWF/Licensing */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Softelvdm.Tools.ProjectSettings {

    /// <summary>
    /// This is a hacky little program that is typically used during installation of YetaWF to
    /// - create symlinks which are different between Windows and Linux et.al.
    /// - handle selection of SQL or SQLDyn package. The selection in .csproj doesn't work with docker/dotnet build for nested referenced packages.
    /// </summary>
    /// <remarks>The code could be prettier. This is a dev tool that ended up being needed for installation on linux. Oh well.</remarks>
    class Program {

        public bool Junctions { get; private set; }
        public bool SQL { get; private set; }
        public bool SQLDyn { get; private set; }

        static int Main(string[] args) {

            Program pgm = new Program();

            // Process command line
            int argCount = args.Length;
            if (argCount > 0) {
                for (int i = 0; i < argCount; ++i) {
                    string s = args[i];
                    if (string.Compare(s, "Symlinks", true) == 0) {
                        pgm.Junctions = true;
                    } else if (string.Compare(s, "SQL", true) == 0) {
                        pgm.SQL = true;
                    } else if (string.Compare(s, "SQLDyn", true) == 0) {
                        pgm.SQLDyn = true;
                    } else {
                        Messages.Message(string.Format("Invalid argument {0}", s));
                        return -1;
                    }
                }
            }

            // Validate conflicting parms
            if ((!pgm.Junctions && !pgm.SQL && !pgm.SQLDyn) || (pgm.SQL && pgm.SQLDyn)) {
                Messages.Message("Usage: YetaWF.ProjectSettings.exe {Symlinks|SQL|SQLDyn} ");
                return -1;
            }

            // Find the current directory and search for *.sln moving up the hierarchy
            string dir = Directory.GetCurrentDirectory();
            string solFolder = pgm.FindSolutionFolder(dir);
            if (string.IsNullOrWhiteSpace(solFolder)) {
                Messages.Message("No solution found (starting at {0}", dir);
                return -1;
            }

            if (pgm.Junctions) {
                pgm.DeleteAllDirectories(Path.Combine(solFolder, "Website", "Areas"));
                pgm.DeleteAllDirectories(Path.Combine(solFolder, "Website", "wwwroot", "Addons"));
                pgm.ProjectsFolderWebsiteLinks(Path.Combine(solFolder, "Modules"), solFolder);
                pgm.ProjectsFolderWebsiteLinks(Path.Combine(solFolder, "Skins"), solFolder);
                pgm.OneProjectFolderWebsiteLinks(Path.Combine(solFolder, "CoreComponents"), solFolder, "YetaWF", "Core", TargetCompany: false);

                Directory.CreateDirectory(Path.Combine(solFolder, "Website", "wwwroot", "Addons", "YetaWF.DataProvider"));
                MakeSymLink(Path.Combine(solFolder, "Website", "wwwroot", "Addons", "YetaWF.DataProvider", "File"), Path.Combine(solFolder, "DataProvider", "File", "Addons"));
                MakeSymLink(Path.Combine(solFolder, "Website", "wwwroot", "Addons", "YetaWF.DataProvider", "Localization"), Path.Combine(solFolder, "DataProvider", "Localization", "Addons"));
                MakeSymLink(Path.Combine(solFolder, "Website", "wwwroot", "Addons", "YetaWF.DataProvider", "ModuleDefinition"), Path.Combine(solFolder, "DataProvider", "ModuleDefinition", "Addons"));
                MakeSymLink(Path.Combine(solFolder, "Website", "wwwroot", "Addons", "YetaWF.DataProvider", "PostgreSQL"), Path.Combine(solFolder, "DataProvider", "PostgreSQL", "Addons"));
                MakeSymLink(Path.Combine(solFolder, "Website", "wwwroot", "Addons", "YetaWF.DataProvider", "SQL"), Path.Combine(solFolder, "DataProvider", "SQL", "Addons"));
                MakeSymLink(Path.Combine(solFolder, "Website", "wwwroot", "Addons", "YetaWF.DataProvider", "SQLDyn"), Path.Combine(solFolder, "DataProvider", "SQLDyn", "Addons"));
                MakeSymLink(Path.Combine(solFolder, "Website", "wwwroot", "Addons", "YetaWF.DataProvider", "SQLGeneric"), Path.Combine(solFolder, "DataProvider", "SQLGeneric", "Addons"));

                MakeSymLink(Path.Combine(solFolder, "Website", "Localization"), Path.Combine(solFolder, "Localization"));
                MakeSymLink(Path.Combine(solFolder, "CoreComponents", "Core", "node_modules"), Path.Combine(solFolder, "Website", "node_modules"));
            }
            if (pgm.SQL || pgm.SQLDyn) {
                Messages.Message("Website projects...");
                pgm.VisitFoldersForProjects(Path.Combine(solFolder, "Website"), Recurse: false);
                Messages.Message("CoreComponents projects...");
                pgm.VisitFoldersForProjects(Path.Combine(solFolder, "CoreComponents"));
                Messages.Message("DataProvider projects...");
                pgm.VisitFoldersForProjects(Path.Combine(solFolder, "DataProvider"));
                Messages.Message("Modules projects...");
                pgm.VisitFoldersForProjects(Path.Combine(solFolder, "Modules"));
                Messages.Message("Skins projects...");
                pgm.VisitFoldersForProjects(Path.Combine(solFolder, "Skins"));
                Messages.Message("Templates projects...");
                pgm.VisitFoldersForProjects(Path.Combine(solFolder, "Templates"));
                Messages.Message("Tools projects...");
                pgm.VisitFoldersForProjects(Path.Combine(solFolder, "Tools"));
                Messages.Message("Public Tools projects...");
                pgm.VisitFoldersForProjects(Path.Combine(solFolder, "PublicTools"));
            }
            return 0;
        }

        private string FindSolutionFolder(string dir) {
            for (; ; ) {
                List<string> files = Directory.GetFiles(dir, "*.sln", SearchOption.TopDirectoryOnly).ToList();
                if (files.Count > 0) return dir;
                dir = Path.GetDirectoryName(dir);
                if (string.IsNullOrWhiteSpace(dir)) return null;
            }
            /* not reached */
        }

        private void DeleteAllDirectories(string target) {
            if (Directory.Exists(target)) {
                string[] folders = Directory.GetDirectories(target);
                foreach (string folder in folders) {
                    DeleteFolder(folder);
                }
            }
        }
        private void DeleteFolder(string targetFolder) {
            if (!Directory.Exists(targetFolder)) return;// avoid exception spam

            int retry = 10; // folder occasionally are in use to we'll just wait a bit
            while (retry > 0) {
                try {
                    Directory.Delete(targetFolder, true);
                    return;
                } catch (Exception exc) {
                    if (exc is DirectoryNotFoundException)
                        return;// done
                    if (retry <= 1)
                        throw;
                }
                System.Threading.Thread.Sleep(100); // wait a bit
                --retry;
            }
        }
        private void ProjectsFolderWebsiteLinks(string folder, string solFolder) {
            List<string> companies = Directory.GetDirectories(folder).ToList();
            foreach (string company in companies) {
                List<string> projects = Directory.GetDirectories(company).ToList();
                foreach (string project in projects) {
                    // Make a symlink from the project to Website/node_modules
                    //// WORKAROUND for VS2017 preview bug (slow open of solution) - only make node_modules symlink for projects that actually need it
                    //List<string> projectsWithNodeNames = new List<string> { "Basics" };
                    //string projName = Path.GetFileName(project);
                    //if (projectsWithNodeNames.Contains(projName)) {
                    string srcFolder = Path.Combine(project, "node_modules");
                    string targetFolder = Path.Combine(solFolder, "Website", "node_modules");
                    MakeSymLink(srcFolder, targetFolder);
                    Messages.Message($"Symlink from {srcFolder} to {targetFolder}");
                    //}
                    OneProjectFolderWebsiteLinks(folder, solFolder, Path.GetFileName(company), project);
                }
            }
        }

        private void OneProjectFolderWebsiteLinks(string folder, string solFolder, string companyName, string project, bool TargetCompany = true) {

            string targetCompanyName = TargetCompany ? companyName : "";
            string projName = Path.GetFileName(project);

            string websiteFolder = Path.Combine(solFolder, "Website");
            // Make a symlink from the Website/Addons/company/project to folder/company/project/Addons
            string srcFolder = Path.Combine(websiteFolder, "wwwroot", "Addons");
            Directory.CreateDirectory(srcFolder);
            srcFolder = Path.Combine(websiteFolder, "wwwroot", "Addons", companyName);
            Directory.CreateDirectory(srcFolder);
            srcFolder = Path.Combine(websiteFolder, "wwwroot", "Addons", companyName, projName);
            string targetFolder = Path.Combine(folder, targetCompanyName, projName, "Addons");
            MakeSymLink(srcFolder, targetFolder);
        }

        private static void MakeSymLink(string srcFolder, string targetFolder) {
            if (Directory.Exists(srcFolder)) {
                Console.WriteLine($"Removing folder {srcFolder}");
                Directory.Delete(srcFolder);
            }
            if (!Directory.Exists(targetFolder))
                return;
            //Console.WriteLine(string.Format("Linking {0} to {1}", srcFolder, targetFolder));
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                Junction.Create(srcFolder, targetFolder, true);
            } else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                // There is no API in .NET Core to do this. Tsk, tsk.
                RunCommand("ln", $"-f -s \"{targetFolder}\" \"{srcFolder}\"");
                if (!Directory.Exists(srcFolder))
                    throw new ApplicationException($"Unable to create symlink from {srcFolder} to {targetFolder}");
            } else {
                throw new ApplicationException($"Unsupported operating system (currently only Windows and Linux are supported)");
            }
        }
        private static void RunCommand(string cmd, string args) {

            Process p = new Process();

            Console.WriteLine($"Executing {cmd} {args}");

            p.StartInfo.FileName = cmd;
            p.StartInfo.Arguments = args;

            p.StartInfo.CreateNoWindow = true;
            p.EnableRaisingEvents = true;

            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.UseShellExecute = false;

            if (p.Start()) {
                string result = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
            } else {
                throw new ApplicationException($"Failed to start {cmd}");
            }

            if (p.ExitCode != 0)
                throw new ApplicationException($"{cmd} {args} failed - {p.ExitCode}");
        }

        private List<string> ProjectPatterns = new List<string> { "*.csproj" };

        private List<string> GetProjectFiles(string folder) {
            List<string> files = new List<string>();
            foreach (string pattern in ProjectPatterns) {
                List<string> match = Directory.GetFiles(folder, pattern).ToList();
                files.AddRange(match);
            }
            return files;
        }

        private void VisitFoldersForProjects(string folder, bool Recurse = true) {
            if (!Directory.Exists(folder)) return;
            string folderEnd = Path.GetFileName(folder).ToLower();
            if (folderEnd == "node_modules")
                return;
            if (folderEnd == "bin")
                return;
            if (folderEnd == "addons")
                return;

            List<string> files = GetProjectFiles(folder);
            foreach (string f in files)
                FixProject(f);
            if (files.Count > 0)
                return;// no need to search subfolders

            if (Recurse) {
                List<string> dirs = Directory.GetDirectories(folder).ToList();
                foreach (string d in dirs)
                    VisitFoldersForProjects(d);
            }
        }

        private void FixProject(string project) {
            string projText = File.ReadAllText(project);
            string newText;
            if (SQLDyn)
                newText = reCsProj.Replace(projText, @"<ItemGroup><ProjectReference Include=""$1""/></ItemGroup>");
            else
                newText = reCsProj.Replace(projText, @"<ItemGroup><ProjectReference Include=""$2""/></ItemGroup>");
            if (newText != projText)
                File.WriteAllText(project, newText);
        }

        private const string reg =
@"<Choose>\s*" +
  @"<When Condition=""Exists\('[^""]*USE_SQLDYN.txt'\)"">\s*" +
    @"<ItemGroup>\s*<ProjectReference Include=""([^""].*?)""\s*\/>\s*</ItemGroup>\s*" +
  @"</When>\s*" +
  @"<Otherwise>\s*" +
    @"<ItemGroup>\s*<ProjectReference Include=""([^""].*?)""\s*\/>\s*</ItemGroup>\s*" +
  @"</Otherwise>\s*" +
@"</Choose>";

        private Regex reCsProj = new Regex(reg, RegexOptions.Singleline | RegexOptions.Compiled);

    }
}
