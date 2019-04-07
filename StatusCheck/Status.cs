/* Copyright © 2019 Softel vdm, Inc. - https://yetawf.com/Documentation/YetaWF/StatusCheck#License */

using System.Collections.Generic;

namespace YetaWF.PublicTools.StatusCheck {

    /// <summary>
    /// An instance of this class contains the settings found in StatusCheck.json.
    /// </summary>
    public class Settings {
        /// <summary>
        /// The domain name used to access data. This must be an existing domain with a YetaWF site and Appsettings.json must contain data provider information.
        /// </summary>
        public string SiteDomain { get; set; }
        /// <summary>
        /// The interval (in seconds) used to check all specified URLs.
        /// </summary>
        public int Interval { get; set; }
        /// <summary>
        /// A collection of URLs to check.
        /// </summary>
        public List<string> URLs { get; set; }
        /// <summary>
        /// A collection of phone numbers that receive SMS notifications when a site goes down (or comes back up).
        /// </summary>
        public List<string> SMSNotify { get; set; }
        /// <summary>
        /// The timeout (in seconds) used when retrieving URLs. If there is no response after the specified number of seconds, the site is considered down.
        /// </summary>
        public int Timeout { get; set; }
    }

}
