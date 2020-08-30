/* Copyright © 2020 Softel vdm, Inc. - https://yetawf.com/Documentation/YetaWF/Licensing */

using System;

namespace Softelvdm.Tools.DeploySite {

    public class Error : System.Exception {
        public Error(string message) {
            Console.WriteLine(message);
            throw new ApplicationException(message);
        }
        public Error(string format, params object[] args) {
            string message = string.Format(format, args);
            Console.WriteLine(message);
            throw new ApplicationException(message);
        }

        public static string FormatExceptionMessage(Exception exc) {
            if (exc == null) return "";
            string message = "(none)";
            if (exc.Message != null && !string.IsNullOrWhiteSpace(exc.Message))
                message = exc.Message;
            if (exc is AggregateException) {
                AggregateException aggrExc = (AggregateException)exc;
                foreach (Exception innerExc in aggrExc.InnerExceptions) {
                    string s = FormatExceptionMessage(innerExc);
                    if (s != null)
                        message += " - " + s;
                }
            } else {
                while (exc.InnerException != null) {
                    exc = exc.InnerException;
                    string s = FormatExceptionMessage(exc);
                    if (s != null)
                        message += " - " + s;
                }
            }
            return message;
        }
    }
}
