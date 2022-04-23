/* Copyright © 2022 Softel vdm, Inc. - https://yetawf.com/Documentation/YetaWF/Licensing */

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Softelvdm.Tools.DeploySite {

    public class YamlData {
        public List<Database> Databases { get; set; }
        public List<FTP> FTPs { get; set; }
        [Required]
        public Deploy Deploy { get; set; }
        public Local Local { get; set; }
        public Site Site { get; set; }
        public List<FTPCopy> FTPCopy { get; set; }
    }

    public class Database {

        public string Bacpac { get; set; }
        public string ubackup { get; set; }

        public string DevDB { get; set; }
        public string DevServer { get; set; }
        public string DevUsername { get; set; }
        public string DevPassword { get; set; }

        [Required]
        public string ProdDB { get; set; }
        [Required]
        public string ProdServer { get; set; }
        [Required]
        public string ProdUsername { get; set; }
        [Required]
        public string ProdPassword { get; set; }
    }
    public class Deploy {
        [Required]
        public string Type { get; set; }
        [Required]
        public string To { get; set; }
        public string From { get; set; }
        [Required]
        public string BaseFolder { get; set; }
        public string ConfigParm { get; set; }
        public bool Localization { get; set; } = true;
        public bool SiteTemplates { get; set; } = true;
        public bool Debug { get; set; } = false;
        public string DetermineBlueGreen { get; set; }
        public string BlueRegex { get; set; }
        public string GreenRegex { get; set; }
    }

    public class FTP {
        [Required]
        public string Server { get; set; }
        public int Port { get; set; } = 21;
        [Required]
        public string User { get; set; }
        [Required]
        public string Password { get; set; }
    }
    public class FTPCopy {
        [Required]
        public string From { get; set; }
        [Required]
        public string To { get; set; }
        public bool ReplaceBG { get; set; }
        public bool Conditional { get; set; }
    }

    public class Local {
        [Required]
        public string PublishFolder { get; set; }
        public List<LocalCopy> Copy { get; set; }
    }
    public class LocalCopy {
        [Required]
        public string From { get; set; }
        [Required]
        public string To { get; set; }
        public bool ReplaceBG { get; set; }
    }

    public class Site {
        public string Include { get; set; }
        [Required]
        public string Location { get; set; }
        [Required]
        public string Zip { get; set; }

        public string Sqlcmd { get; set; }
        public string Sqlpackage { get; set; }

        public bool Maintenance { get; set; }

        public List<RunCommand> RunFirst { get; set; }
        public List<RunCommand> Run { get; set; }
    }

    public class RunCommand {
        [Required]
        public string Command { get; set; }
        public bool IgnoreErrors { get; set; } = false;
    }
}
