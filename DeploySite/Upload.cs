using FluentFTP;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace Softelvdm.Tools.DeploySite {

    public partial class Backup {

        private void UploadAll() {
            if (Program.YamlData.FTP != null) {
                Console.WriteLine($"Uploading to {Program.YamlData.FTP.Server} ...");

                NetworkCredential creds = new NetworkCredential(Program.YamlData.FTP.User, Program.YamlData.FTP.Password);
                using (FtpClient ftpClient = new FtpClient(Program.YamlData.FTP.Server, Program.YamlData.FTP.Port, creds)) {
                    FtpProfile ftpProf = ftpClient.AutoConnect();
                    if (ftpProf == null)
                        throw new Error($"Can't connect to FTP server {Program.YamlData.FTP.Server}");

                    foreach (FTPCopy copy in Program.YamlData.FTP.Copy) {
                        string from = Path.Combine(Program.YamlData.Deploy.BaseFolder, copy.From);
                        Upload(ftpClient, from, copy.To, ReplaceBG: true);
                    }
                }
            }
        }

        private void Upload(FtpClient ftpClient, string from, string to, bool ReplaceBG = false) {
            if (Directory.Exists(from)) {
                Console.WriteLine($"Uploading folder {from}");
                string[] files = Directory.GetFiles(from);
                string folder = Path.GetFileName(from);
                foreach (string file in files) {
                    Upload(ftpClient, file, $"{to}/{folder}/{Path.GetFileName(file)}");
                }
            } else if (File.Exists(from)) {
                Console.WriteLine($"Uploading file {from}");
                if (ReplaceBG) {
                    string content = File.ReadAllText(from);
                    content = Program.ReplaceBlueGreen(content);
                    byte[] btes = Encoding.ASCII.GetBytes(content);
                    ftpClient.Upload(btes, to, createRemoteDir: true);
                } else
                    ftpClient.UploadFile(from, to, createRemoteDir: true);
            } else
                throw new Error($"Can't upload {from} - not found");
        }
    }
}
