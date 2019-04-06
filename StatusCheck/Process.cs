/* Copyright © 2019 Softel vdm, Inc. - https://yetawf.com/Documentation/YetaWF/StatusCheck#License */

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using YetaWF.Core.Extensions;
using YetaWF.Core.Support.SendSMS;

namespace YetaWF.PublicTools.StatusCheck {

    public partial class Program {

        /// <summary>
        /// Processes all URLs at the specified interval and sends SMS notifications if a site is found down (or back up).
        /// </summary>
        /// <returns></returns>
        public async Task Process() {

            bool[] LastFailures = new bool[Settings.URLs.Count];
            for (int i = 0; i < LastFailures.Length; ++i)
                LastFailures[i] = false;

            for (;;) {
                bool[] failures = new bool[Settings.URLs.Count];

                int index = 0;
                foreach (string page in Settings.URLs) {

                    string site = page.TruncateStart("http://").TruncateStart("https://");
                    failures[index] = false;

                    string msg = null;

                    int status = RetrievePage(page);
                    if (status != 200) {
                        failures[index] = true;
                        if (LastFailures[index] != failures[index])
                            msg = $"Site {site} is DOWN!";
                    } else {
                        failures[index] = false;
                        if (LastFailures[index] != failures[index])
                            msg = $"Site {site} is back UP";
                    }
                    LastFailures[index] = failures[index];
                    index++;

                    if (!string.IsNullOrWhiteSpace(msg)) {
                        Console.WriteLine(msg);
                        SendSMS sendSMS = new SendSMS();
                        foreach (string smsNotify in Settings.SMSNotify) {
                            await sendSMS.SendMessageAsync(smsNotify, msg);
                        }
                    }
                }
                Thread.Sleep(Settings.Interval * 1000);
            }
        }

        private static string UserAgent = "Mozilla/5.0 (Windows NT 10.0; WOW64) StatusCheckBox AppleWebKit/537.36 (KHTML, like Gecko) Chrome/55.0.2883.87 Safari/537.36";

        /// <summary>
        /// Retrieves on URL and returns its status.
        /// </summary>
        /// <param name="url">The fully qualified URL to retrieve.</param>
        /// <returns>Returns the retrieval status. 200 is successful. Everything else is considered a failure.</returns>
        public static int RetrievePage(string url) {

            Console.WriteLine(url);

            DateTime start = DateTime.UtcNow;

            HttpWebRequest req = null;
            HttpWebResponse resp = null;
            try {
                req = (HttpWebRequest)WebRequest.Create(url);
                req.AllowAutoRedirect = true;
                req.Timeout = Settings.Timeout * 1000;
                req.UserAgent = UserAgent;
                resp = (HttpWebResponse)req.GetResponse();
            } catch (Exception exc) {
                Console.WriteLine(exc.Message);
            }
            int statusCode = -1;
            if (resp != null) {
                statusCode = (int)resp.StatusCode;
                resp.Close();
            }

            DateTime end = DateTime.UtcNow;
            TimeSpan ts = end.Subtract(start);
            Console.WriteLine($"{DateTime.Now}: {statusCode} {ts.ToString()}");

            return statusCode;
        }
    }
}
