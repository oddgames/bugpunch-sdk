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

namespace PaxScript.Net
{
	#region Modifier Enum
	/// <summary>
	/// Enumerates all possible modifiers.
	/// </summary>
	public enum Modifier {New, Public, Protected, Internal, Private,
					  Abstract, Sealed, Static, ReadOnly, Volatile,
					  Override, Virtual, Extern,

					  // VB.NET modifiers
					  Overloads, Friend, Default, WriteOnly,
					  Shadows, WithEvents,

					  None};

	#endregion Modifier Enum


	#region ModifierList Class
	/// <summary>
	/// Represents list of modifiers of a member.
	/// </summary>
	internal class ModifierList
	{
		/// <summary>
		/// List of modifiers.
		/// </summary>
		Modifier[] items;

		/// <summary>
		/// Number of modifiers in list.
		/// </summary>
		int count;

		/// <summary>
		/// Constructor.
		/// </summary>
		public ModifierList()
		{
			items = new Modifier[(int) Modifier.None];
			count = 0;
		}

		/// <summary>
		/// Adds modifier.
		/// </summary>
		public void Add(Modifier m)
		{
			if (HasModifier(m))
				return;
				
			items[count] = m;
			++ count;
		}

		/// <summary>
		/// Returns 'true', if list has modifier m.
		/// </summary>
		public bool HasModifier(Modifier m)
		{
			for (int i = 0; i < count; i++)
			{
				if (items[i] == m)
					return true;
			}
			return false;
		}

		/// <summary>
		/// Deletes modifier from list.
		/// </summary>
		public void Delete(Modifier m)
		{
			int j = -1;
			
			for (int i = 0; i < count; i++)
			{
				if (items[i] == m)
				{
					j = i;
					break;
				}
			}

			for (int i = count - 2; i >= j; i--)
				items[i] = items[i + 1];
			-- count;
		}

		/// <summary>
		/// Creates a copy of modifier list.
		/// </summary>
		public ModifierList Clone()
		{
			ModifierList result = new ModifierList();
			for (int i = 0; i < Count; i++)
				result.Add(this[i]);
			return result;
		}

		/// <summary>
		/// Returns number of modifiers in list.
		/// </summary>
		public int Count
		{
			get
			{
				return count;
			}
		}

		/// <summary>
		/// Returns modifier by index.
		/// </summary>
		public Modifier this[int index]
		{
			get
			{
				return items[index];
			}
			set
			{
				items[index] = value;
			}
		}
	}
	#endregion ModifierList Class
}
