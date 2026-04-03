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
    #region ScriptResult Class
    /// <summary>
    /// Represents error or warning.
    /// </summary>
    public class ScriptResult
    {
        /// <summary>
        /// Name of variable.
        /// </summary>
        string name;

        /// <summary>
        /// Type name of variable.
        /// </summary>
        string type_name;

        /// <summary>
        /// Value of variable.
        /// </summary>
        object value;

        /// <summary>
        /// Undocumented.
        /// </summary>
        public BaseScripter scripter;

        /// <summary>
        /// Constructor.
        /// </summary>
        internal ScriptResult(BaseScripter ascripter, int id)
        {
            scripter = ascripter;
            name = scripter.symbol_table[id].Name;
            int type_id = scripter.symbol_table[id].TypeId;
            type_name = scripter.symbol_table[type_id].FullName;
            value = scripter.symbol_table[id].Value;
        }

        /// <summary>
        /// Returns name of variable.
        /// </summary>
        public string Name
        {
            get
            {
                return name;
            }
        }

        /// <summary>
        /// Returns type name of variable.
        /// </summary>
        public string TypeName
        {
            get
            {
                return type_name;
            }
        }

        /// <summary>
        /// Returns value of variable.
        /// </summary>
        public object Value
        {
            get
            {
                return value;
            }
        }
    }
    #endregion ScriptResult Class
}
