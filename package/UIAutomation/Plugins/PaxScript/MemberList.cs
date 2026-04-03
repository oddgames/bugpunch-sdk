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
	#region MemberList Class
	/// <summary>
	/// Represents list of member definitions.
	/// </summary>
	internal class MemberList
	{
		/// <summary>
		/// List of MemberObject objects.
		/// </summary>
		PaxArrayList items;

		BaseScripter scripter;

		/// <summary>
		/// Constructor.
		/// </summary>
		public MemberList(BaseScripter scripter)
		{
			this.scripter = scripter;
			items = new PaxArrayList();
		}

		/// <summary>
		/// Number of members in list.
		/// </summary>
		public int Count
		{
			get
			{
				return items.Count;
			}
		}

		/// <summary>
		/// Adds new member to list.
		/// </summary>
		public int Add(MemberObject m)
		{
			items.Add(m);
			return Count;
		}

		/// <summary>
		/// Remove member.
		/// </summary>
		public void Delete(int i)
		{
			items.RemoveAt(i);
		}

		public void ResetCompileStage()
		{
			for (int i = Count - 1; i >= 0; i--)
			{
				if (this[i].Id > scripter.symbol_table.RESET_COMPILE_STAGE_CARD)
				{
					Delete(i);
				}
			}
		}

		/// <summary>
		/// Returns member by index.
		/// </summary>
		public MemberObject this[int i]
		{
			get
			{
				return (MemberObject) items[i];
			}
		}
	}
	#endregion MemberList Class
}
