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
using System.Collections;
using SL;

namespace PaxScript.Net
{
	#region CallStackRec Class
	/// <summary>
	/// Represents the call stack record.
	/// </summary>
	public sealed class CallStackRec
	{
		/// <summary>
		/// Represents kernel of scripter.
		/// </summary>
		BaseScripter scripter;

		/// <summary>
		/// List of actual parameters.
		/// </summary>
		PaxArrayList p;

		/// <summary>
		/// P-code line number.
		/// </summary>
		int n;

		/// <summary>
		/// Represents method.
		/// </summary>
		FunctionObject f;

		/// <summary>
		/// Constructor.
		/// </summary>
		internal CallStackRec(BaseScripter scripter, FunctionObject f, int n)
		{
			p = new PaxArrayList();
			this.scripter = scripter;
			this.n = n;
			this.f = f;
		}

		/// <summary>
		/// Id of method.
		/// </summary>
		internal int SubId
		{
			get
			{
				return f.Id;
			}
		}

		/// <summary>
		/// Returns p-code line number.
		/// </summary>
		internal int N
		{
			get
			{
				return n;
			}
		}

		/// <summary>
		/// Returns list of parameters.
		/// </summary>
		public PaxArrayList Parameters
		{
			get
			{
				return p;
			}
		}

		/// <summary>
		/// Returns name of module.
		/// </summary>
		public string ModuleName
		{
			get
			{
				Module m = scripter.GetModule(n);
				if (m == null)
					return "";
				else
					return m.Name;
			}
		}

		/// <summary>
		/// Returns source line number.
		/// </summary>
		public int LineNumber
		{
			get
			{
				return scripter.GetLineNumber(n);
			}
		}

		/// <summary>
		/// Returns name of method.
		/// </summary>
		public string Name
		{
			get
			{
				return f.Name;
			}
		}

		/// <summary>
		/// Returns full name of method.
		/// </summary>
		public string FullName
		{
			get
			{
				return f.FullName;
			}
		}

		/// <summary>
		/// Returns view of method call.
		/// </summary>
		public string CallView
		{
			get
			{
				string s = Name + "(";
				for (int i = 0; i < Parameters.Count; i++)
				{
					s += Parameters[i].ToString();
					if (i < Parameters.Count - 1)
						s += ",";
				}
				s += ")";
				return s;
			}
		}
	}
	#endregion CallStackRec Class
}
