/* Copyright © 2022 Softel vdm, Inc. - https://yetawf.com/Documentation/YetaWF/Licensing */

using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Softelvdm.Tools.DeploySite {

    public class BackupZipFile : IDisposable {

        public string FileName { get; private set; }
        public List<BackupZipEntry> Entries { get; private set; }

        public class BackupZipEntry {
            public string RelativeName { get; set; }
            public string AbsoluteFileName { get; set; }
            public string Data { get; set; }
        }

        public BackupZipFile(string fileName) {
            FileName = fileName;
            Entries = new List<BackupZipEntry>();
        }

        public void Dispose() { Dispose(true); }
        protected virtual void Dispose(bool disposing) { }

        public void AddFile(string absFileName, string fileName) {
            Entries.Add(new BackupZipEntry {
                AbsoluteFileName = absFileName,
                RelativeName = CleanFileName(fileName),
            });
        }
        public static string CleanFileName(string fileName) {
            fileName = fileName.Replace("\\", "/");
            if (fileName.StartsWith("/"))
                fileName = fileName.Substring(1);
            return fileName;
        }
        public void AddData(string data, string fileName) {
            Entries.Add(new BackupZipEntry {
                Data = data,
                RelativeName = fileName,
            });
        }
        public void AddFolder(string tempFolder) {
            List<string> files = Directory.GetFiles(tempFolder).ToList();
            foreach (string file in files)
                AddFile(file, Path.GetFileName(file));
        }
        public void Save() {
            using (FileStream fs = new FileStream(FileName, FileMode.Create)) {
                Save(fs);
            }
        }
        public void Save(Stream stream) {
            // add all files
            using (ZipOutputStream zipStream = new ZipOutputStream(stream)) {
                zipStream.SetLevel(5);
                foreach (BackupZipEntry entry in this.Entries) {
                    if (entry.Data != null)
                        WriteData(zipStream, entry.Data, entry.RelativeName);
                    else
                        WriteFile(zipStream, entry.AbsoluteFileName, entry.RelativeName);
                }
            }
        }

        private void WriteData(ZipOutputStream zipStream, string data, string relativeName) {
            ZipEntry newEntry = new ZipEntry(relativeName);

            using (MemoryStream ms = new MemoryStream()) {
                // create a memory stream from the string
                using (StreamWriter writer = new StreamWriter(ms, System.Text.Encoding.ASCII)) {
                    writer.Write(data);
                    writer.Flush();
                    ms.Position = 0;

                    newEntry.Size = ms.Length;
                    zipStream.PutNextEntry(newEntry);

                    byte[] buffer = new byte[4096];
                    StreamUtils.Copy(ms, zipStream, buffer);
                }
            }
        }

        private void WriteFile(ZipOutputStream zipStream, string absoluteFileName, string relativeName) {
            using (FileStream fs = new FileStream(absoluteFileName, FileMode.Open)) {
                DateTime lastWrite = File.GetLastWriteTimeUtc(absoluteFileName);
                ZipEntry newEntry = new ZipEntry(relativeName);
                newEntry.DateTime = lastWrite;
                newEntry.Size = fs.Length;
                zipStream.PutNextEntry(newEntry);

                byte[] buffer = new byte[4096];
                StreamUtils.Copy(fs, zipStream, buffer);

                fs.Close();
            }
        }
    }
}
