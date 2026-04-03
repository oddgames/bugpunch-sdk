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
	#region ScriptObject Class
	/// <summary>
	/// Base class for ObjectObject and MemberObject classes.
	/// </summary>
	internal class ScriptObject
	{
		/// <summary>
		/// Kernel of scripter.
		/// </summary>
		internal BaseScripter Scripter;

		/// <summary>
		/// Constructor.
		/// </summary>
		internal ScriptObject(BaseScripter scripter)
		{
			this.Scripter = scripter;
		}
	}
	#endregion ScriptObject Class
}
