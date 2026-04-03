/* ---------------------------------------------------------------------*
*                       paxScript.NET, version 2.7                      *
*   Copyright (c) 2005-2010 Alexander Baranovsky. All Rights Reserved   *
*                                                                       *
* THE SOURCE CODE CONTAINED HEREIN AND IN RELATED FILES IS PROVIDED     *
* TO THE REGISTERED DEVELOPER. UNDER NO CIRCUMSTANCES MAY ANY PORTION   *
* OF THE SOURCE CODE BE DISTRIBUTED, DISCLOSED OR OTHERWISE MADE        *
* AVAILABLE TO ANY THIRD PARTY WITHOUT THE EXPRESS WRITTEN CONSENT OF   *
* AUTHOR.                                                               *
*                                                                       *
* THIS COPYRIGHT NOTICE MAY NOT BE REMOVED FROM THIS FILE.              *
* --------------------------------------------------------------------- *
*/

using System;

namespace PaxScript.Net
{
    #region ScriptError Class
    /// <summary>
    /// Represents error or warning.
    /// </summary>
    public class ScriptError
    {
        /// <summary>
        /// Message of error.
        /// </summary>
        string message;

        /// <summary>
        /// Name of module which contains given error.
        /// </summary>
        string module_name;

        /// <summary>
        /// Line number of p-code which contains given error.
        /// </summary>
        int pcode_line;

        /// <summary>
        /// Line number of source code which contains given error.
        /// </summary>
        int line_number;

        /// <summary>
        /// Line of source code which contains given error.
        /// </summary>
        string line;

        /// <summary>
        /// Undocumented.
        /// </summary>
        public Exception E;

        /// <summary>
        /// Undocumented.
        /// </summary>
        public BaseScripter scripter;

        /// <summary>
        /// Constructor.
        /// </summary>
        internal ScriptError(BaseScripter scripter, string message)
        {
            this.message = message;

            pcode_line = scripter.code.n;
            if (pcode_line == 0) pcode_line = scripter.code.Card;

            Module m = scripter.code.GetModule(pcode_line);
            if (m == null)
            {
                module_name = "";
                line_number = 0;
                line = "";
            }
            else
            {
                module_name = m.Name;
                line_number = scripter.code.GetErrorLineNumber(pcode_line);
                line = m.GetLine(line_number);

                while (IsEmptyLine(line))
                {
                    pcode_line++;
                    line_number++;
                    line = m.GetLine(line_number);
                    if (pcode_line >= scripter.code.Card)
                        break;
                }
            }
            E = null;
        }

        bool IsEmptyLine(string s)
        {
            for (int i = 0; i < s.Length - 1; i++)
            {
                char c = s[i];
                if (c == '\u000A' ||
                    c == '\u000D' ||
                    c == '\u0085' ||
                    c == '\u2028' ||
                    c == '\u2029' ||
                    c == '\u0009' ||
                    c == '\u000B' ||
                    c == '\u000C' ||
                    c == ' ')
                    continue;
                else
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Returns message of error.
        /// </summary>
        public string Message
        {
            get
            {
                return message;
            }
        }

        /// <summary>
        /// Returns name of module which contains given error.
        /// </summary>
        public string ModuleName
        {
            get
            {
                return module_name;
            }
        }

        /// <summary>
        /// Returns line number of source code which contains given error.
        /// </summary>
        public int LineNumber
        {
            get
            {
                return line_number;
            }
        }

        /// <summary>
        /// Returns line number of p-code which contains given error.
        /// </summary>
        public int PCodeLineNumber
        {
            get
            {
                return pcode_line;
            }
        }

        /// <summary>
        /// Returns line of source code which contains given error.
        /// </summary>
        public string Line
        {
            get
            {
                return line;
            }
        }
    }
    #endregion ScriptError Class
}
