/* Copyright © 2020 Softel vdm, Inc. - https://yetawf.com/Documentation/YetaWF/Licensing */

using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

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
        public static string YamlRawContent { get; set; }

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
#if MVC6
            System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
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

            // Read Yaml input file
            string input = args[1];
            try {
                YamlRawContent = File.ReadAllText(input);
            } catch (Exception) {
                Console.WriteLine($"Input file {input} not found");
                throw;
            }

            // Find out what to do

            string content;

            // Determine Blue/Green
            if (args.Length == 3) {

                string blueGreen = args[2].ToLower();
                if (blueGreen == "blue")
                    BlueGreenDeploy = BlueGreenDeployEnum.Blue;
                else if (blueGreen == "green")
                    BlueGreenDeploy = BlueGreenDeployEnum.Green;
                else
                    return Usage();

                if (action == CopyAction.Restore)
                    throw new Error("Can't specify Blue/Green when using Restore");
            } else {

                if (action == CopyAction.Backup) {
                    // find out whether Blue or Green is running

                    YamlData yamlData;
                    try {
                        YamlDotNet.Serialization.IDeserializer deserializer = Yaml.GetDeserializer();
                        yamlData = deserializer.Deserialize<YamlData>(YamlRawContent);
                    } catch (Exception exc) {
                        Console.WriteLine($"Input file {input} is invalid");
                        string msg = Error.FormatExceptionMessage(exc);
                        Console.WriteLine(msg);
                        throw;
                    }
                    // note that yamlData still has Blue/Green variables
                    if (!string.IsNullOrWhiteSpace(yamlData.Deploy.DetermineBlueGreen)) {
                        if (string.IsNullOrWhiteSpace(yamlData.Deploy.BlueRegex))
                            throw new Error($"Can't determine Blue/Green - Yaml file doesn't define {nameof(yamlData.Deploy)}:{nameof(yamlData.Deploy.BlueRegex)}");
                        if (string.IsNullOrWhiteSpace(yamlData.Deploy.GreenRegex))
                            throw new Error($"Can't determine Blue/Green - Yaml file doesn't define {nameof(yamlData.Deploy)}:{nameof(yamlData.Deploy.GreenRegex)}");

                        BlueGreenDeploy = DetermineNewBlueGreen(yamlData);
                    }
                }
            }

            content = ReplaceBlueGreen(YamlRawContent);

            // deserialize yamldata - yamlData no longer has Blue/Green variables
            try {
                YamlDotNet.Serialization.IDeserializer deserializer = Yaml.GetDeserializer();
                YamlData = deserializer.Deserialize<YamlData>(content);
            } catch (Exception exc) {
                Console.WriteLine($"Input file {input} is invalid");
                string msg = Error.FormatExceptionMessage(exc);
                Console.WriteLine(msg);
                throw;
            }

            try {
                if (action == CopyAction.Backup) {
                    Backup backup = new Backup();
                    backup.PerformBackup();
                } else {
                    Restore restore = new Restore();
                    restore.PerformRestore();
                }
            } catch (Exception exc) {
                string msg = Error.FormatExceptionMessage(exc);
                Console.WriteLine(msg);
                throw;
            }

            return 0;
        }

        private BlueGreenDeployEnum DetermineNewBlueGreen(YamlData yamlData) {
            using (HttpClient client = new HttpClient()) {
                string data;
                try {
                    data = client.GetStringAsync(yamlData.Deploy.DetermineBlueGreen).Result;
                } catch (Exception) {
                    Console.WriteLine($"Retrieving Blue/Green status from {yamlData.Deploy.DetermineBlueGreen} failed");
                    throw;
                }

                Regex reBlue = new Regex(yamlData.Deploy.BlueRegex, RegexOptions.IgnoreCase);
                bool blue = reBlue.IsMatch(data);
                Regex reGreen = new Regex(yamlData.Deploy.GreenRegex, RegexOptions.IgnoreCase);
                bool green = reGreen.IsMatch(data);

                BlueGreenDeployEnum newDeploy;
                if (blue && green)
                    throw new Error($"Both Blue and Green found - Please check {nameof(yamlData.Deploy)}:{nameof(yamlData.Deploy.BlueRegex)} and {nameof(yamlData.Deploy)}:{nameof(yamlData.Deploy.GreenRegex)}");
                if (blue) {
                    Console.WriteLine($"{yamlData.Deploy.DetermineBlueGreen} is currently running Blue - Backing up for deployment to Green");
                    newDeploy = BlueGreenDeployEnum.Green;
                } else if (green) {
                    Console.WriteLine($"{yamlData.Deploy.DetermineBlueGreen} is currently running Green - Backing up for deployment to Blue");
                    newDeploy = BlueGreenDeployEnum.Blue;
                } else {
                    throw new Error($"Unable to determine Blue/Green - Neither Blue nor Green found - Please check {nameof(yamlData.Deploy)}:{nameof(yamlData.Deploy.BlueRegex)} and {nameof(yamlData.Deploy)}:{nameof(yamlData.Deploy.GreenRegex)}");
                }
                return newDeploy;
            }
        }

        public static string ReplaceBlueGreen(string content) {
            if (BlueGreenDeploy != BlueGreenDeployEnum.None) {
                content = content.Replace("{bluegreen}", BlueGreenDeploy == BlueGreenDeployEnum.Blue ? "blue" : "green");
                content = content.Replace("{BLUEGREEN}", BlueGreenDeploy == BlueGreenDeployEnum.Blue ? "BLUE" : "GREEN");
                content = content.Replace("{-BLUEGREEN}", BlueGreenDeploy == BlueGreenDeployEnum.Blue ? "-BLUE" : "-GREEN");
                content = content.Replace("{BLUEGREEN-OTHER}", BlueGreenDeploy == BlueGreenDeployEnum.Blue ? "GREEN" : "BLUE");
                content = content.Replace("{-BLUEGREEN-OTHER}", BlueGreenDeploy == BlueGreenDeployEnum.Blue ? "-GREEN" : "-BLUE");
            } else {
                if (content.Contains("{bluegreen}") || content.Contains("{BLUEGREEN}") || content.Contains("{-BLUEGREEN}") || content.Contains("{BLUEGREEN-OTHER}") || content.Contains("{-BLUEGREEN-OTHER}"))
                    throw new Error("BLUEGREEN variable found but this is not a Blue-Green deploy");
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
