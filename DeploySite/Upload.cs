/* Copyright © 2022 Softel vdm, Inc. - https://yetawf.com/Documentation/YetaWF/Licensing */

using FluentFTP;
using System;
using System.IO;
using System.Net;
using System.Text;

namespace Softelvdm.Tools.DeploySite {

    public partial class Backup {

        private void UploadAll() {
            if (Program.YamlData.FTPs != null) {
                foreach (FTP ftp in Program.YamlData.FTPs) {

                    Console.WriteLine($"Uploading to {ftp.Server} ...");

                    NetworkCredential creds = new NetworkCredential(ftp.User, ftp.Password);
                    using (FtpClient ftpClient = new FtpClient(ftp.Server, ftp.Port, creds)) {
                        FtpProfile ftpProf = ftpClient.AutoConnect();
                        if (ftpProf == null)
                            throw new Error($"Can't connect to FTP server {ftp.Server}");

                        foreach (FTPCopy copy in Program.YamlData.FTPCopy) {
                            string from = Path.Combine(Program.YamlData.Deploy.BaseFolder, copy.From);
                            Upload(ftpClient, from, copy.To, ReplaceBG: copy.ReplaceBG, Conditional: copy.Conditional);
                        }
                    }
                }
            }
        }

        private void Upload(FtpClient ftpClient, string from, string to, bool ReplaceBG = false, bool Conditional = false) {
            if (Directory.Exists(from)) {
                Console.WriteLine($"Uploading folder {from}");
                string[] files = Directory.GetFiles(from);
                foreach (string file in files) {
                    Upload(ftpClient, file, Path.Combine(to, Path.GetFileName(file)));
                }
            } else if (File.Exists(from)) {
                Console.WriteLine($"Uploading file {from}");
                if (ReplaceBG) {
                    string content = File.ReadAllText(from);
                    content = Program.ReplaceBlueGreen(content);
                    byte[] btes = Encoding.ASCII.GetBytes(content);
                    ftpClient.Upload(btes, to, createRemoteDir: true);
                } else {
                    if (Conditional) {
                        if (ftpClient.FileExists(to)) {
                            long sizeTo = ftpClient.GetFileSize(to);
                            DateTime timeTo = ftpClient.GetModifiedTime(to);
                            FileInfo fi = new FileInfo(from);
                            long sizeFrom = fi.Length;
                            DateTime timeFrom = File.GetLastWriteTime(from);
                            if (sizeTo != sizeFrom || timeFrom >= timeTo)
                                ftpClient.UploadFile(from, to, createRemoteDir: true);
                            else
                                Console.WriteLine($"Skipped - already uploaded");
                        } else {
                            ftpClient.UploadFile(from, to, createRemoteDir: true);
                        }
                    } else {
                        ftpClient.UploadFile(from, to, createRemoteDir: true);
                    }
                }
            } else
                throw new Error($"Can't upload {from} - not found");
        }
    }
}
