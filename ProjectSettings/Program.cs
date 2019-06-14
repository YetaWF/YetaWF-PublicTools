/* Copyright © 2019 Softel vdm, Inc. - https://yetawf.com/Documentation/YetaWF/Licensing */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Softelvdm.Tools.ProjectSettings {

    /// <summary>
    /// This is a hacky little program that is typically used during installation of YetaWF to
    /// - create symlinks which are different between Windows and Linux et.al.
    /// - copy project files which are different between ASP.NET and ASP.NET Core.
    /// </summary>
    /// <remarks>The code could be prettier. This is a dev tool that ended up being needed for installation on linux. Oh well.</remarks>
    class Program {
        public bool SetMVC5 { get; private set; }
        public bool SetMVC6 { get; private set; }
        public bool SaveCurrentAsMVC5 { get; private set; }
        public bool SaveCurrentAsMVC6 { get; private set; }
        public bool Junctions { get; private set; }
        public bool IsMVC6 { get; private set; }

        static int Main(string[] args) {

            Program pgm = new Program();

            int options = 0;

            // Process command line
            int argCount = args.Length;
            if (argCount > 0) {
                for (int i = 0 ; i < argCount ; ++i) {
                    string s = args[i];
                    if (string.Compare(s, "SaveCurrentAsMVC5", true) == 0) {
                        pgm.SaveCurrentAsMVC5 = true;
                        options++;
                    } else  if (string.Compare(s, "SaveCurrentAsMVC6", true) == 0) {
                        pgm.SaveCurrentAsMVC6 = true;
                        options++;
                    } else if (string.Compare(s, "SetMVC6", true) == 0) {
                        pgm.SetMVC6 = true;
                        options++;
                    } else if (string.Compare(s, "SetMVC5", true) == 0) {
                        pgm.SetMVC5 = true;
                        options++;
                    } else if (string.Compare(s, "Symlinks", true) == 0) {
                        pgm.Junctions = true;
                    } else {
                        Messages.Message(string.Format("Invalid argument {0}", s));
                        return -1;
                    }
                }
            }

            // Validate conflicting parms
            if (options > 1 || (options == 0 && !pgm.Junctions)) {
                Messages.Message("Usage: YetaWF.ProjectSettings.exe {SetMVC5|SetMVC6|SaveCurrentAsMVC5|SaveCurrentAsMVC6|Symlinks} ");
                return -1;
            }

            // Find the current directory and search for *.sln moving up the hierarchy
            string dir = Directory.GetCurrentDirectory();
            string solFolder = pgm.FindSolutionFolder(dir);
            if (string.IsNullOrWhiteSpace(solFolder)) {
                Messages.Message("No solution found (starting at {0}", dir);
                return -1;
            }

            pgm.IsMVC6 = Directory.Exists(Path.Combine(solFolder, "Website", "wwwroot"));

            if (pgm.Junctions) {
                pgm.DeleteAllDirectories(Path.Combine(solFolder, "Website", "Areas"));
                pgm.DeleteAllDirectories(Path.Combine(solFolder, "Website", pgm.IsMVC6 ? "wwwroot" : "", "Addons"));
                pgm.ProjectsFolderWebsiteLinks(Path.Combine(solFolder, "Modules"), solFolder);
                pgm.ProjectsFolderWebsiteLinks(Path.Combine(solFolder, "Skins"), solFolder);
                pgm.OneProjectFolderWebsiteLinks(Path.Combine(solFolder, "CoreComponents"), solFolder, "YetaWF", "Core", TargetCompany: false);
                MakeSymLink(Path.Combine(solFolder, "Website", "Localization"), Path.Combine(solFolder, "Localization"));
                MakeSymLink(Path.Combine(solFolder, "CoreComponents", "Core", "node_modules"), Path.Combine(solFolder, "Website", "node_modules"));
            }
            if (pgm.SetMVC5 || pgm.SetMVC6 || pgm.SaveCurrentAsMVC5 || pgm.SaveCurrentAsMVC6) {// really always
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

            //$$$ TEMPORARY
            if (companyName == "YetaWF" && projName == "Messenger")
                return; // Messenger module is not ready for distribution
            //$$$ TEMPORARY

            string websiteFolder = Path.Combine(solFolder, "Website");
            // Make a symlink from the Website/Addons/company/project to folder/company/project/Addons
            string srcFolder = Path.Combine(websiteFolder, IsMVC6 ? "wwwroot" : "", "Addons");
            Directory.CreateDirectory(srcFolder);
            srcFolder = Path.Combine(websiteFolder, IsMVC6 ? "wwwroot" : "", "Addons", companyName);
            Directory.CreateDirectory(srcFolder);
            srcFolder = Path.Combine(websiteFolder, IsMVC6 ? "wwwroot" : "", "Addons", companyName, projName);
            string targetFolder = Path.Combine(folder, targetCompanyName, projName, "Addons");
            MakeSymLink(srcFolder, targetFolder);
        }

        private static void MakeSymLink(string srcFolder, string targetFolder) {
            if (Directory.Exists(srcFolder))
                Directory.Delete(srcFolder);
            if (!Directory.Exists(targetFolder))
                return;
            //Console.WriteLine(string.Format("Linking {0} to {1}", srcFolder, targetFolder));
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                Junction.Create(srcFolder, targetFolder, true);
            } else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                // There is no API in .NET Core to do this. Tsk, tsk.
                RunCommand("ln" , $"-s \"{targetFolder}\" \"{srcFolder}\"");
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

        private List<string> ProjectPatterns = new List<string> { "*.csproj", "packages.config", "app.config" };

        private List<string> GetProjectFiles(string folder, string append = null) {
            List<string> files = new List<string>();
            foreach (string pattern in ProjectPatterns) {
                List<string> match = Directory.GetFiles(folder, pattern + append ?? "").ToList();
                files.AddRange(match);
            }
            return files;
        }
        private void DeleteProjectFiles(string folder) {
            foreach (string pattern in ProjectPatterns) {
                List<string> files = Directory.GetFiles(folder, pattern).ToList();
                foreach (string file in files)
                    File.Delete(file);
            }
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
            if (SetMVC5) {
                List<string> files = GetProjectFiles(folder, "_MVC5");
                if (files.Count > 0)
                    DeleteProjectFiles(folder);
                foreach (string f in files)
                    CopyFile(f, f.Substring(0, f.Length-"_MVC5".Length));
                if (files.Count > 0)
                    return;// no need to search subfolders
            } else if (SetMVC6) {
                List<string> files = GetProjectFiles(folder, "_MVC6");
                if (files.Count > 0)
                    DeleteProjectFiles(folder);
                foreach (string f in files)
                    CopyFile(f, f.Substring(0, f.Length-"_MVC6".Length));
                if (files.Count > 0)
                    return;// no need to search subfolders
            } else if (SaveCurrentAsMVC5) {
                List<string> files = GetProjectFiles(folder);
                foreach (string f in files)
                    CopyFile(f, f + "_MVC5");
                if (files.Count > 0)
                    return;// no need to search subfolders
            } else if (SaveCurrentAsMVC6) {
                List<string> files = GetProjectFiles(folder);
                foreach (string f in files)
                    CopyFile(f, f + "_MVC6");
                if (files.Count > 0)
                    return;// no need to search subfolders
            }
            if (Recurse) {
                List<string> dirs = Directory.GetDirectories(folder).ToList();
                foreach (string d in dirs)
                    VisitFoldersForProjects(d);
            }
        }

        private void CopyFile(string oldName, string newName) {
            if (File.Exists(oldName))
                File.Copy(oldName, newName, true);
        }
        private void MoveFile(string oldName, string newName) {
            if (File.Exists(oldName)) {
                if (File.Exists(newName))
                    File.Delete(newName);
                File.Move(oldName, newName);
            }
        }
        private string FindSolutionFolder(string dir) {
            for ( ; ; ) {
                List<string> files = Directory.GetFiles(dir, "*.sln", SearchOption.TopDirectoryOnly).ToList();
                if (files.Count > 0) return dir;
                dir = Path.GetDirectoryName(dir);
                if (string.IsNullOrWhiteSpace(dir)) return null;
            }
            /* not reached */
        }
    }
}
