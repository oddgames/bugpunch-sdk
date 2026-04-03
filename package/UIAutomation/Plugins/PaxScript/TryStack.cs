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
	#region TryStack Class
	/// <summary>
	/// This class is used to process try-exept, try-finally statements at
	/// run-time.
	/// </summary>
	internal sealed class TryStack
	{
		/// <summary>
		/// Represents a record of try stack.
		/// </summary>
		class TryStackRec
		{
			public int Bound1;
			public int Bound2;
		}

		/// <summary>
		/// List of records.
		/// </summary>
		PaxArrayList fItems;

		/// <summary>
		/// Constructor.
		/// </summary>
		public TryStack()
		{
			fItems = new PaxArrayList();
		}

		/// <summary>
		/// Creates new record and pushes it into stack.
		/// </summary>
		public void Push(int b1, int b2)
		{
			TryStackRec r = new TryStackRec();
			r.Bound1 = b1;
			r.Bound2 = b2;
			fItems.Add(r);
		}

		/// <summary>
		/// Pops topmost record from stack.
		/// </summary>
		public void Pop()
		{
			fItems.RemoveAt(Count - 1);
		}

		/// <summary>
		/// Deletes all records.
		/// </summary>
		public void Clear()
		{
			fItems.Clear();
		}

		/// <summary>
		/// Returns 'true', if p-code line n belongs to interval
		/// (Bound1. Bound2) of topmost record of stack.
		/// </summary>
		public bool Legal(int n)
		{
			TryStackRec r = (TryStackRec) fItems[Count - 1];
			return (n >= r.Bound1) || (n <= r.Bound2);
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
	#endregion TryStack Class
}
