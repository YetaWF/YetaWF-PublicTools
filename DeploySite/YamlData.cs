using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Softelvdm.Tools.DeploySite {

    public class YamlData {
        public List<Database> Databases { get; set; }
        [Required]
        public Deploy Deploy { get; set; }
        public FTP FTP { get; set; }
        [Required]
        public Site Site { get; set; }
    }

    public class Database {
        [Required]
        public string DevDB { get; set; }
        [Required]
        public string DevServer { get; set; }
        [Required]
        public string DevUsername { get; set; }
        [Required]
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
        [Required]
        public string From { get; set; }
        [Required]
        public string BaseFolder { get; set; }
        [Required]
        public string Website { get; set; }
        public string ConfigParm { get; set; }
        public bool Localization { get; set; } = true;
        public bool SiteTemplates { get; set; } = true;
    }
    public class FTP {
        [Required]
        public string Server { get; set; }
        public int Port { get; set; } = 21;
        [Required]
        public string User { get; set; }
        [Required]
        public string Password { get; set; }
        [Required]
        public List<FTPCopy> Copy { get; set; }
    }

    public class FTPCopy {
        [Required]
        public string From { get; set; }
        [Required]
        public string To { get; set; }
        public bool ReplaceBG { get; set; }
        public bool Conditional { get; set; }
    }

    public class Site {
        [Required]
        public string Location { get; set; }
        [Required]
        public string Zip { get; set; }

        public List<RunCommand> RunFirst { get; set; }
        public List<RunCommand> Run { get; set; }
    }

    public class RunCommand {
        [Required]
        public string Command { get; set; }
        public bool IgnoreErrors { get; set; } = false;
    }
}
