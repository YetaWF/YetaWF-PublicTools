/* Copyright © 2021 Softel vdm, Inc. - https://yetawf.com/Documentation/YetaWF/Licensing */

using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace RetrievePages
{

    class Program {

        public static string UserAgent = "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/55.0.2883.87 Safari/537.36";

        private static HttpClientHandler Handler = new HttpClientHandler {
            AllowAutoRedirect = true,
            UseCookies = true,
        };
        private static HttpClient Client = new HttpClient(Handler, true) { 
             Timeout = new TimeSpan(0, 1, 0),
             
        };

        static async Task Main(string[] args) {

            DateTime start = DateTime.UtcNow;
            int pageCount = 0;

            string line;
            while ((line = Console.ReadLine()) != null) {
                line = line.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("-- ")) continue;
                if (line.StartsWith("LOGIN POST ")) {
                    Console.WriteLine("Logging on...");
                    await LoginAsync(line.Substring(11), Post: true);
                } else if (line.StartsWith("LOGIN GET ")) {
                    Console.WriteLine("Logging on...");
                    await LoginAsync(line.Substring(10), Post: false);
                } else if (line.StartsWith("LOGOFF ")) {
                    Console.WriteLine("Logging off...");
                    await RetrievePageAsync(line.Substring(7));
                } else
                    await RetrievePageAsync(line);
                ++pageCount;
            }

            DateTime end = DateTime.UtcNow;
            TimeSpan ts = end.Subtract(start);
            Console.WriteLine($"Load time: {ts} - {pageCount}");
        }

        private static async Task LoginAsync(string url, bool Post) {
            HttpResponseMessage resp = null;
            try {
                using (var request = new HttpRequestMessage(Post ? HttpMethod.Post : HttpMethod.Get, url)) {
                    request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                    resp = await Client.SendAsync(request);
                }
            } catch (Exception exc) {
                Console.WriteLine(exc.Message);
                throw;
            }
            int statusCode = -1;
            string html = "";
            if (resp != null) {
                statusCode = (int)resp.StatusCode;
                html = await resp.Content.ReadAsStringAsync();
                resp.Dispose();
            }
            Console.WriteLine("{0} {1}", statusCode, html);
        }

        private static async Task RetrievePageAsync(string url) {

            Console.WriteLine(url);

            DateTime start = DateTime.UtcNow;

            HttpResponseMessage resp = null;
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url)) {
                    request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                    resp = await Client.SendAsync(request);
                }
            } catch (Exception exc) {
                Console.WriteLine(exc.Message);
                throw;
            }
            int statusCode = -1;
            if (resp != null) {
                statusCode = (int)resp.StatusCode;
                resp.Dispose();
            }

            DateTime end = DateTime.UtcNow;
            TimeSpan ts = end.Subtract(start);
            Console.WriteLine("{0} {1}", statusCode, ts.ToString());
        }
    }
}
