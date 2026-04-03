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
	#region BreakpointList Class
	/// <summary>
	/// Represents list of breakpoints.
	/// </summary>
	public sealed class BreakpointList: IEnumerator, IEnumerable
	{
		/// <summary>
		/// Represents kernel of scripter.
		/// </summary>
		BaseScripter scripter;

		/// <summary>
		/// Collection of breakpoints.
		/// </summary>
		PaxArrayList fItems;

		/// <summary>
		/// It is necessary for implementation of IEnumerator.
		/// </summary>
		int pos = -1;

		/// <summary>
		/// Constructor.
		/// </summary>
		internal BreakpointList(BaseScripter scripter)
		{
			this.scripter = scripter;
			fItems = new PaxArrayList();
		}

		/// <summary>
		/// Removes all breakpoints from list.
		/// </summary>
		internal void Clear()
		{
			fItems.Clear();
		}

		/// <summary>
		/// Adds new breakpoint to list.
		/// </summary>
		internal void Add(Breakpoint bp)
		{
			fItems.Add(bp);
		}

		/// <summary>
		/// Activates breakpoint list.
		/// </summary>
		internal void Activate()
		{
			for (int i = 0; i < Count; i++)
				this[i].Activate();
		}

		/// <summary>
		/// Removes breakpoint from list.
		/// </summary>
		internal void Remove(Breakpoint bp)
		{
			for (int i = 0; i < Count; i++)
				if (this[i] == bp)
				{
					fItems.RemoveAt(i);
				}
		}

		/// <summary>
		/// Removes breakpoint from list.
		/// </summary>
		internal void Remove(string module_name, int line_number)
		{
			for (int i = 0; i < Count; i++)
				if (this[i].ModuleName == module_name && this[i].LineNumber == line_number)
				{
					fItems.RemoveAt(i);
				}
		}

		/// <summary>
		/// Returns 'true', if p-code line number n contains breakpoint.
		/// </summary>
		internal bool HasBreakpoint(int n)
		{
			for (int i = 0; i < Count; i++)
				if (this[i].N == n)
					return true;
			return false;
		}

		/// <summary>
		/// Returns number of breakpoints in list.
		/// </summary>
		public int Count
		{
			get
			{
				return fItems.Count;
			}
		}

		/// <summary>
		/// Returns breakpoint by index.
		/// </summary>
		public Breakpoint this[int index]
		{
			get
			{
				return (Breakpoint) fItems[index];
			}
		}

		/// <summary>
		/// Implements MoveNext of IEnumerator.
		/// </summary>
		public bool MoveNext()
		{
			if (pos < fItems.Count - 1)
			{
				pos ++;
				return true;
			}
			else
			{
				Reset();
				return false;
			}
		}

		/// <summary>
		/// Implements Reset of IEnumerator.
		/// </summary>
		public void Reset()
		{
			pos = -1;
		}

		/// <summary>
		/// Implements Current of IEnumerator.
		/// </summary>
		public object Current
		{
			get
			{
				return fItems[pos];
			}
		}

		/// <summary>
		/// Implements GetEnumerator of IEnumerable.
		/// </summary>
		public IEnumerator GetEnumerator()
		{
			return (IEnumerator) this;
		}
	}
	#endregion BreakpointList Class
}

