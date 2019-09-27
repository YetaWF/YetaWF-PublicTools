/* Copyright © 2019 Softel vdm, Inc. - https://yetawf.com/Documentation/YetaWF/Licensing */

using System;
using System.Collections.Generic;
using System.IO;
using YetaWF.Core.Support;

namespace YetaWF.PublicTools.StatusCheck {

    /// <summary>
    /// The class implementing the StatusCheck console application.
    /// </summary>
    public partial class Program {

        /// <summary>
        /// The name of the JSON file containing settings for the StatusCheck console application.
        /// </summary>
        public static readonly string SETTINGSFILE = "StatusCheck.json";
        /// <summary>
        /// Contains the settings found in StatusCheck.json.
        /// </summary>
        public static Settings Settings { get; set; }

        /// <summary>
        /// A collection maintaining the status of each URL.
        /// </summary>
        public List<bool> LastFailures = new List<bool>();

        static void Main(string[] args) {

            Console.Title = "Softel vdm, Inc. - StatusCheck";
            Console.WriteLine("Initializing...");

            // StatusCheck console application settings
            string text = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SETTINGSFILE));
            Settings = Newtonsoft.Json.JsonConvert.DeserializeObject<Settings>(text);

            // Initialize the YetaWF console application
            Console.WriteLine("Starting YetaWF Support...");
            StartupBatch.Start(AppDomain.CurrentDomain.BaseDirectory, Settings.SiteDomain);

            Console.WriteLine("Processing...");

            YetaWFManager.Syncify(() => {
                Program pgm = new StatusCheck.Program();
                return pgm.Process();
            });
        }
    }
}
