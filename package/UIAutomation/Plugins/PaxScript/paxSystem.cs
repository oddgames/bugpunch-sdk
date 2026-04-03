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
	public class PaxScriptForbid: Attribute
	{
	}

	#region Upcase Enum
	/// <summary>
	///
	/// </summary>
	public enum Upcase
	{
		None,
		Yes,
		No,
	}
	#endregion Upcase Enum

	#region PaxLanguage Enum
	/// <summary>
	/// Languages supported by paxScript.NET
	/// </summary>
	public enum PaxLanguage
	{
		CSharp,
		VB,
		Pascal,
	}
	#endregion Language Enum

	#region MemberKind Enum
	/// <summary>
	/// Kind of a class member.
	/// </summary>
	public enum MemberKind
	{
		None,
		Type,
		Var,
		Ref,
		Const,
		Method,
		Constructor,
		Destructor,
		Field,
		Property,
		Event,
		Index,
		Label,
		Alias
	}
	#endregion MemberKind Enum

	#region ParamMod Enum
	/// <summary>
	/// Modifier of formal parameter.
	/// </summary>
	public enum ParamMod
	{
		None,
		RetVal,
		Out
	}
	#endregion ParamMod Enum

	#region ClassKind Enum
	/// <summary>
	/// Kind of type.
	/// </summary>
	public enum ClassKind
	{
		Namespace,
		Class,
		Struct,
		Interface,
		Enum,
		Array,
		Delegate,
		Subrange
	}
	#endregion ClassKind Enum

	#region StandardType Enum
	/// <summary>
	/// Standard types.
	/// </summary>
	public enum StandardType: int
	{
		None,
		Void,
		Bool,
		Byte,
		Char,
		Decimal,
		Double,
		Float,
		Int,
		Long,
		Sbyte,
		Short,
		String,
		Uint,
		Ulong,
		Ushort,
		Object
	}
	#endregion StandardType Enum

	public class Types
	{
		PaxHashTable ht;

		public Types()
		{
			ht = new PaxHashTable();
		}

		public void Add(string v1, StandardType v2)
		{
			ht.Add(v1, v2);
		}

		public int GetTypeId(string type_name)
		{
			object v = ht[type_name];
			if (v == null)
				return -1;
			else
				return (int) v;
		}
	}

	#region RegisteredType Class
	/// <summary>
	/// Registered type.
	/// </summary>
	public class RegisteredType
	{
		/// <summary>
		/// Type info.
		/// </summary>
		public Type T;

		/// <summary>
		/// Type id.
		/// </summary>
		public int Id;
	}
	#endregion RegisteredType Class

	#region RegisteredTypeList Class
	/// <summary>
	/// Registered types.
	/// </summary>
	public class RegisteredTypeList
	{
		/// <summary>
		/// List of type records.
		/// </summary>
		StringList Items;
		PaxHashTable ht = new PaxHashTable();

		/// <summary>
		/// Constructor.
		/// </summary>
		public RegisteredTypeList()
		{
			Items = new StringList(false);
		}

		/// <summary>
		/// Deletes all records from list.
		/// </summary>
		public void Clear()
		{
			Items.Clear();
			ht.Clear();
		}

		/// <summary>
		/// Deletes record from list.
		/// </summary>
		public void Delete(int i)
		{
			RegisteredType rt = this[i];
			ht.Remove(rt.T);
			Items.Delete(i);

		}

		/// <summary>
		/// Creates copy of object.
		/// </summary>
		public RegisteredTypeList Clone()
		{
			RegisteredTypeList result = new RegisteredTypeList();
			for (int i = 0; i < Count; i++)
				result.RegisterType(this[i].T, this[i].Id);
			return result;
		}

		/// <summary>
		/// Adds new type record to list.
		/// </summary>
		public void RegisterType(Type t, int type_id)
		{
			RegisteredType r = new RegisteredType();
			r.T = t;
			r.Id = type_id;
			Items.AddObject(t.FullName, r);

			ht.Add(t, type_id);
		}

		/// <summary>
		/// Returns id of type by type info.
		/// </summary>
		public int FindRegisteredTypeId(Type t)
		{
			object v = ht[t];
			if (v == null)
				return 0;
			else
				return (int) v;
		}

		/// <summary>
		/// Returns number of type records in list.
		/// </summary>
		public int Count
		{
			get
			{
				return Items.Count;
			}
		}

		/// <summary>
		/// Returns type record by index.
		/// </summary>

		public RegisteredType this[int i]
		{
			get
			{
				return (RegisteredType) Items.Objects[i];
			}
		}
	}
	#endregion RegisteredTypeList Class

	#region StandardTypeList Class
	/// <summary>
	/// List of standard types.
	/// </summary>
	public class StandardTypeList
	{
		/// <summary>
		/// List of items.
		/// </summary>
		public StringList Items;

		/// <summary>
		/// Constructor.
		/// </summary>
		public StandardTypeList()
		{
			Items = new StringList(false);
			Items.AddObject("Void", typeof(void));
			Items.AddObject("Boolean", typeof(Boolean));
			Items.AddObject("Byte", typeof(Byte));
			Items.AddObject("Char", typeof(Char));
			Items.AddObject("Decimal", typeof(Decimal));
			Items.AddObject("Double", typeof(Double));
			Items.AddObject("Single", typeof(Single));
			Items.AddObject("Int32", typeof(Int32));
			Items.AddObject("Int64", typeof(Int64));
			Items.AddObject("SByte", typeof(SByte));
			Items.AddObject("Int16", typeof(Int16));
			Items.AddObject("String", typeof(String));
			Items.AddObject("UInt32", typeof(UInt32));
			Items.AddObject("UInt64", typeof(UInt64));
			Items.AddObject("UInt16", typeof(UInt16));
			Items.AddObject("Object", typeof(Object));
		}

		/// <summary>
		/// Returns index of record by type name.
		/// </summary>
		public int IndexOf(string s)
		{
			return Items.IndexOf(s);
		}

		/// <summary>
		/// Returns number of records.
		/// </summary>
		public int Count
		{
			get
			{
				return Items.Count;
			}
		}

		/// <summary>
		/// Returns record by index.
		/// </summary>
		public string this[int i]
		{
			get
			{
				return Items[i];
			}
		}
	}
	#endregion StandardTypeList Class

	#region RunMode Enum
	/// <summary>
	/// Determines mode of script running.
	/// </summary>
	public enum RunMode
	{
		/// <summary>
		/// Run program.
		/// </summary>
		Run,

		/// <summary>
		/// Execute a program one line at a time, tracing into procedures
		/// and following the execution of each line.
		/// </summary>
		TraceInto,

		/// <summary>
		/// Execute a program one line at a time, stepping over procedures
		/// while executing them as a single unit.
		/// </summary>
		StepOver,

		/// <summary>
		/// Use this command to stop on the next source line in your
		/// application, regardless of the control flow.
		/// </summary>
		NextLine,

		/// <summary>
		/// Run the loaded program until execution returns from the current
		/// function.
		/// </summary>
		UntilReturn
	}
	#endregion RunMode Enum

	#region PaxSystem Class
	/// <summary>
	/// Common use routines.
	/// </summary>
	public class PaxSystem
	{
		/// <summary>
		/// Adjustes string.
		/// </summary>
		public static string Norm(object s, int l)
		{
			string result = s.ToString();
			while (result.Length < l) result = " " + result;
			if (result.Length > l)
				result = result.Substring(0, l);
			return result;
		}

		/// <summary>
		/// Returns position of char c in string s.
		/// </summary>
		public static int PosCh(char c, string s)
		{
			for (int i = 0; i < s.Length; i++)
				if (s[i] == c) return i;
			return -1;
		}

		/// <summary>
		/// Extractes namespace name from full type name or namespace name.
		/// </summary>
		public static string ExtractOwner(string s, out char c)
		{
            c = '.';

            if (s == null)
            {
                return "";
            }

			for (int i = s.Length - 1; i >= 0; i--)
				if (s[i] == '.' || s[i] == '+')
				{
					c = s[i];
					return s.Substring(0, i);
				}
			return "";
		}

		/// <summary>
		/// Extractes name from full name.
		/// </summary>
		public static string ExtractName(string s)
		{
			for (int i = s.Length - 1; i >= 0; i--)
				if (s[i] == '.')
					return s.Substring(i + 1);
			return s;
		}

		/// <summary>
		/// Extractes path from file name.
		/// </summary>
		public static string ExtractPath(string s)
		{
			for (int i = s.Length - 1; i >= 0; i--)
				if (s[i] == '\\')
					return s.Substring(0, i + 1);
			return "";
		}

		/// <summary>
		/// Extractes prefix from full type name.
		/// </summary>
		public static string ExtractPrefixName(string s, out int p)
		{
			for (int i = 0;  i < s.Length - 1; i++)
			{
				char c = s[i];
				if (c == '[')
				{
					p = i;
					return s.Substring(0, i);
				}
			}
			p = -1;
			return s;
		}

		/// <summary>
		/// Returns element type name from array type name.
		/// </summary>
		public static string GetElementTypeName(string array_type_name)
		{
			if (array_type_name == "String")
			{
				return "Char";
			}

			int i = PaxSystem.PosCh('[', array_type_name);
			int j = PaxSystem.PosCh(']', array_type_name);
			if ((i == -1) || (j == -1))
				return "";
			string s = array_type_name.Substring(0, i);
			if (j < array_type_name.Length - 1)
				s += array_type_name.Substring(j+1);
			return s;
		}

		/// <summary>
		/// Returns rank of array.
		/// </summary>
		public static int GetRank(string array_type_name)
		{
			if (PosCh('[', array_type_name) == -1)
				return 0;

			bool b = false;
			int result = 1;
			for (int i = 0; i < array_type_name.Length; i++)
			{
				char c = array_type_name[i];
				if (c == '[')
					b = true;
				else if (c == ']')
					break;
				if ((c == ',') && b)
					result ++;
			}
			return result;
		}

		public static bool CompareStrings(string s1, string s2, bool upcase)
		{
			if (upcase)
				return s1.ToUpper() == s2.ToUpper();
			else
				return s1 == s2;
		}

		public static bool StrEql(string s1, string s2)
		{
			return CompareStrings(s1, s2, true);
		}
	}
	#endregion PaxSystem Class
}

