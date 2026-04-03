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

	#region ExitKind Enum
	/// <summary>
	/// Kind of exiit statement
	/// </summary>
	public enum ExitKind
	{
		None,
		Do,
		For,
		While,
		Select,
		Sub,
		Function,
		Property,
		Try,
	}
	#endregion ExitKind Enum

	#region EntryStack Class
	/// <summary>
	/// Allows to process Break and Continue statements.
	/// </summary>
	internal class EntryStack
	{
		#region EntryRec Class
		/// <summary>
		/// Represents record of entry stack.
		/// </summary>
		class EntryRec
		{
			public int Label;
			public string StringLabel;
			public ExitKind EK = ExitKind.None;
		}
		#endregion EntryRec Class

		/// <summary>
		/// Stack records.
		/// </summary>
		PaxArrayList fItems;

		/// <summary>
		/// Constructor.
		/// </summary>
		public EntryStack()
		{
			fItems = new PaxArrayList();
		}

		/// <summary>
		/// Push label into stack.
		/// </summary>
		public void Push(int label)
		{
			EntryRec r = new EntryRec();
			r.Label = label;
			r.StringLabel = "";
			fItems.Add(r);
		}

		/// <summary>
		/// Push label into stack.
		/// </summary>
		public void Push(int label, ExitKind ek)
		{
			EntryRec r = new EntryRec();
			r.Label = label;
			r.StringLabel = "";
            r.EK = ek;
			fItems.Add(r);
		}

		/// <summary>
		/// Push label into stack.
		/// </summary>
		public void Push(int label, ref string string_label)
		{
			EntryRec r = new EntryRec();
			r.Label = label;
			r.StringLabel = string_label;
			fItems.Add(r);
			string_label = "";
		}

		/// <summary>
		/// Pops topmost record from stack.
		/// </summary>
		public void Pop()
		{
			fItems.RemoveAt(Count - 1);
		}

		/// <summary>
		/// Delete all records.
		/// </summary>
		public void Clear()
		{
			fItems.Clear();
		}

		/// <summary>
		/// Returns topmost label.
		/// </summary>
		public int TopLabel()
		{
			EntryRec r = (EntryRec) fItems[Count - 1];
			return r.Label;
		}

		/// <summary>
		/// Returns topmost label.
		/// </summary>
		public int TopLabel(string string_label)
		{
			for (int i = Count - 1; i >= 0; i--)
			{
				EntryRec r = (EntryRec) fItems[i];
				if (r.StringLabel == string_label)
					return r.Label;
			}
			return 0;
		}

		/// <summary>
		/// Returns topmost label.
		/// </summary>
		public int TopLabel(ExitKind ek)
		{
			for (int i = Count - 1; i >= 0; i--)
			{
				EntryRec r = (EntryRec) fItems[i];
				if (r.EK == ek)
					return r.Label;
			}
			return 0;
		}

		/// <summary>
		/// Returns number of records in stack.
		/// </summary>
		public int Count
		{
			get
			{
				return fItems.Count;
			}
		}
	}
	#endregion EntryStack Class
}
