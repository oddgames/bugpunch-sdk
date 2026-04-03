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
using System.IO;
using System.Collections;

namespace PaxScript.Net
{
	#region ModuleList Class
	/// <summary>
	/// Represents script as list of modules.
	/// </summary>
	public class ModuleList: IEnumerator, IEnumerable
	{
		/// <summary>
		/// Kernel of scripter.
		/// </summary>
		BaseScripter scripter;

		/// <summary>
		/// List of modules.
		/// </summary>
		StringList items;

		/// <summary>
		/// It is necessary for IEnumerator implementation.
		/// </summary>
		int pos = -1;

		/// <summary>
		/// Constructor.
		/// </summary>
		internal ModuleList(BaseScripter scripter)
		{
			this.scripter = scripter;
			items = new StringList(false);
		}

		/// <summary>
		/// Deletes all modules.
		/// </summary>
		internal void Clear()
		{
			items.Clear();
		}

		/// <summary>
		/// Adds new module to module list.
		/// </summary>
		internal void Add(Module m)
		{
			items.AddObject(m.Name, m);
		}

		/// <summary>
		/// Returns module by module name index.
		/// </summary>
		internal Module GetModule(int name_index)
		{
			for (int i = 0; i < Count; i++)
			{
				Module m = (Module) items.Objects[i];
				if (m.NameIndex == name_index)
					return m;
			}
			return null;
		}

		/// <summary>
		/// Returns number of modules in list.
		/// </summary>
		public int Count
		{
			get
			{
				return items.Count;
			}
		}

		/// <summary>
		/// Returns index of module.
		/// </summary>
		public int IndexOf(string module_name)
		{
			return items.IndexOf(module_name);
		}

		/// <summary>
		/// Returns module by index.
		/// </summary>
		public Module this[int index]
		{
			get
			{
				return (Module) items.Objects[index];
			}
		}

		/// <summary>
		/// Returns module by module name.
		/// </summary>
		public Module this[string module_name]
		{
			get
			{
				int index = IndexOf(module_name);
				return (Module) items.Objects[index];
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
	#endregion ModuleList Class
}
