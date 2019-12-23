/* Copyright © 2020 Softel vdm, Inc. - https://yetawf.com/Documentation/YetaWF/Licensing */

using FluentFTP;
using System;
using System.IO;
using System.Net;
using System.Text;

namespace Softelvdm.Tools.DeploySite {

    public partial class Backup {

        private void LocalCopy() {
            if (Program.YamlData.Local != null) {
                Console.WriteLine($"Local copies to {Program.YamlData.Local.PublishFolder} ...");

                IOHelper.DeleteFolder(Program.YamlData.Local.PublishFolder);
                Directory.CreateDirectory(Program.YamlData.Local.PublishFolder);

                foreach (LocalCopy copy in Program.YamlData.Local.Copy) {
                    string from = Path.Combine(Program.YamlData.Deploy.BaseFolder, copy.From);
                    string to = Path.Combine(Program.YamlData.Local.PublishFolder, copy.To);
                    CopyFile(from, to, ReplaceBG: copy.ReplaceBG);
                }
            }
        }

        private void CopyFile(string from, string to, bool ReplaceBG = false) {
            if (Directory.Exists(from)) {
                Console.WriteLine($"Copying folder {from}");
                string[] files = Directory.GetFiles(from);
                string folder = Path.GetFileName(from);
                foreach (string file in files) {
                    string toFile = Path.Combine(to, folder, Path.GetFileName(file));
                    File.Copy(file, toFile, true);
                }
            } else if (File.Exists(from)) {
                Console.WriteLine($"Copying file {from}");
                string folder = Path.GetDirectoryName(to);
                Directory.CreateDirectory(folder);
                if (ReplaceBG) {
                    string content = File.ReadAllText(from);
                    content = Program.ReplaceBlueGreen(content);
                    File.WriteAllText(to, content);
                } else {
                    File.Copy(from, to, true);
                }
            } else
                throw new Error($"Can't copy file {from} - not found");
        }
    }
}
