/* Copyright © 2019 Softel vdm, Inc. - https://yetawf.com/Documentation/YetaWF/Licensing */

using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace Softelvdm.Tools.DeploySite {

    public partial class Program {

        public enum CopyAction {
            Backup = 1,
            Restore = 2,
        }
        public enum BlueGreenDeployEnum {
            None = 0,
            Blue = 1,
            Green = 2,
        }

        public static BlueGreenDeployEnum BlueGreenDeploy { get; set; }
        public static YamlData YamlData { get; set; }

        public const string DBFOLDER = "DBs";
        public const string DATAFOLDER = "Data";
        public const string MARKERMVC6 = "wwwroot";
        public const string MAINTENANCEFOLDER = "Maintenance";

        static int Main(string[] args) {
            Console.Title = "YetaWF Deploy Site";
            Program prog = new DeploySite.Program();
            return prog.Run(args);
        }

        private const string USAGE = "Usage: {0} {{Backup|Restore}} \"...yaml file with deploy config...\"  [{{Blue|Green}}] ";

        private int Run(string[] args) {

            System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            if (args.Length < 2 || args.Length > 3)
                return Usage();

            // Determine Backup/Restore
            CopyAction action;
            if (args[0].ToLower() == "backup") {
                action = CopyAction.Backup;
            } else if (args[0].ToLower() == "restore") {
                action = CopyAction.Restore;
            } else
                return Usage();

            // Determine blue/green
            if (args.Length == 3) {

                if (action == CopyAction.Restore)
                    throw new Error("Can't specify Blue/Green when using Restore");

                string blueGreen = args[2].ToLower();
                if (blueGreen == "blue")
                    BlueGreenDeploy = BlueGreenDeployEnum.Blue;
                else if (blueGreen == "green")
                    BlueGreenDeploy = BlueGreenDeployEnum.Green;
                else
                    return Usage();
            }

            // Parse Yaml input file
            string input = args[1];
            string content = null;
            try {
                content = File.ReadAllText(input);
            } catch (Exception) {
                Console.WriteLine($"Input file {input} not found");
                throw;
            }
            content = ReplaceBlueGreen(content);

            try {
                YamlDotNet.Serialization.IDeserializer deserializer = Yaml.GetDeserializer();
                YamlData = deserializer.Deserialize<YamlData>(content);
            } catch (Exception exc) {
                Console.WriteLine($"Input file {input} is invalid");
                while (exc != null) {
                    Console.WriteLine(exc.Message);
                    exc = exc.InnerException;
                }
                throw;
            }

            if (action == CopyAction.Backup) {
                Backup backup = new Backup();
                backup.PerformBackup();
            } else {
                Restore restore = new Restore();
                restore.PerformRestore();
            }

            return 0;
        }

        public static string ReplaceBlueGreen(string content) {
            if (BlueGreenDeploy != BlueGreenDeployEnum.None) {
                content = content.Replace("{bluegreen}", BlueGreenDeploy == BlueGreenDeployEnum.Blue ? "blue" : "green");
                content = content.Replace("{BLUEGREEN}", BlueGreenDeploy == BlueGreenDeployEnum.Blue ? "Blue" : "Green");
                content = content.Replace("{-BLUEGREEN}", BlueGreenDeploy == BlueGreenDeployEnum.Blue ? "-Blue" : "-Green");
                content = content.Replace("{BLUEGREEN-OTHER}", BlueGreenDeploy == BlueGreenDeployEnum.Blue ? "Green" : "Blue");
                content = content.Replace("{-BLUEGREEN-OTHER}", BlueGreenDeploy == BlueGreenDeployEnum.Blue ? "-Green" : "-Blue");
            } else {
                if (content.Contains("{bluegreen}") || content.Contains("{BLUEGREEN}") || content.Contains("{-BLUEGREEN}") || content.Contains("{BLUEGREEN-OTHER}") || content.Contains("{-BLUEGREEN-OTHER}"))
                    throw new Error("BLUEGREEN variable found but this is not a blue-green deploy");
            }
            return content;
        }
        private int Usage() {
            Assembly asm = Assembly.GetExecutingAssembly();
            Console.WriteLine(USAGE, asm.ManifestModule.Name);
            return -1;
        }
    }
}
