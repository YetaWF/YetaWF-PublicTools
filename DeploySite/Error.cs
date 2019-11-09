﻿using System;

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
    }
}