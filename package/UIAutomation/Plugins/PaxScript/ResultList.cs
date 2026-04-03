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
	#region ResultList Class
	/// <summary>
	/// Represents list of script results.
	/// </summary>
	public class ResultList: IEnumerator, IEnumerable
	{
		/// <summary>
		/// List of resultss.
		/// </summary>
		PaxArrayList items;

		/// <summary>
		/// scripter
		/// </summary>
		BaseScripter scripter;

		/// <summary>
		/// It is used for IEnumerator implementation.
		/// </summary>
		int pos = -1;

		/// <summary>
		/// Constructor.
		/// </summary>
		internal ResultList(BaseScripter scripter)
		{
			items = new PaxArrayList();
			this.scripter = scripter;
		}

		/// <summary>
		/// Adds new item object to list.
		/// </summary>
		internal int Add(ScriptResult e)
		{
			return items.Add(e);
		}

		/// <summary>
		/// Deletes all objects from list.
		/// </summary>
		internal void Clear()
		{
			items.Clear();
		}

		/// <summary>
		/// Returns number of objects in list.
		/// </summary>
		public int Count
		{
			get
			{
				return items.Count;
			}
		}

		/// <summary>
		/// Returns error object by index.
		/// </summary>
		public ScriptResult this[int i]
		{
			get
			{
				return items[i] as ScriptResult;
			}
		}


		// IEnumerator

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

		// IEnumerable

		/// <summary>
		/// Implements GetEnumerator of IEnumerable.
		/// </summary>
		public IEnumerator GetEnumerator()
		{
			return (IEnumerator) this;
		}
	}
	#endregion ResultList Class
}
