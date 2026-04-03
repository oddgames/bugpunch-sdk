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

#define dump

using System;
using System.Collections;
using System.Reflection;
using System.IO;
using SL;

namespace PaxScript.Net
{
	#region SymbolTable Class
	/// <summary>
	/// Represents symbol table.
	/// </summary>
	internal sealed class SymbolTable
	{
		/// <summary>
		/// Determines number of records for the first allocation of
		/// symbol table.
		/// </summary>
		const int FIRST_SYMBOL_CARD = 1000;
		/// <summary>
		/// Determines number of records for the subsequent allocation.
		/// </summary>
		const int DELTA_SYMBOL_CARD = 1000;

		/// <summary>
		/// List of records.
		/// </summary>
		PaxArrayList a;

		/// <summary>
		/// Kernel of scripter.
		/// </summary>
		BaseScripter scripter;

		/// <summary>
		/// Current number of records in symbol table.
		/// </summary>
		int card;

		/// <summary>
		/// Id of 'true' constant.
		/// </summary>
		int true_id;

		/// <summary>
		/// Id of 'false' constant.
		/// </summary>
		int false_id;

		/// <summary>
		/// Id of 'null' constant.
		/// </summary>
		int null_id;

		/// <summary>
		/// Id of '\n' constant.
		/// </summary>
		int br_id;

		/// <summary>
		/// Id of root (noname) namespace.
		/// </summary>
		int root_namespace_id;

		/// <summary>
		/// Id of System namespace.
		/// </summary>
		int system_namespace_id;

		/// <summary>
		/// Id of System.Object class.
		/// </summary>
		int object_class_id;

		/// <summary>
		/// Id of System.Type class.
		/// </summary>
		int type_class_id;

		/// <summary>
		/// Id of System.ValueType class.
		/// </summary>
		int valuetype_class_id;

		/// <summary>
		/// Id of System.Array class.
		/// </summary>
		int array_class_id;

		/// <summary>
		/// Id of System.Delegate class.
		/// </summary>
		int delegate_class_id;

		/// <summary>
		/// Id of System.IClonable interface.
		/// </summary>
		int icloneable_class_id;

		int array_of_object_class_id;
		public int DATETIME_CLASS_id;

		public int SYSTEM_COLLECTIONS_ID;

		/// <summary>
		/// Keeps state of symbol table.
		/// </summary>
		IntegerStack state_stack;

		/// <summary>
		/// KUndocumented.
		/// </summary>
		public int RESET_COMPILE_STAGE_CARD = 0;

		/// <summary>
		/// Constructor.
		/// </summary>
		public SymbolTable(BaseScripter scripter)
		{
			this.scripter = scripter;

			state_stack = new IntegerStack();

			a = new PaxArrayList();
			for (int id = 0; id < FIRST_SYMBOL_CARD; id++)
				a.Add(new SymbolRec(scripter, id));
		}

		/// <summary>
		/// Initializes symbol table.
		/// </summary>
		public void Init()
		{
			card = -1;
			root_namespace_id = RegisterNamespace("");

			system_namespace_id = scripter.StandardTypes.Count + 1;
			card = system_namespace_id - 1;
			RegisterNamespace("System");

			card = 0;
			for (int i = 0; i < scripter.StandardTypes.Count; i++)
				RegisterType((Type) scripter.StandardTypes.Items.Objects[i], false);
			card = system_namespace_id;

			valuetype_class_id = RegisterType(typeof(System.ValueType), false);
			array_class_id = RegisterType(typeof(System.Array), false);
			delegate_class_id = RegisterType(typeof(System.Delegate), false);
#if SILVERLIGHT
            icloneable_class_id = -1;
#else
            icloneable_class_id = RegisterType(typeof(System.ICloneable), false);
#endif
			array_of_object_class_id = RegisterType(typeof(object[]), false);
			DATETIME_CLASS_id = RegisterType(typeof(System.DateTime), false);

			true_id = AppBooleanConst(true);
			false_id = AppBooleanConst(false);
			null_id = AppVar();
			this[null_id].Kind = MemberKind.Const;
			this[null_id].TypeId = (int) StandardType.Object;
			this[null_id].Name = "null";

			string s = '"' + "\n" + '"';
			br_id = AppStringConst(s);

			for (int i = 0; i < scripter.StandardTypes.Count; i++)
			{
				ClassObject c = scripter.GetClassObject(i);
				RegisterMemberTypes((Type) scripter.StandardTypes.Items.Objects[i], c);
			}
		}

		/// <summary>
		/// Resets symbol table.
		/// </summary>
		public void Reset()
		{
			state_stack.Clear();

			while (Card > 0)
			{
				this[Card] = new SymbolRec(scripter, Card);
				Card --;
			}
		}

		/// <summary>
		/// Resets compile stage
		/// </summary>
		public void ResetCompileStage()
		{
			state_stack.Clear();

			while (Card > RESET_COMPILE_STAGE_CARD)
			{
				this[Card] = new SymbolRec(scripter, Card);
				Card --;
			}

			for (int i = Card; i >= 0; i--)
			{
				if (this[i].Kind == MemberKind.Type)
				{
					ClassObject c = scripter.GetClassObject(i);
					c.Members.ResetCompileStage();
				}
			}
		}

		/// <summary>
		/// Saves state of symbol table.
		/// </summary>
		public void SaveState()
		{
			state_stack.Push(Card);
		}

		/// <summary>
		/// Restores previous state of symbol table.
		/// </summary>
		public void RestoreState()
		{
			Card = (int) state_stack.Pop();
		}

		/// <summary>
		/// Adds new record to symbol table which will represent a type.
		/// </summary>
		int AppType(string type_name)
		{
			Card ++;
			SymbolRec s = (SymbolRec) a[Card];
			s.Kind = MemberKind.Type;
			s.Name = type_name;
			return Card;
		}

		/// <summary>
		/// Adds new record to symbol table which will represent a label.
		/// </summary>
		public int AppLabel()
		{
			int result = AppVar();
			this[result].Level = 0;
			this[result].Kind = MemberKind.Label;
			return result;
		}

		/// <summary>
		/// Assigns value of label.
		/// </summary>
		public void SetLabel(int label_id, int instruction_number)
		{
			this[label_id].Value = instruction_number;
			this[label_id].Value = scripter.code[instruction_number];
		}

		/// <summary>
		/// Returns record of symbol table by record.
		/// </summary>
		public SymbolRec this[int id]
		{
			get
			{
				return (SymbolRec) a[id];
			}
			set
			{
				a[id] = value;
			}
		}

		/// <summary>
		/// Adds new record to symbol table which will represent a variable.
		/// </summary>
		public int AppVar()
		{
			Card ++;
			SymbolRec s = (SymbolRec) a[Card];
			s.Kind = MemberKind.Var;
			s.TypeId = 0;

			return Card;
		}

		/// <summary>
		/// Adds new constant to symbol table.
		/// </summary>
		public int AppIntegerConst(int value)
		{
			SymbolRec s;
			int i = AppVar();
			s = (SymbolRec) a[i];
			s.Name = value.ToString();
			s.Kind = MemberKind.Const;
			s.Value = value;
			s.Level = 0;
			s.TypeId = (int) StandardType.Int;
			return i;
		}

		/// <summary>
		/// Adds new constant to symbol table.
		/// </summary>
		public int AppConst(string name, object value, StandardType type_id)
		{
			SymbolRec s;
			int result = AppVar();
			s = this[result];
			s.Name = name;
			s.Kind = MemberKind.Const;
			s.Value = value;
			s.Level = 0;
			s.TypeId = (int) type_id;
			return result;
		}

		/// <summary>
		/// Adds new constant to symbol table.
		/// </summary>
		public int AppConst(object value, int type_id)
		{
			SymbolRec s;
			int result = AppVar();
			s = this[result];
#if PORTABLE
            if (value == null)
#else
			if (value == null || value == System.DBNull.Value)
#endif
				s.Name = "null";		
			else
				s.Name = value.ToString();
			s.Kind = MemberKind.Const;
			s.Value = value;
			s.Level = 0;
			s.TypeId = type_id;
			return result;
		}

		/// <summary>
		/// Adds new character constant to symbol table.
		/// </summary>
		public int AppCharacterConst(char value)
		{
			SymbolRec s;
			int i = AppVar();
			s = (SymbolRec) a[i];
			s.Name = value.ToString();
			s.Kind = MemberKind.Const;
			s.Value = value;
			s.Level = 0;
			s.TypeId = (int) StandardType.Char;
			return i;
		}

		/// <summary>
		/// Adds new string constant to symbol table.
		/// </summary>
		public int AppStringConst(string value)
		{
			SymbolRec s;
			int i = AppVar();
			s = (SymbolRec) a[i];
			s.Name = value;
			s.Kind = MemberKind.Const;
			s.Value = value.Substring(1, value.Length - 2);
			s.Level = 0;
			s.TypeId = (int) StandardType.String;
			return i;
		}

		/// <summary>
		/// Adds new boolean constant to symbol table.
		/// </summary>
		public int AppBooleanConst(bool value)
		{
			SymbolRec s;
			int i = AppVar();
			s = (SymbolRec) a[i];
			s.Kind = MemberKind.Const;
			s.Value = value;
			s.Level = 0;
			s.TypeId = (int) StandardType.Bool;
			if (value)
				s.Name = "true";
			else
				s.Name = "false";
			return i;
		}

		/// <summary>
		/// Returns id of result of a method.
		/// </summary>
		public int GetResultId(int sub_id)
		{
			return sub_id + 2;
		}

		/// <summary>
		/// Returns id of 'this' parameter of a method.
		/// </summary>
		public int GetThisId(int sub_id)
		{
			return sub_id + 3;
		}

		/// <summary>
		/// Returns id of name.
		/// </summary>
		public int LookupID(string name, int level, bool upcase)
		{
			string upcase_name = null;
			if (upcase)
				upcase_name = name.ToUpper();

			for (int i = Card; i >= 1; i--)
			{
				SymbolRec s = this[i];
				MemberKind k = s.Kind;

				if (k == MemberKind.Const || k == MemberKind.None ||
					k == MemberKind.Ref ||
					 s.Level != level)
					continue;


				if (upcase)
				{
					if (s.Name.ToUpper() == upcase_name)
						return i;
				}
				else
				{
					if (s.Name == name)
						return i;
				}
			}
			return 0;
		}

		/// <summary>
		/// Returns id of local field of a method.
		/// </summary>
		public int LookupIDLocal(string name, int level, bool upcase)
		{
			string upcase_name = null;
			if (upcase)
				upcase_name = name.ToUpper();
				
			for (int i = Card; i >= 1; i--)
			{
				if (i < level)
					return 0;

				SymbolRec s = this[i];
				MemberKind k = s.Kind;

				if (k == MemberKind.Const || k == MemberKind.None ||
					  k == MemberKind.Ref || s.Level != level)
					continue;

				if (upcase)
				{
					if (s.Name.ToUpper() == upcase_name)
						return i;
				}
				else
				{
					if (s.Name == name)
						return i;
				}
			}
			return 0;
		}

		/// <summary>
		/// Returns id of a name.
		/// </summary>
		public int LookupFullName(string full_name, bool upcase)
		{
			string upcase_name = null;
			if (upcase)
				upcase_name = full_name.ToUpper();

			for (int i = Card; i >= 1; i--)
			{
				SymbolRec s = (SymbolRec) a[i];

				if (upcase)
				{
					if (s.FullName.ToUpper() == upcase_name)
						return i;
				}
				else
				{
					if (s.FullName == full_name)
						return i;
				}
			}
			return 0;
		}

		/// <summary>
		/// Returns id of a type name.
		/// </summary>
		public int LookupTypeByName(string name, bool upcase)
		{
			string upcase_name = null;
			if (upcase)
				upcase_name = name.ToUpper();

			for (int i = Card; i >= 1; i--)
			{
				SymbolRec s = (SymbolRec) a[i];

				if (s.Kind != MemberKind.Type)
					continue;

				if (upcase)
				{
					if (s.Name.ToUpper() == upcase_name)
						return i;
				}
				else
				{
					if (s.Name == name)
						return i;
				}
			}
			return 0;
		}

		/// <summary>
		/// Returns id of a type name.
		/// </summary>
		public int LookupTypeByFullName(string full_name, bool upcase)
		{
			string upcase_name = null;
			if (upcase)
				upcase_name = full_name.ToUpper();

			for (int i = Card; i >= 1; i--)
			{
				SymbolRec s = (SymbolRec) a[i];

				if (s.Kind != MemberKind.Type)
					continue;

				if (upcase)
				{
					if (s.FullName.ToUpper() == upcase_name)
						return i;
				}
				else
				{
					if (s.FullName == full_name)
						return i;
				}
			}
			return 0;
		}

		/// <summary>
		/// Registers a host-defined namespace.
		/// </summary>
		public int RegisterNamespace(string namespace_name)
		{
			int id, owner_id;
			int i = scripter.RegisteredNamespaces.IndexOf(namespace_name);
			if (i >= 0)
			{
				id = (int) scripter.RegisteredNamespaces.Objects[i];
				return id;
			}

			char cc;
			string owner_name = PaxSystem.ExtractOwner(namespace_name, out cc);

			if (namespace_name == "System")
				owner_id = 0;
			else if (owner_name == "")
				owner_id = 0;
			else
				owner_id = RegisterNamespace(owner_name);

			namespace_name = PaxSystem.ExtractName(namespace_name);

			id = AppType(namespace_name);
			ClassObject c = new ClassObject(scripter, id, owner_id, ClassKind.Namespace);
			c.Imported = true;
			this[id].Level = owner_id;
			c.Modifiers.Add(Modifier.Public);
			c.Modifiers.Add(Modifier.Static);
			this[id].Value = c;

			if (namespace_name != "")
			{
				ClassObject o = (ClassObject) (this[owner_id].Val);
				o.AddMember(c);
			}

			scripter.RegisteredNamespaces.AddObject(c.FullName, id);

			if (c.FullName == "System.Collections")
			  SYSTEM_COLLECTIONS_ID = id;

			return id;
		}

		/// <summary>
		/// Registers a host-defined type.
		/// </summary>
		public int RegisterType(Type t, bool recursive)
		{
			int type_id = scripter.RegisteredTypes.FindRegisteredTypeId(t);
			if (type_id > 0)
				return type_id;

			string full_name = t.FullName;

            if (full_name == null)
            {
                return 0;
            }

			char cc;
			string owner_name = PaxSystem.ExtractOwner(full_name, out cc);

			int owner_id;
			if (owner_name == "System")
				owner_id = system_namespace_id;
			else if (owner_name == "")
				owner_id = root_namespace_id;
			else
			{
				if (cc == '.')
					owner_id = RegisterNamespace(owner_name);
				else
				{
					owner_id = LookupTypeByName(owner_name, false);
					if (owner_id == 0)
					{
						Type tt = Type.GetType(owner_name);
						if (tt != null)
							owner_id = RegisterType(tt, false);
					}
				}
			}

			ClassKind ck = ClassKind.Class;
			if (t.IsArray)
				ck = ClassKind.Array;
			else if (t.IsEnum)
				ck = ClassKind.Enum;
			else if (t.IsInterface)
				ck = ClassKind.Interface;
			else if (t.IsValueType)
				ck = ClassKind.Struct;
			else if (t.BaseType == typeof(MulticastDelegate))
				ck = ClassKind.Delegate;

			type_id = AppType(t.Name);

			if (t == typeof(System.Type))
				type_class_id = type_id;
			else if (t == typeof(System.Object))
				object_class_id = type_id;

			ClassObject c = new ClassObject(scripter, type_id, owner_id, ck);
			c.Modifiers.Add(Modifier.Public);
			c.Modifiers.Add(Modifier.Static);

			if (t.IsSealed)
				c.Modifiers.Add(Modifier.Sealed);

			if (t.IsAbstract)
				c.Modifiers.Add(Modifier.Abstract);

			c.Imported = true;
			c.ImportedType = t;
            c.RType = t;
			this[type_id].Value = c;
			this[type_id].Level = owner_id;

			ClassObject o = (ClassObject) (this[owner_id].Val);
			o.AddMember(c);

			if (t.BaseType == typeof(MulticastDelegate))
			{
				int sub_id = AppVar();

				FunctionObject f = new FunctionObject(scripter, sub_id, c.Id);
				this[sub_id].Kind = MemberKind.Method;
				this[sub_id].Level = c.Id;
				this[sub_id].Val = f;

				int id;
				id = AppLabel();
				this[id].Level = sub_id;
				id = AppVar();
				this[id].Level = sub_id;
				id = AppVar();
				this[id].Level = sub_id;

				c.AddMember(f);
				c.PatternMethod = f;
			}

			scripter.RegisteredTypes.RegisterType(t, type_id);

			if (recursive)
				RegisterMemberTypes(t, c);

			return type_id;
		}

		/// <summary>
		/// Registers members of a host-defined type.
		/// </summary>
		public void RegisterMemberTypes(Type t, ClassObject c)
		{
			ConstructorInfo[] constructors = t.GetConstructors();
			foreach (ConstructorInfo info in constructors)
			{
				ParameterInfo[] parameters = info.GetParameters();
				foreach (ParameterInfo parameter in parameters)
					RegisterType(parameter.ParameterType, true);
			}

			MethodInfo[] methods = t.GetMethods();
			foreach (MethodInfo info in methods)
			{
				RegisterType(info.ReturnType, false);
				ParameterInfo[] parameters = info.GetParameters();
				foreach (ParameterInfo parameter in parameters)
					RegisterType(parameter.ParameterType, false);
			}

			FieldInfo[] fields = t.GetFields();
			foreach (FieldInfo info in fields)
			{
				RegisterType(info.FieldType, false);
			}

			Type[] types = t.GetNestedTypes(BindingFlags.Public);
			foreach (Type tt in types)
			{
				int id = RegisterType(tt, true);
				this[id].Level = c.Id;
				c.AddMember(scripter.GetClassObject(id));
			}

			Type[] interfaces = t.GetInterfaces();
			foreach (Type intf_type in interfaces)
			{
				int type_id = RegisterType(intf_type, false);
				c.AncestorIds.Add(type_id);
			}

			if (t.IsEnum)
			{
				Type underlying_type = Enum.GetUnderlyingType(t);
				int type_id = RegisterType(underlying_type, false);
				c.UnderlyingType = scripter.GetClassObject(type_id);
			}

			Type base_type = t.BaseType;
			if (base_type != null && base_type != typeof(object))
			{
				int type_id = RegisterType(base_type, true);
				c.AncestorIds.Add(type_id);
			}
		}

        /// <summary>
        /// Registers constructor of a host-defined type.
        /// </summary>
        public void RegisterConstructor(ConstructorInfo info)
        {
            Type t = info.DeclaringType;
            int type_id = scripter.RegisteredTypes.FindRegisteredTypeId(t);
            if (type_id <= 0)
                return;
            RegisterConstructor(info, type_id);
        }

		/// <summary>
		/// Registers constructor of a host-defined type.
		/// </summary>
		public int RegisterConstructor(ConstructorInfo info, int type_id)
		{
			int sub_id = AppVar();
			this[sub_id].Name = info.Name;
			this[sub_id].Level = type_id;
			this[sub_id].Kind = MemberKind.Constructor;

			FunctionObject f = new FunctionObject(scripter, sub_id, type_id);

			if (info.IsPublic)
				f.Modifiers.Add(Modifier.Public);
			if (info.IsStatic)
				f.Modifiers.Add(Modifier.Static);
			if (info.IsAbstract)
				f.Modifiers.Add(Modifier.Abstract);
			if (info.IsVirtual)
				f.Modifiers.Add(Modifier.Virtual);
			f.Imported = true;
			f.Constructor_Info = info;
			this[sub_id].Value = f;

			int res_id = AppVar();
			int this_id = AppVar();
			if (!info.IsStatic)
				this[this_id].Name = "this";

			ParameterInfo[] parameters = info.GetParameters();
			foreach (ParameterInfo parameter in parameters)
			{
				int param_id = AppVar();
				this[param_id].Level = sub_id;
				this[param_id].TypeId = RegisterType(parameter.ParameterType, false);

				object default_value = null;
#if PORTABLE
                if (parameter.DefaultValue != null)
#else
				if (parameter.DefaultValue != null && parameter.DefaultValue != System.DBNull.Value)
#endif
					default_value = parameter.DefaultValue;

#if full
				if (parameter.IsRetval)
					f.AddParam(param_id, ParamMod.RetVal);
				else if (parameter.IsOut)
					f.AddParam(param_id, ParamMod.Out);
				else
#endif
					f.AddParam(param_id, ParamMod.None);
			}

			f.SetupParameters();

			ClassObject c = (ClassObject) (this[type_id].Val);
			c.AddMember(f);

			return sub_id;
		}

		/// <summary>
		/// Returns id of method of a host-defined type.
		/// </summary>
		int FindMethod(MethodInfo info)
		{
			for (int i = card; i >= 1; i--)
				if (this[i].Kind == MemberKind.Method)
				{
					FunctionObject f = scripter.GetFunctionObject(i);
					if (f.Method_Info == info)
						return i;
				}
			return -1;
		}

        /// <summary>
        /// Registers method of a host-defined type.
        /// </summary>
        public void RegisterMethod(MethodInfo info)
        {
            Type t = info.DeclaringType;
            int type_id = scripter.RegisteredTypes.FindRegisteredTypeId(t);
            if (type_id <= 0)
                return;
            RegisterMethod(info, type_id);
        }

		/// <summary>
		/// Registers method of a host-defined type.
		/// </summary>
		public int RegisterMethod(MethodInfo info, int type_id)
		{
			int sub_id = FindMethod(info);
			if (sub_id > 0)
				return sub_id;

			sub_id = AppVar();
			this[sub_id].Name = info.Name;
			this[sub_id].Level = type_id;
			this[sub_id].Kind = MemberKind.Method;
			this[sub_id].TypeId = RegisterType(info.ReturnType, false);

			FunctionObject f = new FunctionObject(scripter, sub_id, type_id);

			if (info.IsPublic)
				f.Modifiers.Add(Modifier.Public);
			if (info.IsStatic)
				f.Modifiers.Add(Modifier.Static);
			if (info.IsAbstract)
				f.Modifiers.Add(Modifier.Abstract);
			if (info.IsVirtual)
				f.Modifiers.Add(Modifier.Virtual);
			f.Imported = true;
			f.Method_Info = info;
			this[sub_id].Value = f;

			//label
			AppVar();

			//result
			int res_id = AppVar();
			this[res_id].Level = sub_id;
			this[res_id].TypeId = RegisterType(info.ReturnType, false);

			//this
			int this_id = AppVar();
			this[this_id].Level = sub_id;
			if (!info.IsStatic)
				this[this_id].Name = "this";

			bool is_optional = false;

			int param_id = 0;
			ParameterInfo[] parameters = info.GetParameters();
			foreach (ParameterInfo parameter in parameters)
			{
				param_id = AppVar();
				this[param_id].Level = sub_id;
				this[param_id].TypeId = RegisterType(parameter.ParameterType, false);

#if full
				if (parameter.IsRetval)
					f.AddParam(param_id, ParamMod.RetVal);
				else if (parameter.IsOut)
					f.AddParam(param_id, ParamMod.Out);
				else
#endif
					f.AddParam(param_id, ParamMod.None);

#if PORTABLE
                if (is_optional || parameter.DefaultValue != null)
#else
				if (is_optional || (parameter.DefaultValue != null && parameter.DefaultValue != System.DBNull.Value))
#endif
				{
					int param_type_id = this[param_id].TypeId;
					object x = parameter.DefaultValue;
					if (parameter.ParameterType.IsEnum)
					{
						x = Conversion.ToEnum(parameter.ParameterType, x);
					}
					int default_value_id = AppConst(x, param_type_id);
					f.AddDefaultValueId(param_id, default_value_id);
					is_optional = true;
				}

			}
			if (param_id != 0)
			{
				int param_type_id = this[param_id].TypeId;
				string last_param_type = this[param_type_id].Name;
				if (PaxSystem.GetRank(last_param_type) == 1)
				{
					f.ParamsId = param_id;
					int array_type_id = this[f.ParamsId].TypeId;
					string element_type_name = PaxSystem.GetElementTypeName(this[array_type_id].Name);
					int element_type_id = scripter.GetTypeId(element_type_name);
					f.ParamsElementId = AppVar();
					this[f.ParamsElementId].TypeId = element_type_id;
				}
			}

			f.SetupParameters();

			ClassObject c = (ClassObject) (this[type_id].Val);
			c.AddMember(f);

			return sub_id;
		}

		/// <summary>
		/// Registers field of a host-defined type.
		/// </summary>
        public void RegisterField(FieldInfo info)
        {
            Type t = info.DeclaringType;
            int type_id = scripter.RegisteredTypes.FindRegisteredTypeId(t);
            if (type_id <= 0)
                return;
            RegisterField(info, type_id);
        }

		/// <summary>
		/// Registers field of a host-defined type.
		/// </summary>
		public int RegisterField(FieldInfo info, int type_id)
		{
			int id = AppVar();
			this[id].Name = info.Name;
			this[id].Level = type_id;
			this[id].Kind = MemberKind.Field;
			this[id].TypeId = RegisterType(info.FieldType, false);

			FieldObject f = new FieldObject(scripter, id, type_id);

			if (info.IsPublic)
				f.Modifiers.Add(Modifier.Public);
			if (info.IsStatic)
				f.Modifiers.Add(Modifier.Static);
			f.Imported = true;
			f.Field_Info = info;
			this[id].Val = f;

			ClassObject c = (ClassObject) (this[type_id].Val);
			c.AddMember(f);

			f.OwnerType = c.ImportedType;

			return id;
		}

        /// <summary>
        /// Registers property of a host-defined type.
        /// </summary>
        public void RegisterProperty(PropertyInfo info)
        {
            Type t = info.DeclaringType;
            int type_id = scripter.RegisteredTypes.FindRegisteredTypeId(t);
            if (type_id <= 0)
                return;
            RegisterProperty(info, type_id);
        }

		/// <summary>
		/// Registers property of a host-defined type.
		/// </summary>
		public int RegisterProperty(PropertyInfo info, int type_id)
		{
			int id = AppVar();
			this[id].Name = info.Name;
			this[id].Level = type_id;
			this[id].Kind = MemberKind.Property;
			this[id].TypeId = RegisterType(info.PropertyType, false);

			ParameterInfo[] parameters = info.GetIndexParameters();
			PropertyObject p = new PropertyObject(scripter, id, type_id, parameters.Length);

			p.Imported = true;
			p.Property_Info = info;
			this[id].Val = p;

			ClassObject c = (ClassObject) (this[type_id].Val);
			c.AddMember(p);

			p.OwnerType = c.ImportedType;

			MethodInfo get_info = info.GetGetMethod();
			if (get_info == null && scripter.SearchProtected)
				get_info = info.GetGetMethod(true);

			if (get_info != null)
			{
				p.ReadId = RegisterMethod(get_info, type_id);

				if (get_info.IsPublic)
					p.Modifiers.Add(Modifier.Public);
				if (get_info.IsStatic)
					p.Modifiers.Add(Modifier.Static);
			}

			MethodInfo set_info = info.GetSetMethod();
			if (set_info == null && scripter.SearchProtected)
				set_info = info.GetSetMethod(true);

			if (set_info != null)
			{
				p.WriteId = RegisterMethod(set_info, type_id);

				if (set_info.IsPublic)
					p.Modifiers.Add(Modifier.Public);
				if (set_info.IsStatic)
					p.Modifiers.Add(Modifier.Static);
			}

			return id;
		}

        /// <summary>
        /// Registers event of a host-defined type.
        /// </summary>
        public void RegisterEvent(EventInfo info)
        {
            Type t = info.DeclaringType;
            int type_id = scripter.RegisteredTypes.FindRegisteredTypeId(t);
            if (type_id <= 0)
                return;
            RegisterEvent(info, type_id);
        }

		/// <summary>
		/// Registers event of a host-defined type.
		/// </summary>
		public int RegisterEvent(EventInfo info, int type_id)
		{
			int id = AppVar();
			this[id].Name = info.Name;
			this[id].Level = type_id;
			this[id].Kind = MemberKind.Event;
			this[id].TypeId = RegisterType(info.EventHandlerType, false);

			EventObject e = new EventObject(scripter, id, type_id);

			e.Imported = true;
			e.Event_Info = info;
			this[id].Val = e;

			ClassObject c = (ClassObject) (this[type_id].Val);
			c.AddMember(e);

			e.OwnerType = c.ImportedType;

			MethodInfo add_info = info.GetAddMethod();
			if (add_info != null)
			{
				e.AddId = RegisterMethod(add_info, type_id);

				if (add_info.IsPublic)
					e.Modifiers.Add(Modifier.Public);
				if (add_info.IsStatic)
					e.Modifiers.Add(Modifier.Static);
			}

			MethodInfo remove_info = info.GetRemoveMethod();
			if (remove_info != null)
			{
				e.RemoveId = RegisterMethod(remove_info, type_id);

				if (remove_info.IsPublic)
					e.Modifiers.Add(Modifier.Public);
				if (remove_info.IsStatic)
					e.Modifiers.Add(Modifier.Static);
			}

			return id;
		}

		/// <summary>
		/// Registers instance of a host-defined type.
		/// </summary>
		public void RegisterVariable(string full_name, Type t, bool need_check)
		{
			if (need_check && LookupFullName(full_name, true) > 0)
				return;

			string instance_name = PaxSystem.ExtractName(full_name);
			char cc;
			string namespace_name = PaxSystem.ExtractOwner(full_name, out cc);

			int type_id = RegisterType(t, true);

			int namespace_id = RegisterNamespace(namespace_name);

			int id = AppVar();
			this[id].Name = instance_name;
			this[id].Level = namespace_id;
			this[id].Kind = MemberKind.Var;
			this[id].TypeId = RegisterType(t, false);

			MemberObject m = new MemberObject(scripter, id, namespace_id);
			m.Modifiers.Add(Modifier.Public);
			m.Modifiers.Add(Modifier.Static);
			ClassObject c = (ClassObject) (this[namespace_id].Val);
			c.AddMember(m);
		}

		/// <summary>
		/// Registers instance of a host-defined type.
		/// </summary>
		public void RegisterInstance(string full_name, object instance, bool need_check)
		{
			if (need_check && LookupFullName(full_name, true) > 0)
				return;

			string instance_name = PaxSystem.ExtractName(full_name);
			char cc;
			string namespace_name = PaxSystem.ExtractOwner(full_name, out cc);

			Type t = instance.GetType();
			int type_id = RegisterType(t, true);

			int namespace_id = RegisterNamespace(namespace_name);

			int id = AppVar();
			this[id].Name = instance_name;
			this[id].Level = namespace_id;
			this[id].Kind = MemberKind.Var;
			this[id].TypeId = RegisterType(t, false);
			this[id].Val = instance;

			MemberObject m = new MemberObject(scripter, id, namespace_id);
			m.Modifiers.Add(Modifier.Public);
			m.Modifiers.Add(Modifier.Static);
			ClassObject c = (ClassObject) (this[namespace_id].Val);
			c.AddMember(m);
		}

		/// <summary>
		/// Returns id of a name.
		/// </summary>
		public int LookupVarByFullName(string full_name)
		{
			for (int i = 0; i < Card; i++)
			{
				SymbolRec s = (SymbolRec) a[i];
				if (s.Kind == MemberKind.Var &&
					s.TypeId != 0 &&
					s.FullName == full_name)
					return i;
			}
			return 0;
		}

		/// <summary>
		/// Reregisteres instance of a host-defined type.
		/// </summary>
		public bool ReregisterInstance(string full_name, object instance)
		{
			int id = LookupVarByFullName(full_name);
			bool result = id > 0;
			if (result)
			{
				this[id].Val = instance;
			}
			return result;
		}


#if full
		/// <summary>
		/// Creates .NET types which correspond to script-defined types.
		/// </summary>
		public void CreateReflectedTypes()
		{
			for (int i = 1; i <= Card; i++)
			{
				SymbolRec s = this[i];
				if (s.Kind == MemberKind.Type)
				{
					ClassObject c = s.ValueAsClassObject;
					bool ok = true;
					try
					{
						if (c.Class_Kind == ClassKind.Enum)
							c.DefineReflectedEnum();
						else if (c.Class_Kind == ClassKind.Class)
							c.DefineReflectedClass();
						else if (c.Class_Kind == ClassKind.Interface)
							c.DefineReflectedInterface();
						else if (c.Class_Kind == ClassKind.Struct)
							c.DefineReflectedStruct();
					}
					catch (Exception e)
					{
						ok = false;
						scripter.Dump();
						scripter.code.n = c.PCodeLine;
						scripter.CreateErrorObject(e.Message);
					}
					if (!ok)
						break;
				}
			}
		}
#endif
		/// <summary>
		/// Saves symbol table to a stream.
		/// </summary>
		public void SaveToStream(BinaryWriter bw, Module m)
		{
			// save records

			for (int i = m.S1; i <= m.S2; i++)
			{
				SymbolRec s = this[i];
				s.SaveToStream(bw);
			}
		}

		/// <summary>
		/// Loads symbol table from a stream.
		/// </summary>
		public void LoadFromStream(BinaryReader br, Module m, int ds, int dp)
		{
			bool shift = (ds != 0) || (dp != 0);

			// load interval

			for (int i = m.S1; i <= m.S2; i++)
			{
				Card ++;
				SymbolRec s = this[Card];
				s.LoadFromStream(br);

				if (shift)
				{
					if (m.IsInternalId(s.Level))
						s.Level += ds;

					if (m.IsInternalId(s.TypeId))
						s.TypeId += ds;

					if (s.Kind == MemberKind.Label)
					{
						int val = (int) s.Val;
						val += dp;
						s.Val = val;
					}
				}

				if (s.Kind == MemberKind.Label)
				{
					int j = (int) s.Value;
					s.CodeProgRec = scripter.code[j];
				}
			}
		}

		/// <summary>
		/// Returns method id.
		/// </summary>
		public int GetMethodId(string full_name)
		{
			for (int i = 1; i <= Card; i++)
				if (this[i].Kind == MemberKind.Method)
					if (this[i].FullName == full_name)
						return i;
			return -1;
		}

		public int GetMemberId(int type_id, string member_name)
		{
			for (int i = 1; i <= Card; i++)
				if (this[i].Level == type_id)
					if (this[i].Name == member_name)
						return i;

			ClassObject c = scripter.GetClassObject(type_id);
			for (int k = 0; k < c.AncestorIds.Count; k++)
			{
				 type_id = c.AncestorIds[k];
				 int result = GetMemberId(type_id, member_name);
				 if (result > 0)
					return result;
			}

			return - 1;
		}

		/// <summary>
		/// Returns method id.
		/// </summary>
		public int GetMethodIdEx(string full_name)
		{
			char c;
			string owner_name = PaxSystem.ExtractOwner(full_name, out c);
			if (owner_name == "")
				return GetMethodId(full_name);

			int type_id = LookupTypeByFullName(owner_name, false);
			if (type_id <= 0 )
				return -1;

			string method_name = PaxSystem.ExtractName(full_name);
			return GetMemberId(type_id, method_name);
		}

		/// <summary>
		/// Returns method id.
		/// </summary>
		public int GetMethodId(string full_name, string signature)
		{
			for (int i = 1; i <= Card; i++)
				if (this[i].Kind == MemberKind.Method)
					if (this[i].FullName == full_name)
					{
						FunctionObject f = scripter.GetFunctionObject(i);
						if (f.Signature == signature)
							return i;
					}
			return -1;
		}

		public int LookupForwardDeclaration(int id, bool upcase)
		{
			string signature = this[id].GetSignature();
			string name = this[id].Name;
			int level = this[id].Level;

			for (int i = id - 1; i >= 0; i--)
			{
				if (this[i].IsForward && this[i].Level == level && this[i].Kind == MemberKind.Method)
				{
					if (upcase)
					{
						if (PaxSystem.StrEql(this[i].Name, name) && PaxSystem.StrEql(this[i].GetSignature(), signature))
							return i;
					}
					else
					{
						if (this[i].Name == name && this[i].GetSignature() == signature)
							return i;
					}
				}
			}
			return 0;
		}

		/// <summary>
		/// Returns current number of records in symbol table.
		/// </summary>
		public int Card
		{
			get
			{
				return card;
			}
			set
			{
				while (value >= a.Count)
				{
					int id = card;
					for (int i = 0; i < DELTA_SYMBOL_CARD; i++)
					{
						id++;
						a.Add(new SymbolRec(scripter, id));
					}
				}
				card = value;
			}
		}

		/// <summary>
		/// Returns id of 'true' constant.
		/// </summary>
		public int TRUE_id
		{
			get
			{
				return true_id;
			}
		}

		/// <summary>
		/// Returns id of 'false' constant.
		/// </summary>
		public int FALSE_id
		{
			get
			{
				return false_id;
			}
		}

		/// <summary>
		/// Returns id of 'null' constant.
		/// </summary>
		public int NULL_id
		{
			get
			{
				return null_id;
			}
		}

		/// <summary>
		/// Returns id of '\n' constant.
		/// </summary>
		public int BR_id
		{
			get
			{
				return br_id;
			}
		}

		/// <summary>
		/// Returns id of System namespace.
		/// </summary>
		public int SYSTEM_NAMESPACE_id
		{
			get
			{
				return system_namespace_id;
			}
		}

		/// <summary>
		/// Returns id of root (noname) namespace.
		/// </summary>
		public int ROOT_NAMESPACE_id
		{
			get
			{
				return root_namespace_id;
			}
		}

		/// <summary>
		/// Returns id of System.Object class.
		/// </summary>
		public int OBJECT_CLASS_id
		{
			get
			{
				return object_class_id;
			}
		}

		/// <summary>
		/// Returns id of System.Type class.
		/// </summary>
		public int TYPE_CLASS_id
		{
			get
			{
				return type_class_id;
			}
		}

		/// <summary>
		/// Returns id of System.ValueType class.
		/// </summary>
		public int VALUETYPE_CLASS_id
		{
			get
			{
				return valuetype_class_id;
			}
		}

		/// <summary>
		/// Returns id of System.Array class.
		/// </summary>
		public int ARRAY_CLASS_id
		{
			get
			{
				return array_class_id;
			}
		}

		/// <summary>
		/// Returns id of System.Delegate class.
		/// </summary>
		public int DELEGATE_CLASS_id
		{
			get
			{
				return delegate_class_id;
			}
		}

		/// <summary>
		/// Returns id of System.IClonable interface.
		/// </summary>
		public int ICLONEABLE_CLASS_id
		{
			get
			{
				return icloneable_class_id;
			}
		}

		/// <summary>
		/// Returns id of System.Object[]
		/// </summary>
		public int ARRAY_OF_OBJECT_CLASS_id
		{
			get
			{
				return array_of_object_class_id;
			}
		}

		public void SetupFastAccessRecords()
		{
			for (int i = 1; i <= Card; i++)
				if (this[i].Kind == MemberKind.Const &&
					this[i].TypeId == (int) StandardType.Int)
				{
					this[i] = new SymbolRecConstInt(scripter, i);
				}
				else if (this[i].Kind == MemberKind.Var)
				{
					if (this[i].TypeId == (int) StandardType.Int)
						this[i] = new SymbolRecVarInt(scripter, i);
					else if (this[i].TypeId == (int) StandardType.Bool)
						this[i] = new SymbolRecVarBool(scripter, i);
					else if (this[i].TypeId == (int) StandardType.Long)
						this[i] = new SymbolRecVarLong(scripter, i);
					else if (this[i].TypeId == (int) StandardType.Float)
						this[i] = new SymbolRecVarFloat(scripter, i);
					else if (this[i].TypeId == (int) StandardType.Double)
						this[i] = new SymbolRecVarDouble(scripter, i);
					else if (this[i].TypeId == (int) StandardType.Decimal)
						this[i] = new SymbolRecVarDecimal(scripter, i);
					else if (this[i].TypeId == (int) StandardType.String)
						this[i] = new SymbolRecVarString(scripter, i);
				}
		}

        /// <summary>
        /// Prints symbol table.
        /// </summary>
        public void DumpSymbolTable(string FileName)
		{
		#if dump
			StreamWriter t = File.CreateText(FileName);

			string index = PaxSystem.Norm("index", 10);
			string name = PaxSystem.Norm("name", 20);
			string full_name = PaxSystem.Norm("full name", 45);
			string type = PaxSystem.Norm("type", 15);
			string level = PaxSystem.Norm("level", 10);
			string block = PaxSystem.Norm("block", 5);
			string kind = PaxSystem.Norm("kind", 10);
			string value = PaxSystem.Norm("value", 10);

			string str = index + "|" +
						 name + "|" +
						 full_name + "|" +
						 kind + "|" +
						 type + "|" +
						 level + "|" +
						 block + "|" +
						 value;

			t.WriteLine(str);

			for (int i=0; i<=Card; i++)
			{
				SymbolRec s = (SymbolRec) a[i];

				index = PaxSystem.Norm(s.Id, 10);
				name = PaxSystem.Norm(s.Name, 20);
				full_name = PaxSystem.Norm(s.FullName, 45);
				level = PaxSystem.Norm(s.Level, 10);
				if (s.TypeId != 0)
				{
					if (s.TypeId > root_namespace_id)
						type = PaxSystem.Norm(s.TypeId.ToString() + ":" + this[s.TypeId].Name, 15);
					else
						type = PaxSystem.Norm(this[s.TypeId].Name, 15);
				}
				else
					type = PaxSystem.Norm("", 15);

				block = PaxSystem.Norm(s.Block, 5);

				if (s.Value == null)
				  value = "null";
				else
				  value = s.Value.ToString();

				kind = s.Kind.ToString();
				kind = PaxSystem.Norm(kind, 10);

				if (i == BR_id)
				{
					name = PaxSystem.Norm("#13#10", 10);
					value = "";
				}

				str = index + "|" +
							name + "|" +
							full_name + "|" +
							kind + "|" +
							type + "|" +
							level + "|" +
							block + "|" +
							value;

				t.WriteLine(str);
			}

			index = PaxSystem.Norm("index", 10);
			name = PaxSystem.Norm("name", 10);
			full_name = PaxSystem.Norm("full name", 25);
			type = PaxSystem.Norm("type", 15);
			level = PaxSystem.Norm("level", 10);
			block = PaxSystem.Norm("block", 5);
			kind = PaxSystem.Norm("kind", 10);
			value = PaxSystem.Norm("value", 10);

			str = index + "|" +
						 name + "|" +
						 full_name + "|" +
						 kind + "|" +
						 type + "|" +
						 level + "|" +
						 block + "|" +
						 value;

			t.WriteLine("");
			t.WriteLine(str);
			t.Close();
		#endif
		}

		/// <summary>
		/// Prints classes.
		/// </summary>
		public void DumpClasses(string FileName)
		{
		#if dump

			StreamWriter t = File.CreateText(FileName);

			for (int i=0; i<=Card; i++)
			{
				string index;
				
				SymbolRec s = (SymbolRec) a[i];
				if (s.Kind == MemberKind.Type)
				{
					if (s.Value == null)
						continue;
					t.WriteLine("");
					t.WriteLine("");
					t.WriteLine("");
					t.WriteLine("");
					ClassObject c = (ClassObject) s.Value;

					if (c.Class_Kind == ClassKind.Namespace)
						index = "Namespace [";
					else if (c.Class_Kind == ClassKind.Class)
						index = "Class [";
					else if (c.Class_Kind == ClassKind.Interface)
						index = "Interface [";
					else if (c.Class_Kind == ClassKind.Array)
						index = "Array [";
					else if (c.Class_Kind == ClassKind.Struct)
						index = "Struct [";
					else if (c.Class_Kind == ClassKind.Delegate)
						index = "Delegate [";
					else if (c.Class_Kind == ClassKind.Enum)
						index = "Enum [";
					else
						index = "Class [";
					index += PaxSystem.Norm(i, 6) + "]";

					t.Write(index + s.Name);

					for (int j = 0; j < c.Modifiers.Count; j++)
					{
						Modifier m = (Modifier)c.Modifiers[j];
						t.Write(" " + m.ToString());
					}
					if (c.OwnerId != 0)
					{
						t.WriteLine("");
						t.WriteLine("Owner: [" + c.OwnerId.ToString() + "]" + this[c.OwnerId].Name);
					}
					if (c.AncestorIds.Count > 0)
					{
						for (int k = 0; k < c.AncestorIds.Count; k++)
							t.WriteLine("Ancestor: [" + c.AncestorIds[k].ToString() + "]" + this[c.AncestorIds[k]].Name);
					}
					t.WriteLine("");
					t.WriteLine("Members:");
					for (int j = 0; j < c.Members.Count; j++)
					{
						MemberObject m = c.Members[j];
						index = "[" + PaxSystem.Norm(m.Id, 6) + "]";
						t.WriteLine("");

						string imp;
						if (m.Imported)
							imp = "true ";
						else
							imp = "false";

						t.Write(index + PaxSystem.Norm(this[m.Id].Name, 12) +
							" imported=" + imp +
							" kind=" + PaxSystem.Norm(this[m.Id].Kind.ToString(), 12) +
							" modifiers=");
						ModifierList modifiers = m.Modifiers;
						for (int k = 0; k < modifiers.Count; k++)
						{
							Modifier mm = modifiers[k];
							t.Write(" " + mm.ToString());
						}

						if ((m.Kind == MemberKind.Method) || (m.Kind == MemberKind.Constructor))
						{
							if ((m as FunctionObject).Method_Info == null)
							{
								t.WriteLine("");
								t.Write("Init = {0}", (m as FunctionObject).Init);
								t.WriteLine("");
								t.Write("Signature = {0}", (m as FunctionObject).Signature);
							}
						}
					}
				}
			}
			t.Close();

		#endif
		}
	}
	#endregion SymbolTable Class
}
