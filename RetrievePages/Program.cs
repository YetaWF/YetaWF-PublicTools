/* Copyright © 2020 Softel vdm, Inc. - https://yetawf.com/Documentation/YetaWF/Licensing */

using System;
using System.IO;
using System.Net;
using System.Text;

namespace RetrievePages {

    class Program {

        public static string UserAgent = "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/55.0.2883.87 Safari/537.36";

        public static CookieContainer cookieContainer = new CookieContainer();

        static void Main(string[] args) {

            DateTime start = DateTime.UtcNow;
            int pageCount = 0;

            string line;
            while ((line = Console.ReadLine()) != null) {
                line = line.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("-- ")) continue;
                if (line.StartsWith("LOGIN POST ")) {
                    Console.WriteLine("Logging on...");
                    Login(line.Substring(11), Post: true);
                } else if (line.StartsWith("LOGIN GET ")) {
                    Console.WriteLine("Logging on...");
                    Login(line.Substring(10), Post: false);
                } else if (line.StartsWith("LOGOFF ")) {
                    Console.WriteLine("Logging off...");
                    RetrievePage(line.Substring(7));
                } else
                    RetrievePage(line);
                ++pageCount;
            }

            DateTime end = DateTime.UtcNow;
            TimeSpan ts = end.Subtract(start);
            Console.WriteLine($"Load time: {ts} - {pageCount}");
        }

        private static void Login(string url, bool Post) {
            HttpWebRequest req = null;
            HttpWebResponse resp = null;
            try {
                req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = Post ? "POST" : "GET";
                req.ContentLength = 0;
                req.CookieContainer = cookieContainer;
                req.AllowAutoRedirect = true;
                req.Timeout = 60000;
                req.UserAgent = UserAgent;
                resp = (HttpWebResponse)req.GetResponse();
            } catch (Exception exc) {
                Console.WriteLine(exc.Message);
                throw;
            }
            int statusCode = -1;
            string html = "";
            if (resp != null) {
                statusCode = (int)resp.StatusCode;
                html = ReadStreamFile(resp);
                resp.Close();
            }
            Console.WriteLine("{0} {1}", statusCode, html);
        }

        private static void RetrievePage(string url) {

            Console.WriteLine(url);

            DateTime start = DateTime.UtcNow;

            HttpWebRequest req = null;
            HttpWebResponse resp = null;
            try {
                req = (HttpWebRequest)WebRequest.Create(url);
                req.CookieContainer = cookieContainer;
                req.AllowAutoRedirect = true;
                req.Timeout = 10*60*1000; // 10 minutes
                req.UserAgent = UserAgent;
                resp = (HttpWebResponse)req.GetResponse();
            } catch (Exception exc) {
                Console.WriteLine(exc.Message);
                throw;
            }
            int statusCode = -1;
            if (resp != null) {
                statusCode = (int)resp.StatusCode;
                resp.Close();
            }

            DateTime end = DateTime.UtcNow;
            TimeSpan ts = end.Subtract(start);
            Console.WriteLine("{0} {1}", statusCode, ts.ToString());
        }

        private static string ReadStreamFile(HttpWebResponse resp) {
            const int CHUNKSIZE = 8192;
            StringBuilder s = new StringBuilder();
            using (Stream strm = resp.GetResponseStream()) {

                byte[] bts = new byte[CHUNKSIZE];
                for ( ; ; ) {
                    int nRead = strm.Read(bts, 0, CHUNKSIZE);
                    if (nRead == 0)
                        break;// EOF
                    System.Text.Encoding enc = System.Text.Encoding.ASCII;
                    s.Append(enc.GetString(bts, 0, nRead));
                }
            }
            return s.ToString();
        }
    }
}
