/* Copyright © 2023 Softel vdm, Inc. - https://yetawf.com/Documentation/YetaWF/Licensing */

using Newtonsoft.Json;
using System.IO;

namespace Softelvdm.Tools.DeploySite {

    public class JSONFile {
        public Variables Variables { get; set; }
    }

    public class Variables {

        public string Server { get; set; }

        public string SQLServer { get; set; }
        public string SQLUser { get; set; }
        public string SQLPassword { get; set; }

        public bool ubackupRestore { get; set; }

        public string Preload { get; set; }

        public static Variables LoadVariables(string fileName) {
            if (!File.Exists(fileName))
                throw new Error("Variables file not found ({0})", fileName);
            JSONFile jsonFile = JsonConvert.DeserializeObject<JSONFile>(File.ReadAllText(fileName));
            return jsonFile.Variables;
        }
    }
}

