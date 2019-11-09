﻿using System;
using System.IO;

namespace Softelvdm.Tools.DeploySite {

    public class IOHelper {

        public static void DeleteFolder(string targetFolder) {

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
    }
}