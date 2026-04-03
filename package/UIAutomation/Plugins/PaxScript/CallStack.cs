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
	#region CallStack Class
	/// <summary>
	/// Represents call stack.
	/// </summary>
	public sealed class CallStack: IEnumerator, IEnumerable
	{
		/// <summary>
		/// Represents kernel of scripter.
		/// </summary>
		BaseScripter scripter;

		/// <summary>
		/// Collection of call stack records.
		/// </summary>
		PaxArrayList items;

		/// <summary>
		/// It is necessary for implementation of IEnumerator.
		/// </summary>
		int pos = -1;

		/// <summary>
		/// Constructor.
		/// </summary>
		internal CallStack(BaseScripter scripter)
		{
			this.scripter = scripter;
			items = new PaxArrayList();
		}

		/// <summary>
		/// Removes all records from call stack.
		/// </summary>
		internal void Clear()
		{
			items.Clear();
		}

		/// <summary>
		/// Adds new record to call stack.
		/// </summary>
		internal void Add(CallStackRec csr)
		{
			items.Insert(0, csr);
		}

		/// <summary>
		/// Pops record from call stack.
		/// </summary>
		internal void Pop()
		{
			items.RemoveAt(Count - 1);
		}

		/// <summary>
		/// Returns id of method at the top of call stack.
		/// </summary>
		internal int CurrSubId
		{
			get
			{
				if (Count == 0)
					return -1;
				else
					return this[0].SubId;
			}
		}

		/// <summary>
		/// Returns 'true', if sub_id is id of method at the top of call stack.
		/// </summary>
		internal bool HasSubId(int sub_id)
		{
			for (int i = 0; i < Count; i++)
				if (this[i].SubId == sub_id)
					return true;
			return false;
		}

		/// <summary>
		/// Returns number of records in the call stack.
		/// </summary>
		public int Count
		{
			get
			{
				return items.Count;
			}
		}

		/// <summary>
		/// Returns record by index.
		/// </summary>
		public CallStackRec this[int index]
		{
			get
			{
				return items[index] as CallStackRec;
			}
		}

		/// <summary>
		/// Implements MoveNext of IEnumerator.
		/// </summary>
		public bool MoveNext()
		{
			if (pos < items.Count - 1)
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
				return items[pos];
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
	#endregion CallStack Class
}

