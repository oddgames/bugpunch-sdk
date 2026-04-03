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
using SL;

namespace PaxScript.Net
{
	#region SymbolRec Class
	/// <summary>
	/// Represents record of symbol table.
	/// </summary>
	internal class SymbolRec
	{
		/// <summary>
		/// Id of record.
		/// </summary>
		int id;

		/// <summary>
		/// Name index of record.
		/// </summary>
		int name_index;

		/// <summary>
		/// Level of record.
		/// </summary>
		int level;

		/// <summary>
		/// Block of record.
		/// </summary>
		int block;

		/// <summary>
		/// Type id of record.
		/// </summary>
		int type_id;

		/// <summary>
		/// Kind of record.
		/// </summary>
		MemberKind kind;

		/// <summary>
		/// Value of record.
		/// </summary>
		PaxArrayList value;

		bool is_forward = false;
		public int Count = 0;

		public bool is_static = false;

		/// <summary>
		/// Value level of record.
		/// </summary>
		internal int value_level = 0;

		/// <summary>
		/// Kernel of scripter.
		/// </summary>
		BaseScripter scripter;

		public ProgRec CodeProgRec = null;

		/// <summary>
		/// Constructor.
		/// </summary>
		internal SymbolRec(BaseScripter scripter, int id)
		{
			this.scripter = scripter;
			this.id = id;
			value = new PaxArrayList();
			value.Add(null);
			value_level = 0;
			level = 0;
			block = 0;
		}

		/// <summary>
		/// Saves record to a stream.
		/// </summary>
		public void SaveToStream(BinaryWriter bw)
		{
			bw.Write(id);
			bw.Write(Name);
			bw.Write(level);
			bw.Write(type_id);
			bw.Write((int) kind);

			if (kind == MemberKind.Label)
				bw.Write((int) Val);
			else if ((kind == MemberKind.Const) || (kind == MemberKind.Var))
			{
				bool has_value = (Val == null) ? false : true;
				bw.Write(has_value);

				if (has_value)
				{
					if (type_id == (int) StandardType.Bool)
						bw.Write(ValueAsBool);
					else if (type_id == (int) StandardType.Byte)
						bw.Write(ValueAsByte);
					else if (type_id == (int) StandardType.Char)
						bw.Write(ValueAsChar);
					else if (type_id == (int) StandardType.Decimal)
					{
#if full
						bw.Write(ValueAsDecimal);
#endif
					}
					else if (type_id == (int) StandardType.Double)
						bw.Write(ValueAsDouble);
					else if (type_id == (int) StandardType.Float)
						bw.Write(ValueAsFloat);
					else if (type_id == (int) StandardType.Int)
						bw.Write(ValueAsInt);
					else if (type_id == (int) StandardType.Long)
						bw.Write(ValueAsLong);
					else if (type_id == (int) StandardType.Sbyte)
						bw.Write(ValueAsByte);
					else if (type_id == (int) StandardType.Short)
						bw.Write(ValueAsShort);
					else if (type_id == (int) StandardType.String)
						bw.Write(ValueAsString);
					else if (type_id == (int) StandardType.Uint)
						bw.Write(ValueAsInt);
					else if (type_id == (int) StandardType.Ulong)
						bw.Write(ValueAsLong);
					else if (type_id == (int) StandardType.Ushort)
						bw.Write(ValueAsShort);
				}
			}
		}

		/// <summary>
		/// Loads record from a stream.
		/// </summary>
		public void LoadFromStream(BinaryReader br)
		{
			id = br.ReadInt32();
			Name = br.ReadString();
			level = br.ReadInt32();
			type_id = br.ReadInt32();
			kind = (MemberKind) br.ReadInt32();
			if (kind == MemberKind.Label)
				Val = br.ReadInt32();
			else if ((kind == MemberKind.Const) || (kind == MemberKind.Var))
			{
				bool has_value = br.ReadBoolean();
				if (has_value)
				{
					if (type_id == (int) StandardType.Bool)
						Val = br.ReadBoolean();
					else if (type_id == (int) StandardType.Byte)
						Val = br.ReadByte();
					else if (type_id == (int) StandardType.Char)
						Val = br.ReadChar();
					else if (type_id == (int) StandardType.Decimal)
					{
#if full
						Val = br.ReadDecimal();
#endif
					}
					else if (type_id == (int) StandardType.Double)
						Val = br.ReadDouble();
					else if (type_id == (int) StandardType.Float)
						Val = br.ReadSingle();
					else if (type_id == (int) StandardType.Int)
						Val = br.ReadInt32();
					else if (type_id == (int) StandardType.Long)
						Val = br.ReadInt64();
					else if (type_id == (int) StandardType.Sbyte)
						Val = br.ReadSByte();
					else if (type_id == (int) StandardType.Short)
						Val = br.ReadInt16();
					else if (type_id == (int) StandardType.String)
						Val = br.ReadString();
					else if (type_id == (int) StandardType.Uint)
						Val = br.ReadUInt32();
					else if (type_id == (int) StandardType.Ulong)
						Val = br.ReadUInt64();
					else if (type_id == (int) StandardType.Ushort)
						Val = br.ReadUInt16();
				}
			}
		}

		/// <summary>
		/// Increases level of value of record.
		/// </summary>
		public virtual void IncValueLevel()
		{
			value_level++;
			while (value_level >= value.Count)
				value.Add(null);
		}

		/// <summary>
		/// Decreases level of value of record.
		/// </summary>
		public virtual void DecValueLevel()
		{
			value_level--;
		}

		/// <summary>
		/// Returns id of record.
		/// </summary>
		public int Id
		{
			get
			{
				return id;
			}
		}

		/// <summary>
		/// Returns level of record.
		/// </summary>
		public int Level
		{
			get
			{
				return level;
			}
			set
			{
				level = value;
			}
		}

		/// <summary>
		/// Returns block of record.
		/// </summary>
		public int Block
		{
			get
			{
				return block;
			}
			set
			{
				block = value;
			}
		}

		/// <summary>
		/// Returns type id of record.
		/// </summary>
		public int TypeId
		{
			get
			{
				return type_id;
			}
			set
			{
				type_id = value;
			}
		}

		/// <summary>
		/// Returns name of record.
		/// </summary>
		public string Name
		{
			get
			{
				return scripter.names[name_index];
			}
			set
			{
				name_index = scripter.names.Add(value);
			}
		}

		/// <summary>
		/// Returns full name of record.
		/// </summary>
		public string FullName
		{
			get
			{
				if (Name == "System")
					return Name;

				if (kind == MemberKind.Type)
				{
					if (id == 0)
						return "";
					else if (id == scripter.symbol_table.ROOT_NAMESPACE_id)
						return Name;
					else if (id == scripter.symbol_table.SYSTEM_NAMESPACE_id)
						return Name;
					else
					{
						string s = scripter.symbol_table[level].FullName;
						if (s == "")
							return Name;
						else
							return s + "." + Name;
					}
				}
				else if (kind == MemberKind.Method || kind == MemberKind.Ref)
				{
						string s = scripter.symbol_table[level].FullName;
						if (s == "")
							return Name;
						else
							return s + "." + Name;
				}
				else
					return Name;
			}
		}

		/// <summary>
		/// Returns name index of record.
		/// </summary>
		public int NameIndex
		{
			get
			{
				return name_index;
			}
			set
			{
				name_index = value;
			}
		}

		/// <summary>
		/// Returns kind of record.
		/// </summary>
		public MemberKind Kind
		{
			get
			{
				return kind;
			}
			set
			{
				this.kind = value;
			}
		}

		public bool IsForward
		{
			get
			{
				return is_forward;
			}
			set
			{
				is_forward = value;
			}
		}

		/// <summary>
		/// Returns value of record.
		/// </summary>
		public virtual object Value
		{
			get
			{
				switch (kind)
				{
					case MemberKind.Field:
					{
						FieldObject f = (FieldObject) value[value_level];
						if (f == null)
							return null;
						return f.Value;
					}
					case MemberKind.Property:
					{
						PropertyObject p = (PropertyObject) value[value_level];
						if (p == null)
							return null;
						return p.Value;
					}
					case MemberKind.Ref:
					{
						ObjectObject o = (ObjectObject) value[value_level];
						if (o == null)
							return null;
						int type_id = scripter.symbol_table[Level].TypeId;
						return o.GetProperty(name_index, type_id);
					}
					case MemberKind.Index:
					{
						IndexObject i = (IndexObject) value[value_level];
						if (i == null)
							return null;
						return i.Value;
					}
					case MemberKind.Label:
					{
						return value[value_level];
					}
					default:
					{
						return value[value_level];
					}
				}
			}
			set
			{
				switch (kind)
				{
					case MemberKind.Field:
					{
						FieldObject f = (FieldObject) this.value[value_level];
						f.Value = value;
						break;
					}
					case MemberKind.Property:
					{
						PropertyObject p = (PropertyObject) this.value[value_level];
						p.Value = value;
						break;
					}
					case MemberKind.Ref:
					{
						ObjectObject o = (ObjectObject) this.value[value_level];
						int type_id = scripter.symbol_table[Level].TypeId;
						o.PutProperty(name_index, type_id, value);
						break;
					}
					case MemberKind.Index:
					{
						IndexObject i = (IndexObject) this.value[value_level];
						i.Value = value;
						break;
					}
					case MemberKind.Label:
					{
						if (value.GetType() == typeof(ProgRec))
							CodeProgRec = (ProgRec) value;
						else
							this.value[value_level] = value;
						break;
					}
					default:
					{
						this.value[value_level] = value;
						break;
					}
				}
			}
		}

		public string GetSignature()
		{
			string result = "";
			if (Kind != MemberKind.Method)
				return result;
			result = "(";
			for (int i = 0; i < Count; i++)
			{
				int param_id = GetParamId(i);
				int t = scripter.symbol_table[param_id].TypeId;
				result += scripter.symbol_table[t].Name;
				if (i < Count - 1)
				  result += ",";
			}
			result += ")";
			return result;
		}

		int GetParamId(int index)
		{
			int k = -1;
			for (int i = scripter.symbol_table.GetThisId(Id) + 1; i < scripter.symbol_table.Card; i++)
			{
				if (scripter.symbol_table[i].Level == Id)
				{
					k++;
					if (k == index)
						return id;
				}
			}
			return 0;
		}

		/// <summary>
		/// Returns value of record as System.Boolean.
		/// </summary>
		public virtual bool ValueAsBool
		{
			get
			{
				return Conversion.ToBoolean(Value);
			}
			set
			{
				Value = value;
			}
		}

		/// <summary>
		/// Returns value of record as System.Byte.
		/// </summary>
		public virtual byte ValueAsByte
		{
			get
			{
				return Conversion.ToByte(Value);
			}
			set
			{
				Value = value;
			}
		}

		/// <summary>
		/// Returns value of record as System.Char.
		/// </summary>
		public virtual char ValueAsChar
		{
			get
			{
				return Conversion.ToChar(Value);
			}
			set
			{
				Value = value;
			}
		}

		/// <summary>
		/// Returns value of record as System.Decimal.
		/// </summary>
		public virtual decimal ValueAsDecimal
		{
			get
			{
				return Conversion.ToDecimal(Value);
			}
			set
			{
				Value = value;
			}
		}

		/// <summary>
		/// Returns value of record as System.Double.
		/// </summary>
		public virtual double ValueAsDouble
		{
			get
			{
				return Conversion.ToDouble(Value);
			}
			set
			{
				Value = value;
			}
		}

		/// <summary>
		/// Returns value of record as System.Float.
		/// </summary>
		public virtual float ValueAsFloat
		{
			get
			{
				return Conversion.ToFloat(Value);
			}
			set
			{
				Value = value;
			}
		}

		/// <summary>
		/// Returns value of record as System.Int32.
		/// </summary>
		public virtual int ValueAsInt
		{
			get
			{
				return Conversion.ToInt(Value);
			}
			set
			{
				Value = value;
			}
		}

		/// <summary>
		/// Returns value of record as System.Int64.
		/// </summary>
		public virtual long ValueAsLong
		{
			get
			{
				return Conversion.ToLong(Value);
			}
			set
			{
				Value = value;
			}
		}

		/// <summary>
		/// Returns value of record as System.Int16.
		/// </summary>
		public virtual short ValueAsShort
		{
			get
			{
				return Conversion.ToShort(Value);
			}
			set
			{
				Value = value;
			}
		}

		/// <summary>
		/// Returns value of record as System.String.
		/// </summary>
		public virtual string ValueAsString
		{
			get
			{
				return Conversion.ToString(Value);
			}
			set
			{
				Value = value;
			}
		}

		/// <summary>
		/// Returns value of record.
		/// </summary>
		public virtual object Val
		{
			get
			{
				return value[value_level];
			}
			set
			{
				this.value[value_level] = value;
			}
		}

		/// <summary>
		/// Returns value of record as ClassObject.
		/// </summary>
		public ClassObject ValueAsClassObject
		{
			get
			{
				return (ClassObject) Val;
			}
		}
	}
	#endregion SymbolRec Class

	#region SymbolRecConstInt Class
	/// <summary>
	/// Represents record of symbol table for System.Int32 constants.
	/// </summary>

	internal class SymbolRecConstInt: SymbolRec
	{
		/// <summary>
		/// Value of constant.
		/// </summary>
		int v;

		/// <summary>
		/// Constructor.
		/// </summary>
		internal SymbolRecConstInt(BaseScripter scripter, int id): base(scripter, id)
		{
			if (scripter.symbol_table[id].Value != null)
				v = scripter.symbol_table[id].ValueAsInt;

			Level = scripter.symbol_table[id].Level;
			TypeId = scripter.symbol_table[id].TypeId;
			Kind = scripter.symbol_table[id].Kind;
			NameIndex = scripter.symbol_table[id].NameIndex;
            is_static = scripter.symbol_table[id].is_static;
		}

		/// <summary>
		/// Returns value of record as System.Int32.
		/// </summary>
		public override int ValueAsInt
		{
			get
			{
				return v;
			}
			set
			{
				v = value;
			}
		}

		/// <summary>
		/// Returns value of record.
		/// </summary>
		public override object Val
		{
			get
			{
				return ValueAsInt;
			}
			set
			{
				ValueAsInt = (int) value;
			}
		}

		/// <summary>
		/// Returns value of record.
		/// </summary>
		public override object Value
		{
			get
			{
				return ValueAsInt;
			}
			set
			{
				ValueAsInt = (int) value;
			}
		}
	}
	#endregion SymbolRecConstInt Class

	#region SymbolRecVarInt Class
	/// <summary>
	/// Represents record of symbol table for System.Int32 variables.
	/// </summary>
	internal class SymbolRecVarInt: SymbolRec
	{
		/// <summary>
		/// Value of variable.
		/// </summary>
		int[] v = new int[20];

		/// <summary>
		/// Constructor.
		/// </summary>
		internal SymbolRecVarInt(BaseScripter scripter, int id): base(scripter, id)
		{
			if (scripter.symbol_table[id].Value != null)
				v[value_level] = scripter.symbol_table[id].ValueAsInt;

			Level = scripter.symbol_table[id].Level;
			TypeId = scripter.symbol_table[id].TypeId;
			Kind = scripter.symbol_table[id].Kind;
			NameIndex = scripter.symbol_table[id].NameIndex;
            is_static = scripter.symbol_table[id].is_static;
		}

		/// <summary>
		/// Increases level of value of record.
		/// </summary>
		public override void IncValueLevel()
		{
			value_level++;
		}

		/// <summary>
		/// Returns value of record as System.Int32.
		/// </summary>
		public override int ValueAsInt
		{
			get
			{
				return v[value_level];
			}
			set
			{
				v[value_level] = value;
			}
		}

		/// <summary>
		/// Returns value of record.
		/// </summary>
		public override object Val
		{
			get
			{
				return v[value_level];
			}
			set
			{
				v[value_level] = Conversion.ToInt(value);
			}
		}

		/// <summary>
		/// Returns value of record.
		/// </summary>
		public override object Value
		{
			get
			{
				return v[value_level];
			}
			set
			{
				v[value_level] = Conversion.ToInt(value);
			}
		}
	}
	#endregion SymbolRecVarInt Class

	#region SymbolRecVarBool Class
	/// <summary>
	/// Represents record of symbol table for System.Boolean variables.
	/// </summary>
	internal class SymbolRecVarBool: SymbolRec
	{
		/// <summary>
		/// Value of variable.
		/// </summary>
		bool[] v = new bool[20];

		/// <summary>
		/// Constructor.
		/// </summary>
		internal SymbolRecVarBool(BaseScripter scripter, int id): base(scripter, id)
		{
			if (scripter.symbol_table[id].Value != null)
				v[value_level] = scripter.symbol_table[id].ValueAsBool;

			Level = scripter.symbol_table[id].Level;
			TypeId = scripter.symbol_table[id].TypeId;
			Kind = scripter.symbol_table[id].Kind;
			NameIndex = scripter.symbol_table[id].NameIndex;
            is_static = scripter.symbol_table[id].is_static;
		}

		/// <summary>
		/// Increases level of value of record.
		/// </summary>
		public override void IncValueLevel()
		{
			value_level++;
		}

		/// <summary>
		/// Returns value of record as System.Boolean.
		/// </summary>
		public override bool ValueAsBool
		{
			get
			{
				return v[value_level];
			}
			set
			{
				v[value_level] = value;
			}
		}

		/// <summary>
		/// Returns value of record.
		/// </summary>
		public override object Val
		{
			get
			{
				return v[value_level];
			}
			set
			{
				v[value_level] = Conversion.ToBoolean(value);
			}
		}

		/// <summary>
		/// Returns value of record.
		/// </summary>
		public override object Value
		{
			get
			{
				return v[value_level];
			}
			set
			{
				v[value_level] = Conversion.ToBoolean(value);
			}
		}
	}
	#endregion SymbolRecVarBool Class

	#region SymbolRecVarLong Class
	/// <summary>
	/// Represents record of symbol table for System.Int64 variables.
	/// </summary>
	internal class SymbolRecVarLong: SymbolRec
	{
		/// <summary>
		/// Value of variable.
		/// </summary>
		long[] v = new long[20];

		/// <summary>
		/// Constructor.
		/// </summary>
		internal SymbolRecVarLong(BaseScripter scripter, int id): base(scripter, id)
		{
			if (scripter.symbol_table[id].Value != null)
				v[value_level] = scripter.symbol_table[id].ValueAsLong;

			Level = scripter.symbol_table[id].Level;
			TypeId = scripter.symbol_table[id].TypeId;
			Kind = scripter.symbol_table[id].Kind;
			NameIndex = scripter.symbol_table[id].NameIndex;
            is_static = scripter.symbol_table[id].is_static;
		}

		/// <summary>
		/// Increases level of value of record.
		/// </summary>
		public override void IncValueLevel()
		{
			value_level++;
		}

		/// <summary>
		/// Returns value of record as System.Int64.
		/// </summary>
		public override long ValueAsLong
		{
			get
			{
				return v[value_level];
			}
			set
			{
				v[value_level] = value;
			}
		}

		/// <summary>
		/// Returns value of record.
		/// </summary>
		public override object Val
		{
			get
			{
				return v[value_level];
			}
			set
			{
				v[value_level] = Conversion.ToLong(value);
			}
		}

		/// <summary>
		/// Returns value of record.
		/// </summary>
		public override object Value
		{
			get
			{
				return v[value_level];
			}
			set
			{
				v[value_level] = Conversion.ToLong(value);
			}
		}
	}
	#endregion SymbolRecVarLong Class

	#region SymbolRecVarFloat Class
	/// <summary>
	/// Represents record of symbol table for System.Single variables.
	/// </summary>
	internal class SymbolRecVarFloat: SymbolRec
	{
		/// <summary>
		/// Value of variable.
		/// </summary>
		float[] v = new float[20];

		/// <summary>
		/// Constructor.
		/// </summary>
		internal SymbolRecVarFloat(BaseScripter scripter, int id): base(scripter, id)
		{
			if (scripter.symbol_table[id].Value != null)
				v[value_level] = scripter.symbol_table[id].ValueAsFloat;

			Level = scripter.symbol_table[id].Level;
			TypeId = scripter.symbol_table[id].TypeId;
			Kind = scripter.symbol_table[id].Kind;
			NameIndex = scripter.symbol_table[id].NameIndex;
            is_static = scripter.symbol_table[id].is_static;
		}

		/// <summary>
		/// Increases level of value of record.
		/// </summary>
		public override void IncValueLevel()
		{
			value_level++;
		}

		/// <summary>
		/// Returns value of record as System.Single.
		/// </summary>
		public override float ValueAsFloat
		{
			get
			{
				return v[value_level];
			}
			set
			{
				v[value_level] = value;
			}
		}

		/// <summary>
		/// Returns value of record.
		/// </summary>
		public override object Val
		{
			get
			{
				return v[value_level];
			}
			set
			{
				v[value_level] = Conversion.ToFloat(value);
			}
		}

		/// <summary>
		/// Returns value of record.
		/// </summary>
		public override object Value
		{
			get
			{
				return v[value_level];
			}
			set
			{
				v[value_level] = Conversion.ToFloat(value);
			}
		}
	}
	#endregion SymbolRecVarFloat Class

	#region SymbolRecVarDouble Class
	/// <summary>
	/// Represents record of symbol table for System.Double variables.
	/// </summary>
	internal class SymbolRecVarDouble: SymbolRec
	{
		/// <summary>
		/// Value of variable.
		/// </summary>
		double[] v = new double[20];

		/// <summary>
		/// Constructor.
		/// </summary>
		internal SymbolRecVarDouble(BaseScripter scripter, int id): base(scripter, id)
		{
			if (scripter.symbol_table[id].Value != null)
				v[value_level] = scripter.symbol_table[id].ValueAsDouble;

			Level = scripter.symbol_table[id].Level;
			TypeId = scripter.symbol_table[id].TypeId;
			Kind = scripter.symbol_table[id].Kind;
			NameIndex = scripter.symbol_table[id].NameIndex;
            is_static = scripter.symbol_table[id].is_static;
		}

		/// <summary>
		/// Increases level of value of record.
		/// </summary>
		public override void IncValueLevel()
		{
			value_level++;
		}

		/// <summary>
		/// Returns value of record as System.Double.
		/// </summary>
		public override double ValueAsDouble
		{
			get
			{
				return v[value_level];
			}
			set
			{
				v[value_level] = value;
			}
		}

		/// <summary>
		/// Returns value of record.
		/// </summary>
		public override object Val
		{
			get
			{
				return v[value_level];
			}
			set
			{
				v[value_level] = Conversion.ToDouble(value);
			}
		}

		/// <summary>
		/// Returns value of record.
		/// </summary>
		public override object Value
		{
			get
			{
				return v[value_level];
			}
			set
			{
				v[value_level] = Conversion.ToDouble(value);
			}
		}
	}
	#endregion SymbolRecVarDouble Class

	#region SymbolRecVarDecimal Class
	/// <summary>
	/// Represents record of symbol table for System.Decimal variables.
	/// </summary>
	internal class SymbolRecVarDecimal: SymbolRec
	{
		/// <summary>
		/// Value of variable.
		/// </summary>
		decimal[] v = new decimal[20];

		/// <summary>
		/// Constructor.
		/// </summary>
		internal SymbolRecVarDecimal(BaseScripter scripter, int id): base(scripter, id)
		{
			if (scripter.symbol_table[id].Value != null)
				v[value_level] = scripter.symbol_table[id].ValueAsDecimal;

			Level = scripter.symbol_table[id].Level;
			TypeId = scripter.symbol_table[id].TypeId;
			Kind = scripter.symbol_table[id].Kind;
			NameIndex = scripter.symbol_table[id].NameIndex;
            is_static = scripter.symbol_table[id].is_static;
		}

		/// <summary>
		/// Increases level of value of record.
		/// </summary>
		public override void IncValueLevel()
		{
			value_level++;
		}

		/// <summary>
		/// Returns value of record as System.Decimal.
		/// </summary>
		public override decimal ValueAsDecimal
		{
			get
			{
				return v[value_level];
			}
			set
			{
				v[value_level] = value;
			}
		}

		/// <summary>
		/// Returns value of record.
		/// </summary>
		public override object Val
		{
			get
			{
				return v[value_level];
			}
			set
			{
				v[value_level] = Conversion.ToDecimal(value);
			}
		}

		/// <summary>
		/// Returns value of record.
		/// </summary>
		public override object Value
		{
			get
			{
				return v[value_level];
			}
			set
			{
				v[value_level] = Conversion.ToDecimal(value);
			}
		}
	}
	#endregion SymbolRecVarDecimal Class

	#region SymbolRecVarString Class
	/// <summary>
	/// Represents record of symbol table for System.String variables.
	/// </summary>
	internal class SymbolRecVarString: SymbolRec
	{
		/// <summary>
		/// Value of variable.
		/// </summary>
		string[] v = new string[20];

		/// <summary>
		/// Constructor.
		/// </summary>
		internal SymbolRecVarString(BaseScripter scripter, int id): base(scripter, id)
		{
			if (scripter.symbol_table[id].Value != null)
				v[value_level] = scripter.symbol_table[id].ValueAsString;

			Level = scripter.symbol_table[id].Level;
			TypeId = scripter.symbol_table[id].TypeId;
			Kind = scripter.symbol_table[id].Kind;
			NameIndex = scripter.symbol_table[id].NameIndex;
            is_static = scripter.symbol_table[id].is_static;
		}

		/// <summary>
		/// Increases level of value of record.
		/// </summary>
		public override void IncValueLevel()
		{
			value_level++;
		}

		/// <summary>
		/// Returns value of record as System.String.
		/// </summary>
		public override string ValueAsString
		{
			get
			{
				return v[value_level];
			}
			set
			{
				v[value_level] = value;
			}
		}

		/// <summary>
		/// Returns value of record.
		/// </summary>
		public override object Val
		{
			get
			{
				return v[value_level];
			}
			set
			{
				v[value_level] = Conversion.ToString(value);
			}
		}

		/// <summary>
		/// Returns value of record.
		/// </summary>
		public override object Value
		{
			get
			{
				return v[value_level];
			}
			set
			{
				v[value_level] = Conversion.ToString(value);
			}
		}
	}
	#endregion SymbolRecVarString Class
}
