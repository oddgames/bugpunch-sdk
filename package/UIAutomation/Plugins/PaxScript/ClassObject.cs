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
using System.Reflection;
using System.Collections;
#if full
using System.Reflection.Emit;
#endif
using SL;

namespace PaxScript.Net
{
	#region ClassObject Class
	/// <summary>
	/// Represents script-defined type or namespace.
	/// </summary>
	internal class ClassObject: MemberObject
	{
		/// <summary>
		/// List of id of ancestors.
		/// </summary>
		internal IntegerList AncestorIds;

		/// <summary>
		/// Imported type.
		/// </summary>
		internal Type ImportedType;

		/// <summary>
		/// Pattern method (Kind.Delegate only)
		/// </summary>
		internal FunctionObject PatternMethod = null;

		/// <summary>
		/// Underlying type (Kind.Enum only)
		/// </summary>
		internal ClassObject UnderlyingType = null;

		/// <summary>
		/// Reflected type.
		/// </summary>
		internal Type RType = null;

		/// <summary>
		/// Kind of type (class, struct, enum etc).
		/// </summary>
		public ClassKind Class_Kind;

		int _namespaceNameIndex = -1;

		public int MinValueId = 0;
		public int MaxValueId = 0;
		public int RangeTypeId = 0;
		public int IndexTypeId = 0;

		private PaxHashTable ht = new PaxHashTable();

		/// <summary>
		/// Constructor.
		/// </summary>
		internal ClassObject(BaseScripter scripter,
							int class_id, int owner_id, ClassKind ck)
		: base(scripter, class_id, owner_id)
		{
			AncestorIds = new IntegerList(false);
			ImportedType = null;
			Class_Kind = ck;
			PatternMethod = null;
		}

		public int NamespaceNameIndex
		{
			get
			{
				if (_namespaceNameIndex == -1)
				{
					char c;
					string _namespace_name = PaxSystem.ExtractOwner(FullName, out c);
					_namespaceNameIndex = Scripter.names.Add(_namespace_name);
				}
				return _namespaceNameIndex;
			}
		}

#if full
		/// <summary>
		/// Creates reflected enum type (Kind.Enum only)
		/// </summary>
		internal void DefineReflectedEnum()
		{
			if (Imported)
			{
				RType = ImportedType;
				return;
			}

			RType = Scripter.FindAvailableType(FullName, false);
			if (RType != null)
				return;

			int u_id = UnderlyingType.Id;
			ClassObject u = Scripter.symbol_table[u_id].ValueAsClassObject;
			Type underlying_type = u.ImportedType;
			EnumBuilder enum_builder = Scripter.PaxModuleBuilder.DefineEnum(FullName,
				TypeAttributes.Public, underlying_type);

			for (int i = 0; i < Members.Count; i++)
			{
				MemberObject m = Members[i];

				if (m.Kind == MemberKind.Field)
				{
					int field_id = m.Id;
					string name = Scripter.symbol_table[field_id].Name;

					if (u_id == (int) StandardType.Bool)
						enum_builder.DefineLiteral(name, Scripter.symbol_table[field_id].ValueAsBool);
					else if (u_id == (int) StandardType.Byte)
						enum_builder.DefineLiteral(name, Scripter.symbol_table[field_id].ValueAsByte);
					else if (u_id == (int) StandardType.Char)
						enum_builder.DefineLiteral(name, Scripter.symbol_table[field_id].ValueAsChar);
					else if (u_id == (int) StandardType.Long)
						enum_builder.DefineLiteral(name, Scripter.symbol_table[field_id].ValueAsLong);
					else if (u_id == (int) StandardType.Int)
						enum_builder.DefineLiteral(name, Scripter.symbol_table[field_id].ValueAsInt);
					else if (u_id == (int) StandardType.Sbyte)
						enum_builder.DefineLiteral(name, (sbyte) Scripter.symbol_table[field_id].ValueAsByte);
					else if (u_id == (int) StandardType.Short)
						enum_builder.DefineLiteral(name, Scripter.symbol_table[field_id].ValueAsShort);
					else if (u_id == (int) StandardType.Uint)
						enum_builder.DefineLiteral(name, (uint) Scripter.symbol_table[field_id].ValueAsInt);
					else if (u_id == (int) StandardType.Ulong)
						enum_builder.DefineLiteral(name, (ulong) Scripter.symbol_table[field_id].ValueAsLong);
					else if (u_id == (int) StandardType.Ushort)
						enum_builder.DefineLiteral(name, (ushort) Scripter.symbol_table[field_id].ValueAsShort);
				}
			}
			RType = enum_builder.CreateType();
			Scripter.RegisterAvailableType(RType);
		}

		/// <summary>
		/// Creates reflected class type (Kind.Class only)
		/// </summary>
		internal void DefineReflectedClass()
		{
			if (Imported)
			{
				RType = ImportedType;
				return;
			}
			RType = Scripter.FindAvailableType(FullName, false);
			if (RType != null)
				return;
			TypeBuilder type_builder = Scripter.PaxModuleBuilder.DefineType(FullName,
					TypeAttributes.Class & TypeAttributes.Public);
			RType = type_builder.CreateType();
			Scripter.RegisterAvailableType(RType);
		}

		/// <summary>
		/// Creates reflected interface type (Kind.Interface only)
		/// </summary>
		internal void DefineReflectedInterface()
		{
			if (Imported)
			{
				RType = ImportedType;
				return;
			}
			RType = Scripter.FindAvailableType(FullName, false);
			if (RType != null)
				return;
			TypeBuilder type_builder = Scripter.PaxModuleBuilder.DefineType(FullName,
				TypeAttributes.Interface & TypeAttributes.Public);
			RType = type_builder.CreateType();
			Scripter.RegisterAvailableType(RType);
		}

		/// <summary>
		/// Creates reflected struct type (Kind.Struct only)
		/// </summary>
		internal void DefineReflectedStruct()
		{
			if (Imported)
			{
				RType = ImportedType;
				return;
			}
			RType = Scripter.FindAvailableType(FullName, false);
			if (RType != null)
				return;

			TypeBuilder type_builder = Scripter.PaxModuleBuilder.DefineType(FullName,
				TypeAttributes.Class & TypeAttributes.Public);

			RType = Scripter.FindAvailableType(type_builder.FullName, false);
			RType = type_builder.CreateType();
			Scripter.RegisterAvailableType(RType);
		}
#endif
		/// <summary>
		/// Returns list of id of supported interfaces
		/// </summary>
		internal IntegerList GetSupportedInterfaceListIds()
		{
			IntegerList result = new IntegerList(false);
			for (int i = 0; i < AncestorIds.Count; i++)
			{
				int ancestor_id = AncestorIds[i];
				ClassObject a = (ClassObject) Scripter.GetVal(ancestor_id);
				if (a.Class_Kind == ClassKind.Interface)
				{
					result.Add(ancestor_id);
					IntegerList l = a.GetSupportedInterfaceListIds();
					result.AddFrom(l);
				}
			}
			return result;
		}

		/// <summary>
		/// Returns 'true', if given type inherits from type a.
		/// </summary>
		public bool InheritsFrom(ClassObject a)
		{
			for (int i = 0; i < AncestorIds.Count; i++)
			{
				int ancestor_id = AncestorIds[i];
				if (a.Id == ancestor_id)
					return true;
				ClassObject v = Scripter.GetClassObject(ancestor_id);
				if (v.InheritsFrom(a))
					return true;
			}
			if ((a.Imported) && (ImportedType != null))
			{
                if (!a.IsInterface)
                {
                    if (ImportedType.IsSubclassOf(a.ImportedType))
                        return true;
                }
                else
                {
                    Type[] interfaces = ImportedType.GetInterfaces();
                    foreach (Type intf_type in interfaces)
                    {
                        if (intf_type == a.ImportedType)
                            return true;
                    }
                }
			}

			return false;
		}

		/// <summary>
		/// Returns 'true', if given type implements interface i.
		/// </summary>
		internal bool Implements(ClassObject i)
		{
			return InheritsFrom(i) && i.IsInterface;
		}

		/// <summary>
		/// Returns 'true', if given type is base class of class c.
		/// </summary>
		internal bool IsBaseClassOf(ClassObject c)
		{
			return this.InheritsFrom(c);
		}

		/// <summary>
		/// Creates instance of given type.
		/// </summary>
		internal ObjectObject CreateObject()
		{
			ObjectObject result = new ObjectObject(Scripter, this);

			ClassObject curr_class = this;

			for(;;)
			{
				for (int i = 0; i < curr_class.Members.Count; i++)
				{
					MemberObject m = curr_class.Members[i];

					if ((m.Kind == MemberKind.Field) && (!m.Static))
					{
						InstanceProperty p = new InstanceProperty(m, curr_class.Id);

						result.Properties.Add(p);

						if (m.Imported)
						{
							FieldObject f = (FieldObject) m;
							p.Field_Info = f.Field_Info;
						}
					}
				}

				ClassObject a = curr_class.AncestorClass;
				if (a != null)
					curr_class = a;
				else
					break;
			}

			return result;
		}

		/// <summary>
		/// Returns id of constructor.
		/// </summary>
		internal int FindConstructorId()
		{
			for (int i = 0; i < Members.Count; i++)
			{
				MemberObject m = Members[i];
				if (m.Kind == MemberKind.Constructor)
					return m.Id;
			}
			return 0;
		}

		/// <summary>
		/// Returns id of constructor.
		/// </summary>
		internal int FindConstructorId(int param_count)
		{
			for (int i = 0; i < Members.Count; i++)
			{
				MemberObject m = Members[i];
				if (m.Kind == MemberKind.Constructor)
				{
					FunctionObject f = Scripter.GetFunctionObject(m.Id);
					if (f.ParamCount == param_count)
					{
						return m.Id;
					}
				}
			}
			return 0;
		}

		/// <summary>
		/// Returns id of constructor.
		/// </summary>
		internal int FindConstructorId(IntegerList a, IntegerList param_mod,
									out FunctionObject best)
		{
			if (a == null)
				a = new IntegerList(false);
			if (param_mod == null)
				param_mod = new IntegerList(false);
			IntegerList applicable_list = new IntegerList(false);
			best = null;
			FindApplicableConstructorList(a, param_mod, ref best, ref applicable_list);
			CompressApplicableMethodList(a, applicable_list);
			if (applicable_list.Count >= 1)
				return applicable_list[0];
			else
				return 0;
		}

		/// <summary>
		/// Returns applicable constructor list.
		/// </summary>
		void FindApplicableConstructorList(IntegerList a, IntegerList param_mod,
								ref FunctionObject best, ref IntegerList applicable_list)
		{
			for (int i = 0; i < Members.Count; i++)
			{
				MemberObject m = Members[i];
				if ((m.Kind == MemberKind.Constructor) && (!m.HasModifier(Modifier.Static)))
				{
					FunctionObject f = (FunctionObject) m;
					AddApplicableMethod(f, a, param_mod, 0, ref best, ref applicable_list);
				}
			}

			ClassObject ancestor_class = AncestorClass;

			if (!Imported)
			{
				if ((ancestor_class != null) && (best == null))
					ancestor_class.FindApplicableConstructorList(a, param_mod, ref best, ref applicable_list);
				return;
			}

			IntegerList l = new IntegerList(false);

			ConstructorInfo[] constructors = ImportedType.GetConstructors();
			foreach (ConstructorInfo info in constructors)
				l.Add(Scripter.symbol_table.RegisterConstructor(info, Id));

			if (Scripter.SearchProtected)
			{
				constructors = ImportedType.GetConstructors(Scripter.protected_binding_flags);
				foreach (ConstructorInfo info in constructors)
					l.Add(Scripter.symbol_table.RegisterConstructor(info, Id));
			}

			for (int i = 0; i < l.Count; i++)
			{
				FunctionObject f = Scripter.GetFunctionObject(l[i]);
				AddApplicableMethod(f, a, param_mod, 0, ref best, ref applicable_list);
			}

			if ((ancestor_class != null) && (best == null))
				ancestor_class.FindApplicableConstructorList(a, param_mod, ref best, ref applicable_list);
		}

		/// <summary>
		/// Returns destructor id.
		/// </summary>
		internal int FindDestructorId(IntegerList a)
		{
			for (int i = 0; i < Members.Count; i++)
			{
				MemberObject m = Members[i];
				if (m.Kind == MemberKind.Destructor)
					return m.Id;
			}
			return 0;
		}

		internal bool HasMethod(int name_index, int param_count)
		{
			for (int i = 0; i < Members.Count; i++)
			{
				MemberObject m = Members[i];
				if (m.Kind == MemberKind.Method)
				{
					FunctionObject f = m as FunctionObject;
					if (f.ParamCount == param_count)
					{
						return true;
					}
				}
			}

			ClassObject ancestor_class = AncestorClass;

			if (!Imported)
			{
				if (ancestor_class != null)
					return ancestor_class.HasMethod(name_index, param_count);
			}

			string name = Scripter.names[name_index];

			MethodInfo[] methods;
			methods = ImportedType.GetMethods(Scripter.public_binding_flags);
			foreach (MethodInfo info in methods)
			{
				if (PaxSystem.CompareStrings(name, info.Name, true))
				{
					ParameterInfo[] parameters = info.GetParameters();
					if (parameters.Length == param_count)
						return true;
				}
			}

			if (Scripter.SearchProtected)
			{
				methods = ImportedType.GetMethods(Scripter.protected_binding_flags);
				foreach (MethodInfo info in methods)
				{
					if (PaxSystem.CompareStrings(name, info.Name, true))
					{
						ParameterInfo[] parameters = info.GetParameters();
						if (parameters.Length == param_count)
							return true;
					}
				}
			}

			return false;
		}

		/// <summary>
		/// Returns method id.
		/// </summary>
		internal int FindMethodId(string name,
						IntegerList a,
						IntegerList param_mod,
						int res_id,
						out FunctionObject best,
						bool upcase)
		{
			int name_index = Scripter.names.Add(name);
			return FindMethodId(name_index, a, param_mod, res_id, out best, upcase);
		}

		/// <summary>
		/// Returns method id.
		/// </summary>
		internal int FindMethodId(int name_index,
						IntegerList a,
						IntegerList param_mod,
						int res_id,
						out FunctionObject best,
						bool upcase)
		{
			if (a == null)
				a = new IntegerList(false);
			if (param_mod == null)
				param_mod = new IntegerList(false);
			IntegerList applicable_list = new IntegerList(false);
			best = null;
			FindApplicableMethodList(name_index, a, param_mod, res_id, ref best, ref applicable_list, upcase);
			if (applicable_list.Count > 1)
				CompressApplicableMethodList(a, applicable_list);
			if (applicable_list.Count >= 1)
			{
				int sub_id = applicable_list[0];
				best = Scripter.GetFunctionObject(sub_id);
				return sub_id;
			}
			return 0;
		}

		/// <summary>
		/// Compresses applicable method list.
		/// </summary>
		internal void CompressApplicableMethodList(IntegerList a, IntegerList applicable_list)
		{
			while (applicable_list.Count >= 2)
			{
				int p_id = applicable_list[0];
				FunctionObject p = Scripter.GetFunctionObject(p_id);

				bool cannot_compress = true;

				for (int i = 1; i < applicable_list.Count; i++)
				{
					int q_id = applicable_list[i];
					FunctionObject q = Scripter.GetFunctionObject(q_id);
					int n_p = 0;
					int n_q = 0;
					for (int j = 0; j < a.Count; j++)
					{
						int actual_id = a[j];
						int	p_formal_id = p.GetParamId(j);
						int q_formal_id = q.GetParamId(j);

						int val = Scripter.conversion.CompareConversions(Scripter, actual_id,
							p_formal_id, q_formal_id);
						if (val > 0)
							n_p ++;
						else if (val < 0)
							n_q ++;
					}

					if ((n_p > 0) && (n_q == 0))
					{
						// p-member is better
						cannot_compress = false;
						applicable_list.DeleteValue(q_id);
						break;
					}
					else if ((n_q > 0) && (n_p == 0))
					{
						// q-member is better
						cannot_compress = false;
						applicable_list.DeleteValue(p_id);
						break;
					}
				}
				if (cannot_compress)
					break;
			}
		}

		/// <summary>
		/// Adds method to applicable method list.
		/// </summary>
		internal void AddApplicableMethod(FunctionObject f,
								 IntegerList a,
								 IntegerList param_mod,
								 int res_id,
								 ref FunctionObject best,
								 ref IntegerList applicable_list)
		{
			if (best == null)
				best = f;
			if (f.ParamCount > a.Count)
				return;
			if ((f.ParamCount < a.Count) && (f.ParamsId == 0))
				return;
			if ((res_id != 0) && (!Scripter.MatchAssignment(f.ResultId, res_id)))
				return;
			best = f;
			if (f.ParamCount == 0)
				applicable_list.Add(f.Id);
			else
			{
				bool ok = true;
				for (int j = 0; j < a.Count; j ++)
				{
					int actual_id = a[j];
					int formal_id = f.GetParamId(j);
					ok = (int)f.GetParamMod(j) == param_mod[j];

					if (!ok)
						if (!f.Imported)
						{
							if (Scripter.code.GetLanguage(Scripter.code.n) == PaxLanguage.VB)
								ok = true;
							else
								break;
						}

					ok = Scripter.MatchAssignment(formal_id, actual_id);
					if (!ok)
					{
						if (Scripter.code.GetLanguage(Scripter.code.n) == PaxLanguage.VB)
						{

							int formal_type_id = Scripter.symbol_table[formal_id].TypeId;
							int actual_type_id = Scripter.symbol_table[actual_id].TypeId;
							ClassObject c1 = Scripter.GetClassObject(formal_type_id);
							ClassObject c2 = Scripter.GetClassObject(actual_type_id);
							ok = Scripter.MatchTypes(c1, c2);
						}

						if (!ok)
							break;
					}
				}
				if (ok)
					applicable_list.Add(f.Id);
			}
		}

		/// <summary>
		/// Returns applicable method list.
		/// </summary>
		void FindApplicableMethodList(int name_index,
								IntegerList a,
								IntegerList param_mod,
								int res_id,
								ref FunctionObject best,
								ref IntegerList applicable_list,
								bool upcase)
		{

            string upcase_name = Scripter.GetUpcaseNameByNameIndex(name_index);

			for (int i = 0; i < Members.Count; i++)
			{
				MemberObject m = Members[i];
				if (m.Kind == MemberKind.Method)
				{
					bool ok;
					if (upcase)
						ok = upcase_name == m.UpcaseName;
					else
						ok = m.NameIndex == name_index;

					if (ok)
					{
						FunctionObject f = (FunctionObject) m;
						AddApplicableMethod(f, a, param_mod, res_id, ref best, ref applicable_list);
					}
				}
			}

			ClassObject ancestor_class = AncestorClass;

			if (!Imported)
			{
				if (ancestor_class != null)
					ancestor_class.FindApplicableMethodList(name_index, a, param_mod, res_id, ref best, ref applicable_list, upcase);
				return;
			}

			string name = Scripter.names[name_index];

			IntegerList l = new IntegerList(false);

			MethodInfo[] methods = ImportedType.GetMethods(Scripter.public_binding_flags);

			foreach (MethodInfo info in methods)
			{
				if (name == info.Name)
				{
					bool ok = true;
					Attribute[] attrs = Attribute.GetCustomAttributes(info);
					foreach (Attribute attr in attrs)
					{
						if (attr is PaxScriptForbid)
							ok = false;
					}

					if (ok)
					{
						int temp = Scripter.symbol_table.RegisterMethod(info, Id);
						l.Add(temp);
					}
				}
			}

			if (Scripter.SearchProtected)
			{
				methods = ImportedType.GetMethods(Scripter.protected_binding_flags);
				foreach (MethodInfo info in methods)
				{
					if (name == info.Name)
					{
						bool ok = true;
						Attribute[] attrs = Attribute.GetCustomAttributes(info);
						foreach (Attribute attr in attrs)
						{
							if (attr is PaxScriptForbid)
								ok = false;
						}

						if (ok)
						{
							int temp = Scripter.symbol_table.RegisterMethod(info, Id);
							l.Add(temp);
						}
					}
				}
			}

			for (int i = 0; i < l.Count; i++)
			{
				FunctionObject f = Scripter.GetFunctionObject(l[i]);
				AddApplicableMethod(f, a, param_mod, res_id, ref best, ref applicable_list);
			}

			if (ancestor_class != null)
				ancestor_class.FindApplicableMethodList(name_index, a,
					param_mod, res_id, ref best, ref applicable_list, upcase);

			for (int i = 0; i < AncestorIds.Count; i++)
			{
				ClassObject c = Scripter.GetClassObject(AncestorIds[i]);
				if (c.IsInterface)
					c.FindApplicableMethodList(name_index, a,
						param_mod, res_id, ref best, ref applicable_list, upcase);
			}
		}

		/// <summary>
		/// Returns id of overloadable unary operator.
		/// </summary>
		internal int FindOverloadableUnaryOperatorId(string operator_name, int arg1)
		{
			if (Id <= (int) StandardType.Object)
				return 0;

			IntegerList applicable_list = new IntegerList(false);
			FunctionObject best = null;
			IntegerList a = new IntegerList(true);
			a.Add(arg1);
			IntegerList param_mod = new IntegerList(true);
			param_mod.Add((int) ParamMod.None);
			int name_index = Scripter.names.Add(operator_name);
			FindApplicableMethodList(name_index, a, param_mod, 0, ref best, ref applicable_list, false);
			CompressApplicableMethodList(a, applicable_list);
			if (applicable_list.Count >= 1)
				return applicable_list[0];
			else
				return 0;
		}

		/// <summary>
		/// Returns id of overloadable binary operator.
		/// </summary>
		internal int FindOverloadableBinaryOperatorId(string operator_name, int arg1, int arg2)
		{
			if (Id <= (int) StandardType.Object)
				return 0;

			IntegerList applicable_list = new IntegerList(false);
			FunctionObject best = null;
			IntegerList a = new IntegerList(true);
			a.Add(arg1);
			a.Add(arg2);
			IntegerList param_mod = new IntegerList(true);
			param_mod.Add((int) ParamMod.None);
			param_mod.Add((int) ParamMod.None);
			int name_index = Scripter.names.Add(operator_name);
			FindApplicableMethodList(name_index, a, param_mod, 0, ref best, ref applicable_list, false);
			CompressApplicableMethodList(a, applicable_list);
			if (applicable_list.Count >= 1)
				return applicable_list[0];
			else
				return 0;
		}

		/// <summary>
		/// Returns method id.
		/// </summary>
		internal int FindOverloadableImplicitOperatorId(int actual_id,	int res_id)
		{
			FunctionObject best;
			bool upcase = false;
			int name_index = Scripter.names.Add("op_Implicit");
			IntegerList a = new IntegerList(false);
			a.Add(actual_id);
			IntegerList param_mod = new IntegerList(false);
			param_mod.Add(0);
			IntegerList applicable_list = new IntegerList(false);
			best = null;
			FindApplicableMethodList(name_index, a, param_mod, res_id, ref best, ref applicable_list, upcase);
			CompressApplicableMethodList(a, applicable_list);
			if (applicable_list.Count >= 1)
			{
				int sub_id = applicable_list[0];
				best = Scripter.GetFunctionObject(sub_id);
				return sub_id;
			}
			return 0;
		}

		/// <summary>
		/// Returns id of overloadable explicit operator.
		/// </summary>
		internal int FindOverloadableExplicitOperatorId(int dest_id)
		{
			for (int i = 0; i < Members.Count; i++)
			{
				MemberObject m = Members[i];
				if (m.Kind == MemberKind.Method)
					if (m.Name == "op_Explicit")
					{
						FunctionObject f = (FunctionObject) m;
						if (f.ParamCount != 1)
							continue;

						if (Scripter.MatchAssignment(f.ResultId, dest_id))
							return f.Id;
					}
			}
			if (!Imported)
				return 0;

			MethodInfo[] methods = ImportedType.GetMethods(Scripter.public_binding_flags);
			foreach (MethodInfo info in methods)
			{
				if ("op_Explicit" == info.Name)
				{
					bool ok = true;
					Attribute[] attrs = Attribute.GetCustomAttributes(info);
					foreach (Attribute attr in attrs)
					{
						if (attr is PaxScriptForbid)
						{
							ok = false;
							break;
						}
					}

					if (ok)
					{
						int sub_id = Scripter.symbol_table.RegisterMethod(info, Id);
						FunctionObject f = (FunctionObject) Scripter.symbol_table[sub_id].Value;
						if (f.ParamCount != 1)
							continue;

						if (Scripter.MatchAssignment(f.ResultId, dest_id))
							return f.Id;
					}
				}
			}

			return 0;
		}

		/// <summary>
		/// Returns class member by name index.
		/// </summary>
		internal override MemberObject GetMemberByNameIndex(int name_index, bool upcase)
		{
			MemberObject m;

			m = (MemberObject) ht[name_index];
			if (m != null)
				return m;

			string name;
			m = base.GetMemberByNameIndex(name_index, upcase);

			if (m != null)
				goto fin;

			if (!Imported)
			{
				ClassObject a = AncestorClass;
				if (a == null)
					return null;
				else
				{
					m = a.GetMemberByNameIndex(name_index, upcase);
                	goto fin;
				}
			}

			if(ImportedType == null)
				return null;

			name = Scripter.names[name_index];
			string upcase_name = null;
			if (upcase)
				upcase_name = Scripter.GetUpcaseNameByNameIndex(name_index);

			ConstructorInfo[] constructors = ImportedType.GetConstructors();
			foreach (ConstructorInfo info in constructors)
			{
				bool ok;
				if (upcase)
					ok = upcase_name == UpcaseName;
				else
					ok = name == Name;

				if (ok)
				{
					Attribute[] attrs = Attribute.GetCustomAttributes(info);
					foreach (Attribute attr in attrs)
					{
						if (attr is PaxScriptForbid)
							ok = false;
					}

					if (ok)
					{
						int sub_id = Scripter.symbol_table.RegisterConstructor(info, Id);
						m = (MemberObject) Scripter.symbol_table[sub_id].Value;
					}
				}
			}

			MethodInfo[] methods = ImportedType.GetMethods(Scripter.public_binding_flags);

            foreach (MethodInfo info in methods)
			{

				bool ok;
				if (upcase)
					ok = upcase_name == info.Name.ToUpper();
				else
					ok = name == info.Name;


                if (ok)
				{
					Attribute[] attrs = Attribute.GetCustomAttributes(info);
					foreach (Attribute attr in attrs)
					{
						if (attr is PaxScriptForbid)
							ok = false;
					}

					if (ok)
                    {
                        
                        int sub_id = Scripter.symbol_table.RegisterMethod(info, Id);
						m = (MemberObject) Scripter.symbol_table[sub_id].Value;
					}
				}
			}

			FieldInfo[] fields = ImportedType.GetFields(Scripter.public_binding_flags);
			foreach (FieldInfo info in fields)
			{
				bool ok;
				if (upcase)
					ok = upcase_name == info.Name.ToUpper();
				else
					ok = name == info.Name;

				if (ok)
				{
					Attribute[] attrs = Attribute.GetCustomAttributes(info);
					foreach (Attribute attr in attrs)
					{
						if (attr is PaxScriptForbid)
							ok = false;
					}

					if (ok)
					{
						int field_id = Scripter.symbol_table.RegisterField(info, Id);
						m = (MemberObject) Scripter.symbol_table[field_id].Val;
					}
				}
			}

			PropertyInfo[] properties = ImportedType.GetProperties(Scripter.public_binding_flags);
			foreach (PropertyInfo info in properties)
			{
				bool ok;
				if (upcase)
					ok = upcase_name == info.Name.ToUpper();
				else
					ok = name == info.Name;

				if (ok)
				{
					Attribute[] attrs = Attribute.GetCustomAttributes(info);
					foreach (Attribute attr in attrs)
					{
						if (attr is PaxScriptForbid)
							ok = false;
					}

					if (ok)
					{
						int property_id = Scripter.symbol_table.RegisterProperty(info, Id);
						m = (MemberObject) Scripter.symbol_table[property_id].Val;
					}
				}
			}

			EventInfo[] events = ImportedType.GetEvents(Scripter.public_binding_flags);
			foreach (EventInfo info in events)
			{
				bool ok;
				if (upcase)
					ok = upcase_name == info.Name.ToUpper();
				else
					ok = name == info.Name;

				if (ok)
				{
					Attribute[] attrs = Attribute.GetCustomAttributes(info);
					foreach (Attribute attr in attrs)
					{
						if (attr is PaxScriptForbid)
							ok = false;
					}

					if (ok)
					{
						int event_id = Scripter.symbol_table.RegisterEvent(info, Id);
						m = (MemberObject) Scripter.symbol_table[event_id].Val;
					}
				}
			}

			if (m == null && Scripter.SearchProtected)
			{
				methods = ImportedType.GetMethods(Scripter.protected_binding_flags);

				foreach (MethodInfo info in methods)
				{
					bool ok;
					if (upcase)
						ok = upcase_name == info.Name.ToUpper();
					else
						ok = name == info.Name;

					if (ok)
					{
						int sub_id = Scripter.symbol_table.RegisterMethod(info, Id);
						m = (MemberObject) Scripter.symbol_table[sub_id].Value;
					}
				}

				fields = ImportedType.GetFields(Scripter.protected_binding_flags);
				foreach (FieldInfo info in fields)
				{
					bool ok;
					if (upcase)
						ok = upcase_name == info.Name.ToUpper();
					else
						ok = name == info.Name;

					if (ok)
					{
						Attribute[] attrs = Attribute.GetCustomAttributes(info);
						foreach (Attribute attr in attrs)
						{
							if (attr is PaxScriptForbid)
								ok = false;
						}

						if (ok)
						{
							int field_id = Scripter.symbol_table.RegisterField(info, Id);
							m = (MemberObject) Scripter.symbol_table[field_id].Val;
						}
					}
				}

				properties = ImportedType.GetProperties(Scripter.protected_binding_flags);
				foreach (PropertyInfo info in properties)
				{
					bool ok;
					if (upcase)
						ok = upcase_name == info.Name.ToUpper();
					else
						ok = name == info.Name;

					if (ok)
					{
						Attribute[] attrs = Attribute.GetCustomAttributes(info);
						foreach (Attribute attr in attrs)
						{
							if (attr is PaxScriptForbid)
								ok = false;
						}

						if (ok)
						{
							int property_id = Scripter.symbol_table.RegisterProperty(info, Id);
							m = (MemberObject) Scripter.symbol_table[property_id].Val;
						}
					}
				}
			}

			if (m == null)
			{
				Type[] interfaces = ImportedType.GetInterfaces();
				foreach (Type t in interfaces)
				{
					int type_id = Scripter.symbol_table.RegisterType(t, false);
					if (AncestorIds.IndexOf(type_id) == -1)
						AncestorIds.Add(type_id);
				}
			}

			if (m == null)
			{
				ClassObject a = AncestorClass;
				if (a != null)
					m = a.GetMemberByNameIndex(name_index, upcase);
				if (m == null)
				{
					for (int i = 0; i < AncestorIds.Count; i++)
					{
						ClassObject c = Scripter.GetClassObject(AncestorIds[i]);
						if (c.IsInterface)
						{
							m = c.GetMemberByNameIndex(name_index, upcase);
							if (m != null)
								break;
						}
					}
				}
			}

			fin:

			if (m != null)
			{
				ht.Add(name_index, m);
			}

			return m;
		}

		/// <summary>
		/// Returns indexer.
		/// </summary>
		internal PropertyObject FindIndexer()
		{
			for (int i = 0; i < Members.Count; i++)
			{
				MemberObject m = Members[i];
				if (m.Kind == MemberKind.Property)
				{
					PropertyObject p = (PropertyObject) m;
					if (p.IsIndexer)
					{
						return p;
					}
				}
			}

			PropertyInfo[] properties = ImportedType.GetProperties(Scripter.public_binding_flags);
			foreach (PropertyInfo info in properties)
			{
				if (info.Name == "Item")
				{
					bool ok = true;
					Attribute[] attrs = Attribute.GetCustomAttributes(info);
					foreach (Attribute attr in attrs)
					{
						if (attr is PaxScriptForbid)
							ok = false;
					}

					if (ok)
					{
						int property_id = Scripter.symbol_table.RegisterProperty(info, Id);
						PropertyObject p = (PropertyObject) Scripter.symbol_table[property_id].Val;
						return p;
					}
				}
			}

			if (Scripter.SearchProtected)
			{
				properties = ImportedType.GetProperties(Scripter.protected_binding_flags);
				foreach (PropertyInfo info in properties)
				{
					if (info.Name == "Item")
					{
						bool ok = true;
						Attribute[] attrs = Attribute.GetCustomAttributes(info);
						foreach (Attribute attr in attrs)
						{
							if (attr is PaxScriptForbid)
								ok = false;
						}

						if (ok)
						{
							int property_id = Scripter.symbol_table.RegisterProperty(info, Id);
							PropertyObject p = (PropertyObject) Scripter.symbol_table[property_id].Val;
							return p;
						}
					}
				}
			}

			return null;
		}

		/// <summary>
		/// Undocumented.
		/// </summary>
		internal bool IsOuterMemberId(int id)
		{
			if (OwnerClass == null)
				return false;
			else
			{
				MemberObject m = OwnerClass.GetMember(id);
				if (m == null)
					return OwnerClass.IsOuterMemberId(id);
				else
					return true;
			}
		}

		/// <summary>
		///  Returns owner of the given type.
		/// </summary>
		internal ClassObject OwnerClass
		{
			get
			{
				if (OwnerId == 0)
					return null;
				else
					return Scripter.GetClassObject(OwnerId);
			}
		}

		/// <summary>
		///  Returns 'true', if given type is abstract.
		/// </summary>
		internal bool Abstract
		{
			get
			{
				return HasModifier(Modifier.Abstract);
			}
		}

		/// <summary>
		///  Returns 'true', if given type is sealed.
		/// </summary>
		internal bool Sealed
		{
			get
			{
				return HasModifier(Modifier.Sealed);
			}
		}

		/// <summary>
		///  Returns 'true', if given object is namespace.
		/// </summary>
		internal bool IsNamespace
		{
			get
			{
				return Class_Kind == ClassKind.Namespace;
			}
		}

		/// <summary>
		///  Returns 'true', if given type is enumeration.
		/// </summary>
		internal bool IsEnum
		{
			get
			{
				return Class_Kind == ClassKind.Enum;
			}
		}

		/// <summary>
		///  Returns 'true', if given type is struct.
		/// </summary>
		internal bool IsStruct
		{
			get
			{
				return Class_Kind == ClassKind.Struct;
			}
		}

		/// <summary>
		///  Returns 'true', if given type is array.
		/// </summary>
		internal bool IsArray
		{
			get
			{
				return Class_Kind == ClassKind.Array;
			}
		}

		/// <summary>
		///  Returns 'true', if given type is array.
		/// </summary>
		internal bool IsPascalArray
		{
			get
			{
				return RangeTypeId != 0 && IndexTypeId != 0;
			}
		}

		/// <summary>
		///  Returns 'true', if given type is delegate.
		/// </summary>
		internal bool IsDelegate
		{
			get
			{
				return Class_Kind == ClassKind.Delegate;
			}
		}

		/// <summary>
		///  Returns 'true', if given type is class.
		/// </summary>
		internal bool IsClass
		{
			get
			{
				return Class_Kind == ClassKind.Class;
			}
		}

		/// <summary>
		///  Returns 'true', if given type is interface.
		/// </summary>
		internal bool IsInterface
		{
			get
			{
				return Class_Kind == ClassKind.Interface;
			}
		}

		/// <summary>
		///  Returns 'true', if given type is subrange.
		/// </summary>
		internal bool IsSubrange
		{
			get
			{
				return Class_Kind == ClassKind.Subrange;
			}
		}

		/// <summary>
		///  Returns 'true', if given type is struct.
		/// </summary>
		internal bool IsValueType
		{
			get
			{
				return IsStruct;
			}
		}

		/// <summary>
		///  Returns 'true', if given type is reference type.
		/// </summary>
		internal bool IsReferenceType
		{
			get
			{
				return !IsStruct;
			}
		}

		/// <summary>
		///  Returns 'true', if given type is type reference.
		/// </summary>
		internal bool IsRefType
		{
			get
			{
				return PaxSystem.PosCh('&', Name) == Name.Length - 1;
			}
		}

		/// <summary>
		///  Returns namespace name which contains given type.
		/// </summary>
		string NamespaceName
		{
			get
			{
				char c;
				return PaxSystem.ExtractOwner(FullName, out c);
			}
		}

		/// <summary>
		///  Returns ancestor class of given type.
		/// </summary>
		internal ClassObject AncestorClass
		{
			get
			{
				for (int i = 0; i < AncestorIds.Count; i++)
				{
					int ancestor_id = AncestorIds[i];
					ClassObject a = Scripter.GetClassObject(ancestor_id);
					if (a.Class_Kind == ClassKind.Class)
					{
						return a;
					}
				}
				return null;
			}
		}

		/// <summary>
		///  Returns default property of the class.
		/// </summary>
		internal PropertyObject DefaultProperty
		{
			get
			{
				for (int i = 0; i < Members.Count; i++)
				{
					MemberObject m = Members[i];

					if ((m.Kind == MemberKind.Property) && (!m.Static))
					{
						PropertyObject p = m as PropertyObject;
						if (p.IsDefault)
							return p;
					}
				}

				return null;
			}
		}

		internal int MinValue
		{
			get
			{
				if (IsSubrange)
					return Scripter.symbol_table[MinValueId].ValueAsInt;
				else
					return 0;
			}
		}

		internal int MaxValue
		{
			get
			{
				if (IsSubrange)
					return Scripter.symbol_table[MaxValueId].ValueAsInt;
				else
					return 0;
			}
		}

	}
	#endregion ClassObject Class
}
