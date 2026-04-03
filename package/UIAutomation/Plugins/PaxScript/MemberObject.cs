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
	#region MemberObject Class
	/// <summary>
	/// Base class for a member definition.
	/// </summary>
    internal class MemberObject: ScriptObject
	{
		/// <summary>
		/// List of member modifiers.
		/// </summary>
		public ModifierList Modifiers;

		/// <summary>
		/// List of members.
		/// </summary>
		public MemberList Members;

		/// <summary>
		/// Kind of member.
		/// </summary>
		public MemberKind Kind;

		/// <summary>
		/// Name index of member.
		/// </summary>
		public int NameIndex;

		/// <summary>
		/// Id of member.
		/// </summary>
		public int Id;

		/// <summary>
		/// Id of owner of member.
		/// </summary>
		public int OwnerId;

		/// <summary>
		/// Id of member implemented by the given member.
		/// </summary>
		public int ImplementsId = 0;

		/// <summary>
		/// If it is 'true', the member represents host-defined member.
		/// </summary>
		public bool Imported;

		/// <summary>
		/// Undocumented.
		/// </summary>
		public int PCodeLine = 0;

		/// <summary>
		/// Constructor.
		/// </summary>
		internal MemberObject(BaseScripter scripter, int id, int owner_id): base(scripter)
		{
			Modifiers = new ModifierList();
			Members = new MemberList(scripter);
			Id = id;
			OwnerId = owner_id;
			Kind = scripter.symbol_table[Id].Kind;
			NameIndex = scripter.symbol_table[Id].NameIndex;
			Imported = false;
		}

		/// <summary>
		/// Adds new member to member list.
		/// </summary>
		public void AddMember(MemberObject m)
		{
			Members.Add(m);
		}

		/// <summary>
		/// Returns member object by id.
		/// </summary>
		public MemberObject GetMember(int id)
		{
			for (int i=0; i < Members.Count; i++)
			{
				MemberObject m = Members[i];
				if (m.Id == id)
					return m;
			}
			return null;
		}

		/// <summary>
		/// Returns member object by name index.
		/// </summary>
		internal virtual MemberObject GetMemberByNameIndex(int name_index, bool upcase)
		{
			for (int i = 0; i < Members.Count; i++)
			{
				MemberObject m = Members[i];
				if (m.NameIndex == name_index)
					return m;
			}

			if (!upcase)
				return null;

			string upcase_name = Scripter.GetUpcaseNameByNameIndex(name_index);

			for (int i = 0; i < Members.Count; i++)
			{
				MemberObject m = Members[i];
				if (m.UpcaseName == upcase_name)
					return m;
			}
			return null;
		}

		/// <summary>
		/// Returns instance member object by name index.
		/// </summary>
		public virtual MemberObject GetInstanceMemberByNameIndex(int name_index, bool upcase)
		{
			for (int i = 0; i < Members.Count; i++)
			{
				MemberObject m = Members[i];
				if (m.NameIndex == name_index && !m.HasModifier(Modifier.Static))
					return m;
			}
			if (!upcase)
				return null;

			string upcase_name = Scripter.GetUpcaseNameByNameIndex(name_index);
			for (int i = 0; i < Members.Count; i++)
			{
				MemberObject m = Members[i];
				if (!m.HasModifier(Modifier.Static) && m.UpcaseName == upcase_name)
					return m;
			}
			return null;
		}

		/// <summary>
		/// Returns static member object by name index.
		/// </summary>
		public virtual MemberObject GetStaticMemberByNameIndex(int name_index, bool upcase)
		{
			for (int i = 0; i < Members.Count; i++)
			{
				MemberObject m = Members[i];
				if (m.NameIndex == name_index && m.HasModifier(Modifier.Static))
					return m;
			}
			if (!upcase)
				return null;

			string upcase_name = Scripter.GetUpcaseNameByNameIndex(name_index);
			for (int i = 0; i < Members.Count; i++)
			{
				MemberObject m = Members[i];
				if (m.HasModifier(Modifier.Static) && m.UpcaseName == upcase_name)
					return m;
			}
			return null;
		}

		/// <summary>
		/// Adds modifier to modifier list.
		/// </summary>
		public void AddModifier(Modifier modifier)
		{
			Modifiers.Add(modifier);
		}

		/// <summary>
		/// Returns 'true', if modifier list constans modifier m.
		/// </summary>
		public bool HasModifier(Modifier m)
		{
			return Modifiers.HasModifier(m);
		}

		/// <summary>
		/// Compares accessibility of 2 members.
		/// </summary>
		public static int CompareAccessibility(MemberObject m1, MemberObject m2)
		{
			if (PaxSystem.PosCh('[', m1.Name) >= 0)
				return 0;

			if (PaxSystem.PosCh('[', m2.Name) >= 0)
				return 0;

			if (m1.HasModifier(Modifier.Public))
			{
				if (m2.HasModifier(Modifier.Public) || m2.HasModifier(Modifier.Friend))
					return 0;
				else
					return 1;
			}
			else if (m1.HasModifier(Modifier.Friend))
			{
				if (m2.HasModifier(Modifier.Public) || m2.HasModifier(Modifier.Friend))
					return 0;
				else
					return 1;
			}
			else if (m1.HasModifier(Modifier.Protected))
			{
				if (m2.HasModifier(Modifier.Public))
					return -1;
				else if (m2.HasModifier(Modifier.Protected))
					return 0;
				else
					return 1;
			}
			else // m1 is private
			{
				if (m2.HasModifier(Modifier.Public))
					return -1;
				else if (m2.HasModifier(Modifier.Protected))
					return -1;
				else
					return 0;
			}
		}

		/// <summary>
		/// Returns 'true', if member is public.
		/// </summary>
		public bool Public
		{
			get
			{
				return HasModifier(Modifier.Public);
			}
		}

		/// <summary>
		/// Returns 'true', if member is protected.
		/// </summary>
		public bool Protected
		{
			get
			{
				return HasModifier(Modifier.Protected);
			}
		}

		/// <summary>
		/// Returns 'true', if member is private.
		/// </summary>
		public bool Private
		{
			get
			{
				return !HasModifier(Modifier.Protected) &&
					   !HasModifier(Modifier.Public) &&
					   !HasModifier(Modifier.Friend);
			}
		}

		/// <summary>
		/// Returns name of member.
		/// </summary>
		public string Name
		{
			get
			{
				return Scripter.symbol_table[Id].Name;
			}
		}

		/// <summary>
		/// Returns uppercase name of member.
		/// </summary>
		public string UpcaseName
		{
			get
			{
				return Scripter.GetUpcaseNameByNameIndex(NameIndex);
			}
		}

		/// <summary>
		/// Returns full name of member.
		/// </summary>
		public string FullName
		{
			get
			{
				return Scripter.symbol_table[Id].FullName;
			}
		}

		/// <summary>
		/// Returns 'true', if member is static.
		/// </summary>
		public bool Static
		{
			get
			{
				return HasModifier(Modifier.Static);
			}
		}

		/// <summary>
		/// Returns 'true', if member represents method, constructor or
		/// destructor.
		/// </summary>
		public bool IsSub
		{
			get
			{
				return (Kind == MemberKind.Method) ||
					   (Kind == MemberKind.Constructor) ||
					   (Kind == MemberKind.Destructor);
			}
		}
	}
	#endregion MemberObject Class
}
