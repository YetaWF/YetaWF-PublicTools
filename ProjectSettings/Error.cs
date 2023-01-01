/* Copyright © 2023 Softel vdm, Inc. - https://yetawf.com/Documentation/YetaWF/Licensing */

using System;

namespace Softelvdm.Tools.ProjectSettings {

    public class Messages {

        /// <summary>
        /// Displays a message.
        /// </summary>
        /// <param name="text">Message text.</param>
        /// <returns>Returns the message text.</returns>
        public static string Message(string text) {
            return Message(text, null);
        }
        /// <summary>
        /// Displays a formatted message.
        /// </summary>
        /// <param name="text">Message text.</param>
        /// <param name="args">Arguments used to formate the message text (using string.Format).</param>
        /// <returns>Returns the formatted message text.</returns>
        public static string Message(string text, params object[] args) {
            string s = text;
            if (args != null)
                s = string.Format(text, args);
            Console.WriteLine(s);
            return s;
        }
        /// <summary>
        /// Create an ApplicationException object using the specified message.
        /// </summary>
        /// <param name="text">Message text.</param>
        /// <param name="args">Arguments used to formate the message text (using string.Format).</param>
        /// <returns>Returns an ApplicationException object using the specified message.</returns>
        public static ApplicationException Error(string text, params object[] args) {
            string s = text;
            if (args != null)
                s = string.Format(text, args);
            return new ApplicationException(s);
        }
    }
}
