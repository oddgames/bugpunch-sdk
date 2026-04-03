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
	public class ScriptErrorList: IEnumerator, IEnumerable
	{
		PaxArrayList items;
		int pos = -1;

		public ScriptErrorList()
		{
			items = new PaxArrayList();
		}

		public int Add(ScriptError e)
		{
			return items.Add(e);
		}

		public void Clear()
		{
			items.Clear();
		}

		public int Count
		{
			get
			{
				return items.Count;
			}
		}

		public ScriptError this[int i]
		{
			get
			{
				return items[i] as ScriptError;
			}
		}

		// IEnumerator

		public bool MoveNext()
		{
			if (pos < items.Count - 1)
			{
				pos ++;
				return true;
			}
			else
				return false;
		}

		public void Reset()
		{
			pos = 0;
		}

		public object Current
		{
			get
			{
				return items[pos];
			}
		}

		// IEnumerable

		public IEnumerator GetEnumerator()
		{
			return (IEnumerator) this;
		}
	}
}
