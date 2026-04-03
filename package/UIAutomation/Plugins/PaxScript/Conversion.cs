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
using System.IO;

namespace PaxScript.Net
{
	#region Conversion Class
	/// <summary>
	/// Contains conversion methods.
	/// </summary>
	internal sealed class Conversion
	{
		/// <summary>
		/// Represents implicit conversions.
		/// </summary>
		AssocIntegers implicit_numeric_conversions;
		/// <summary>
		/// Creates list of implicit conversions.
		/// </summary>
		public Conversion()
		{
			implicit_numeric_conversions = new AssocIntegers(100);
			implicit_numeric_conversions.Add((int)StandardType.Sbyte, (int)StandardType.Short);
			implicit_numeric_conversions.Add((int)StandardType.Sbyte, (int)StandardType.Int);
			implicit_numeric_conversions.Add((int)StandardType.Sbyte, (int)StandardType.Long);
			implicit_numeric_conversions.Add((int)StandardType.Sbyte, (int)StandardType.Float);
			implicit_numeric_conversions.Add((int)StandardType.Sbyte, (int)StandardType.Double);
			implicit_numeric_conversions.Add((int)StandardType.Sbyte, (int)StandardType.Decimal);

			implicit_numeric_conversions.Add((int)StandardType.Byte, (int)StandardType.Short);
			implicit_numeric_conversions.Add((int)StandardType.Byte, (int)StandardType.Ushort);
			implicit_numeric_conversions.Add((int)StandardType.Byte, (int)StandardType.Int);
			implicit_numeric_conversions.Add((int)StandardType.Byte, (int)StandardType.Uint);
			implicit_numeric_conversions.Add((int)StandardType.Byte, (int)StandardType.Long);
			implicit_numeric_conversions.Add((int)StandardType.Byte, (int)StandardType.Ulong);
			implicit_numeric_conversions.Add((int)StandardType.Byte, (int)StandardType.Float);
			implicit_numeric_conversions.Add((int)StandardType.Byte, (int)StandardType.Double);
			implicit_numeric_conversions.Add((int)StandardType.Byte, (int)StandardType.Decimal);

			implicit_numeric_conversions.Add((int)StandardType.Short, (int)StandardType.Int);
			implicit_numeric_conversions.Add((int)StandardType.Short, (int)StandardType.Long);
			implicit_numeric_conversions.Add((int)StandardType.Short, (int)StandardType.Float);
			implicit_numeric_conversions.Add((int)StandardType.Short, (int)StandardType.Double);
			implicit_numeric_conversions.Add((int)StandardType.Short, (int)StandardType.Decimal);

			implicit_numeric_conversions.Add((int)StandardType.Ushort, (int)StandardType.Int);
			implicit_numeric_conversions.Add((int)StandardType.Ushort, (int)StandardType.Uint);
			implicit_numeric_conversions.Add((int)StandardType.Ushort, (int)StandardType.Long);
			implicit_numeric_conversions.Add((int)StandardType.Ushort, (int)StandardType.Ulong);
			implicit_numeric_conversions.Add((int)StandardType.Ushort, (int)StandardType.Float);
			implicit_numeric_conversions.Add((int)StandardType.Ushort, (int)StandardType.Double);
			implicit_numeric_conversions.Add((int)StandardType.Ushort, (int)StandardType.Decimal);

			implicit_numeric_conversions.Add((int)StandardType.Int, (int)StandardType.Long);
			implicit_numeric_conversions.Add((int)StandardType.Int, (int)StandardType.Float);
			implicit_numeric_conversions.Add((int)StandardType.Int, (int)StandardType.Double);
			implicit_numeric_conversions.Add((int)StandardType.Int, (int)StandardType.Decimal);

			implicit_numeric_conversions.Add((int)StandardType.Uint, (int)StandardType.Long);
			implicit_numeric_conversions.Add((int)StandardType.Uint, (int)StandardType.Ulong);
			implicit_numeric_conversions.Add((int)StandardType.Uint, (int)StandardType.Float);
			implicit_numeric_conversions.Add((int)StandardType.Uint, (int)StandardType.Double);
			implicit_numeric_conversions.Add((int)StandardType.Uint, (int)StandardType.Decimal);

			implicit_numeric_conversions.Add((int)StandardType.Long, (int)StandardType.Float);
			implicit_numeric_conversions.Add((int)StandardType.Long, (int)StandardType.Double);
			implicit_numeric_conversions.Add((int)StandardType.Long, (int)StandardType.Decimal);

			implicit_numeric_conversions.Add((int)StandardType.Ulong, (int)StandardType.Float);
			implicit_numeric_conversions.Add((int)StandardType.Ulong, (int)StandardType.Double);
			implicit_numeric_conversions.Add((int)StandardType.Ulong, (int)StandardType.Decimal);

			implicit_numeric_conversions.Add((int)StandardType.Char, (int)StandardType.Ushort);
			implicit_numeric_conversions.Add((int)StandardType.Char, (int)StandardType.Int);
			implicit_numeric_conversions.Add((int)StandardType.Char, (int)StandardType.Uint);
			implicit_numeric_conversions.Add((int)StandardType.Char, (int)StandardType.Long);
			implicit_numeric_conversions.Add((int)StandardType.Char, (int)StandardType.Ulong);
			implicit_numeric_conversions.Add((int)StandardType.Char, (int)StandardType.Float);
			implicit_numeric_conversions.Add((int)StandardType.Char, (int)StandardType.Double);
			implicit_numeric_conversions.Add((int)StandardType.Char, (int)StandardType.Decimal);

			implicit_numeric_conversions.Add((int)StandardType.Float, (int)StandardType.Double);
		}

		/// <summary>
		/// Returns 'true', if there is an implicit numeric conversion between
		/// types [type_id1] and [type_id2].
		/// </summary>
		public bool ExistsImplicitNumericConversion(int type_id1, int type_id2)
		{
			for(int i = 0; i < implicit_numeric_conversions.Count; i++)
			{
				int t1 = implicit_numeric_conversions.Items1[i];
				int t2 = implicit_numeric_conversions.Items2[i];
				if ((t1 == type_id1) && (t2 == type_id2))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Returns 'true', if there is an implicit enumeration conversion between
		/// types of id1 and id2.
		/// </summary>
		public bool ExistsImplicitEnumerationConversion(BaseScripter scripter, int id1, int id2)
		{
			int type_id1 = scripter.symbol_table[id1].TypeId;
			ClassObject c1 = scripter.GetClassObject(type_id1);
			string s = scripter.symbol_table[id2].Name;
			return c1.IsEnum && (s == "0");
		}

		/// <summary>
		/// Returns 'true', if there is an implicit reference conversion between
		/// types c1 and c2.
		/// </summary>
		public bool ExistsImplicitReferenceConversion(ClassObject c1, ClassObject c2)
		{
			BaseScripter scripter = c1.Scripter;
			if (c1.IsReferenceType && (c2.Id == scripter.symbol_table.OBJECT_CLASS_id))
				return true;
			if (c1.InheritsFrom(c2))
				return true;
			if (c1.IsArray && (c2.Id == scripter.symbol_table.ARRAY_CLASS_id))
				return true;
			if (c1.IsDelegate && (c2.Id == scripter.symbol_table.DELEGATE_CLASS_id))
				return true;
			if ((c1.IsArray || c1.IsDelegate) && (c2.Id == scripter.symbol_table.ICLONEABLE_CLASS_id))
				return true;

			if (c1.IsArray && c2.IsArray)
			{
				int r1 = PaxSystem.GetRank(c1.Name);
				int r2 = PaxSystem.GetRank(c2.Name);
				if (r1 != r2)
					return false;
				string el_type_name1 = PaxSystem.GetElementTypeName(c1.Name);
				string el_type_name2 = PaxSystem.GetElementTypeName(c2.Name);
				int el_type_id1 = scripter.GetTypeId(el_type_name1);
				int el_type_id2 = scripter.GetTypeId(el_type_name2);
				ClassObject e1 = scripter.GetClassObject(el_type_id1);
				ClassObject e2 = scripter.GetClassObject(el_type_id2);
				if (e1.IsReferenceType && e2.IsReferenceType)
					return ExistsImplicitReferenceConversion(e1, e2);
				else
					return false;
			}

			return false;
		}

		/// <summary>
		/// Returns 'true', if there is an implicit reference conversion between
		/// types of id1 and id2.
		/// </summary>
		public bool ExistsImplicitReferenceConversion(BaseScripter scripter, int id1, int id2)
		{
			int type_id1 = scripter.symbol_table[id1].TypeId;
			int type_id2 = scripter.symbol_table[id2].TypeId;
			ClassObject c1 = scripter.GetClassObject(type_id1);
			ClassObject c2 = scripter.GetClassObject(type_id2);

			if (ExistsImplicitReferenceConversion(c1, c2))
				return true;

			if ((id1 == scripter.symbol_table.NULL_id) && (c2.IsReferenceType))
				return true;

			return false;
		}

		/// <summary>
		/// Returns 'true', if there is an implicit boxing conversion between
		/// types c1 and c2.
		/// </summary>
		public bool ExistsImplicitBoxingConversion(ClassObject c1, ClassObject c2)
		{
			BaseScripter scripter = c1.Scripter;
			if (c1.IsValueType)
			{
				if (c2.Id == scripter.symbol_table.OBJECT_CLASS_id)
					return true;
				if (c2.Id == scripter.symbol_table.VALUETYPE_CLASS_id)
					return true;
			}
			return false;
		}

		/// <summary>
		/// Returns 'true', if there is an implicit numeric conversion between
		/// types of constants represented by id1 and id2.
		/// </summary>
		public bool ExistsImplicitNumericConstConversion(BaseScripter scripter, int id1, int id2)
		{
			if (scripter.symbol_table[id1].Kind != MemberKind.Const)
				return false;

			int type_id1 = scripter.symbol_table[id1].TypeId;
			int type_id2 = scripter.symbol_table[id2].TypeId;
			if (type_id1 == (int) StandardType.Int)
			{
				if (type_id2 == (int) StandardType.Int)
					return true;
				if (type_id2 == (int) StandardType.Uint)
					return true;
				if (type_id2 == (int) StandardType.Long)
					return true;
				if (type_id2 == (int) StandardType.Ulong)
					return true;
				if (type_id2 == (int) StandardType.Byte)
				{
					int val = scripter.symbol_table[id1].ValueAsInt;
					if ((val >= byte.MinValue) && (val <= byte.MaxValue))
						return true;
				}
				if (type_id2 == (int) StandardType.Sbyte)
				{
					int val = scripter.symbol_table[id1].ValueAsInt;
					if ((val >= sbyte.MinValue) && (val <= sbyte.MaxValue))
						return true;
				}
				if (type_id2 == (int) StandardType.Short)
				{
					int val = scripter.symbol_table[id1].ValueAsInt;
					if ((val >= short.MinValue) && (val <= short.MaxValue))
						return true;
				}
				if (type_id2 == (int) StandardType.Ushort)
				{
					int val = scripter.symbol_table[id1].ValueAsInt;
					if ((val >= ushort.MinValue) && (val <= ushort.MaxValue))
						return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Returns 'true', if there is an implicit conversion between
		/// types of id1 and id2.
		/// </summary>
		public bool ExistsImplicitConversion(BaseScripter scripter, int id1, int id2)
		{
			int type_id1 = scripter.symbol_table[id1].TypeId;
			int type_id2 = scripter.symbol_table[id2].TypeId;
			if (type_id1 == type_id2) // identity
				return true;

			if (ExistsImplicitNumericConstConversion(scripter, id1, id2))
				return true;
			if (ExistsImplicitReferenceConversion(scripter, id1, id2))
				return true;
			if (ExistsImplicitEnumerationConversion(scripter, id1, id2))
				return true;
			if (ExistsImplicitNumericConversion(type_id1, type_id2))
				return true;

			ClassObject c1 = scripter.GetClassObject(type_id1);
			ClassObject c2 = scripter.GetClassObject(type_id2);
			return ExistsImplicitBoxingConversion(c1, c2);
		}

		/// <summary>
		/// Returns 1, if conversion between types of id and id1 is better than
		/// conversion between types of id and id2.
		/// Returns -1, if conversion between types of id and id2 is better than
		/// conversion between types of id and id1.
		/// Returns 0, if there is no better conversion.
		/// </summary>
		public int CompareConversions(BaseScripter scripter, int id, int id1, int id2)
		{
			int s = scripter.symbol_table[id].TypeId;

			int t1 = scripter.symbol_table[id1].TypeId;
			int t2 = scripter.symbol_table[id2].TypeId;

			if (t1 == t2)
				return 0;
			if (s == t1)
				return 1;
			if (s == t2)
				return -1;

			if (
				(t1 == (int) StandardType.Sbyte) &&
				(
					(t2 == (int) StandardType.Byte) ||
					(t2 == (int) StandardType.Ushort) ||
					(t2 == (int) StandardType.Uint) ||
					(t2 == (int) StandardType.Ulong)
				)
			   )
			   return 1;

			if (
				(t2 == (int) StandardType.Sbyte) &&
				(
					(t1 == (int) StandardType.Byte) ||
					(t1 == (int) StandardType.Ushort) ||
					(t1 == (int) StandardType.Uint) ||
					(t1 == (int) StandardType.Ulong)
				)
			   )
			   return -1;

			if (
				(t1 == (int) StandardType.Short) &&
				(
					(t2 == (int) StandardType.Ushort) ||
					(t2 == (int) StandardType.Uint) ||
					(t2 == (int) StandardType.Ulong)
				)
			   )
			   return 1;

			if (
				(t2 == (int) StandardType.Short) &&
				(
					(t1 == (int) StandardType.Ushort) ||
					(t1 == (int) StandardType.Uint) ||
					(t1 == (int) StandardType.Ulong)
				)
			   )
			   return -1;

			if (
				(t1 == (int) StandardType.Int) &&
				(
					(t2 == (int) StandardType.Uint) ||
					(t2 == (int) StandardType.Ulong)
				)
			   )
			   return 1;

			if (
				(t2 == (int) StandardType.Int) &&
				(
					(t1 == (int) StandardType.Uint) ||
					(t1 == (int) StandardType.Ulong)
				)
			   )
			   return -1;

			if (
				(t1 == (int) StandardType.Long) &&
				(t2 == (int) StandardType.Ulong)
			   )
			   return 1;

			if (
				(t2 == (int) StandardType.Long) &&
				(t1 == (int) StandardType.Ulong)
			   )
			   return -1;

			if (ExistsImplicitConversion(scripter, id1, id2))
			{
				if (ExistsImplicitConversion(scripter, id2, id1))
					return 0;
				else
					return 1;
			}

			if (ExistsImplicitConversion(scripter, id2, id1))
			{
				if (ExistsImplicitConversion(scripter, id1, id2))
					return 0;
				else
					return -1;
			}

			return 0;
		}

		/// <summary>
		/// Convertes value v to primitive.
		/// </summary>
		public static object ToPrimitive(object v)
		{
			if (v is ObjectObject)
				return (v as ObjectObject).Instance;
			else
				return v;
		}

		/// <summary>
		/// Convertes value v to System.Boolean value.
		/// </summary>
		public static bool ToBoolean(object v)
		{
			try
			{
				if (v.GetType() == typeof(bool))
					return (bool) v;
				else
					return (bool) Conversion.ChangeType(ToPrimitive(v), typeof(bool));
			}
			catch
			{
				return false;
			}
		}

		/// <summary>
		/// Convertes value v to System.Byte value.
		/// </summary>
		public static byte ToByte(object v)
		{
			try
			{
				int x = (int) Conversion.ChangeType(ToPrimitive(v), typeof(int));
				return (byte) x;
			}
			catch
			{
				return 0;
			}
		}

		/// <summary>
		/// Convertes value v to System.Char value.
		/// </summary>
		public static char ToChar(object v)
		{
			try
			{
				if (v.GetType() == typeof(char))
					return (char) v;
				else
					return (char) Conversion.ChangeType(ToPrimitive(v), typeof(char));
			}
			catch
			{
				return ' ';
			}
		}

		/// <summary>
		/// Convertes value v to System.Decimal value.
		/// </summary>
		public static decimal ToDecimal(object v)
		{
			try
			{
				if (v.GetType() == typeof(decimal))
					return (decimal) v;
				else
					return (decimal) Conversion.ChangeType(ToPrimitive(v), typeof(decimal));
			}
			catch
			{
				return 0;
			}
		}

		/// <summary>
		/// Convertes value v to System.Double value.
		/// </summary>
		public static double ToDouble(object v)
		{
			try
			{
				if (v.GetType() == typeof(double))
					return (double) v;
				else
					return (double) Conversion.ChangeType(ToPrimitive(v), typeof(double));
			}
			catch
			{
				return 0;
			}
		}

		/// <summary>
		/// Convertes value v to System.Float value.
		/// </summary>
		public static float ToFloat(object v)
		{
			try
			{
				if (v.GetType() == typeof(float))
					return (float) v;
				else
					return (float) Conversion.ChangeType(ToPrimitive(v), typeof(float));
			}
			catch
			{
				return 0;
			}
		}

		/// <summary>
		/// Convertes value v to System.Int32 value.
		/// </summary>
		public static int ToInt(object v)
		{
			try
			{
				if (v.GetType() == typeof(int))
					return (int) v;
				else
					return (int) Conversion.ChangeType(ToPrimitive(v), typeof(int));
			}
			catch
			{
				return 0;
			}
		}

		/// <summary>
		/// Convertes value v to System.Int64 value.
		/// </summary>
		public static long ToLong(object v)
		{
			try
			{
				if (v.GetType() == typeof(long))
					return (long) v;
				else
					return (long) Conversion.ChangeType(ToPrimitive(v), typeof(long));
			}
			catch
			{
				return 0;
			}
		}

		/// <summary>
		/// Convertes value v to System.Int16 value.
		/// </summary>
		public static short ToShort(object v)
		{
			try
			{
				int x = (int) Conversion.ChangeType(ToPrimitive(v), typeof(int));
				return (short) x;
			}
			catch
			{
				return 0;
			}
		}

		/// <summary>
		/// Convertes value v to System.String value.
		/// </summary>
		public static string ToString(object v)
		{
			if (v == null)
				return null;
			else
			{
				try
				{
					return (string) Conversion.ChangeType(ToPrimitive(v), typeof(string));
				}
				catch
				{
					return "";
				}
			}
		}

		/// <summary>
		/// Convertes value v to System.Enum value.
		/// </summary>
		public static object ToEnum(Type t, object v)
		{
#if SILVERLIGHT
            return v;
#else
    #if !cf
			Array a = Enum.GetValues(t);
			for (int i = 0; i < a.Length; i++)
				if ((int) (a.GetValue(i)) == (int) v)
					return a.GetValue(i);
    #endif
			return v;
#endif
		}

		public static object ChangeType(object v, Type t)
		{
#if !cf && !SILVERLIGHT

			return Convert.ChangeType(v, t);
#else
			if (t == typeof(bool))
			{
				return Convert.ToBoolean(v);
			}
			else if (t == typeof(char))
			{
				return Convert.ToChar(v);
			}
			else if (t == typeof(int))
			{
				return Convert.ToInt32(v);
			}
			else if (t == typeof(uint))
			{
				return Convert.ToUInt32(v);
			}
			else if (t == typeof(long))
			{
				return Convert.ToInt64(v);
			}
			else if (t == typeof(ulong))
			{
				return Convert.ToUInt64(v);
			}
			else if (t == typeof(float))
			{
				return Convert.ToSingle(v);
			}
			else if (t == typeof(double))
			{
				return Convert.ToDouble(v);
			}
			else if (t == typeof(decimal))
			{
				return Convert.ToDecimal(v);
			}
			else
				return v;

		#endif
		}
	}
	#endregion Conversion Class
}
