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
	#region Breakpoint Class
	/// <summary>
	/// Represents a breakpoint.
	/// </summary>
	public sealed class Breakpoint
	{
		/// <summary>
		/// Represents kernel of scripter.
		/// </summary>
		BaseScripter scripter;

		/// <summary>
		/// P-code line number.
		/// </summary>
		int n;

		/// <summary>
		/// Name of module.
		/// </summary>
		string module_name;

		/// <summary>
		/// Source code line number.
		/// </summary>
		int line_number;

		/// <summary>
		/// Constructor.
		/// </summary>
		internal Breakpoint(BaseScripter scripter, string module_name, int line_number)
		{
			this.scripter = scripter;
			this.n = -1;
			this.module_name = module_name;
			this.line_number = line_number;
		}

		/// <summary>
		/// Returns P-code line number.
		/// </summary>
		internal int N
		{
			get
			{
				return n;
			}
		}

		/// <summary>
		/// Activates breakpoint.
		/// </summary>
		internal void Activate()
		{
			n = scripter.SourceLineToPCodeLine(module_name, line_number);
		}

		/// <summary>
		/// Returns module name.
		/// </summary>
		public string ModuleName
		{
			get
			{
				return module_name;
			}
		}

		/// <summary>
		/// Returns source code line number.
		/// </summary>
		public int LineNumber
		{
			get
			{
				return line_number;
			}
		}

		/// <summary>
		/// Returns 'true', if breakpoint has been activated.
		/// </summary>
		public bool Activated
		{
			get
			{
				return n > 0;
			}
		}
	}
	#endregion Breakpoint Class
}
