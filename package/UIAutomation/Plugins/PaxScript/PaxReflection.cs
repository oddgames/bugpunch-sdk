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
	#region Reflection Namespace
	/// <summary>
	/// This namespace contains classes that allows you to
	/// create script-defined objects at host side at run-time and
	/// to create code explorer tree.
	/// </summary>
	namespace Reflection
	{
		#region PaxObject Class
		/// <summary>
		/// This class represents script-defined objects at run-time.
		/// </summary>
		public class PaxObject
		{
			/// <summary>
			/// Internal field that contains low-level representation of
			/// script-defined object.
			/// </summary>
			internal ObjectObject o;

			/// <summary>
			/// Internal field that contains low-level representation of
			/// script-defined type.
			/// </summary>
			internal PaxType t;

			/// <summary>
			/// Returns type of object.
			/// </summary>
			public PaxType GetPaxType()
			{
				return t;
			}

			/// <summary>
			/// Creates a clone of object.
			/// </summary>
			public PaxObject Clone()
			{
				PaxObject result = new PaxObject();
				result.o = o.Clone();
				result.t = t;
				return result;
			}

			/// <summary>
			/// Returns 'true', if object has property 'PropName'.
			/// </summary>
			public bool HasProperty(string PropName)
			{
				PaxMemberInfo mi = t.LookupMember(PropName);
				return (mi != null || mi.Id != 0);
			}

			/// <summary>
			/// Assigns value of property.
			/// </summary>
			public void PutProperty(string PropName, object value)
			{
				int name_index = t.m.Scripter.names.Add(PropName);
				int type_id = t.Id;


				if (o.HasProperty(name_index))
					o.PutProperty(name_index, type_id, value);
				else
				{
					Invoke("set_" + PropName, value);
					return;
				}
			}

			/// <summary>
			/// Returns value of property.
			/// </summary>
			public object GetProperty(string PropName)
			{

				int name_index = t.m.Scripter.names.Add(PropName);
				int type_id = t.Id;

				object result;

				if (o.HasProperty(name_index))
					result = o.GetProperty(name_index, type_id);
				else
				{
					result = Invoke("get_" + PropName);
				}

				return result;
			}

			/// <summary>
			/// Invokes method of object.
			/// </summary>
			public object Invoke(string method_name, params object[] parameters)
			{
				PaxMemberInfo mi = t.LookupMember(method_name);
				if (mi == null || mi.Id == 0)
				{
					Errors.RaiseExceptionEx(Errors.PAX0005, method_name);
					return null;
				}
				else
				{
					object result = o.Scripter.code.CallMethodEx(RunMode.Run, o,
														mi.Id, parameters);
					if (result is ObjectObject)
						result = (result as ObjectObject).Instance;

					return result;
				}
			}
		}
		#endregion PaxObject Class

		#region PaxMemberInfo Class
		/// <summary>
		/// The PaxMemberInfo class is the base class of the classes
		/// used to obtain information for all members of a class
		/// (constructors, events, fields, methods, and properties).
		/// </summary>
		public class PaxMemberInfo
		{
			/// <summary>
			/// Internal field that contains low-level representation of
			/// member information.
			/// </summary>
			internal MemberObject m;

			/// <summary>
			/// Internal method that returns list of Id of members.
			/// </summary>
			internal IntegerList GetClassKindIds(ClassKind ck)
			{
				IntegerList a = new IntegerList(false);
				SymbolTable symbol_table = m.Scripter.symbol_table;
				for (int i = 1; i < symbol_table.Card; i++)
				{
					if (symbol_table[i].Kind == MemberKind.Type && symbol_table[i].Level == m.Id)
					{
						MemberObject mm = m.Scripter.GetMemberObject(i);
						if (mm is ClassObject)
						{
							ClassObject c = mm as ClassObject;
							if (c.Class_Kind == ck)
								a.Add(c.Id);
						}
					}
				}
				return a;
			}

			/// <summary>
			/// Internal method that returns list of Id of members.
			/// </summary>
			internal IntegerList GetMemberKindIds(MemberKind mk)
			{
				IntegerList a = new IntegerList(false);
				SymbolTable symbol_table = m.Scripter.symbol_table;
				for (int i = m.Id; i < symbol_table.Card; i++)
				{
					if (symbol_table[i].Kind == mk && symbol_table[i].Level == m.Id)
					{
						MemberObject mm = m.Scripter.GetMemberObject(i);
						a.Add(mm.Id);
					}
				}
				return a;
			}

			/// <summary>
			/// Returns PaxMemberInfo object by name.
			/// </summary>
			public PaxMemberInfo LookupMember(string name)
			{
				int id = m.Scripter.symbol_table.GetMemberId(m.Id, name);

                if (id <= 0)
                    return null;

				PaxMemberInfo result;
				MemberObject mo = m.Scripter.GetMemberObject(id);

				if (mo is ClassObject)
				{
					result = new PaxType();
				}
				else if (mo is FunctionObject)
					result = new PaxMethodInfo();
				else if (mo is PropertyObject)
					result = new PaxPropertyInfo();
				else if (mo is FieldObject)
					result = new PaxFieldInfo();
				else if (mo is EventObject)
					result = new PaxEventInfo();
				else
					result = null;

				if (result != null)
					result.m = mo;

				return result;
			}

			/// <summary>
			/// Returns name of member.
			/// </summary>
			public string Name
			{
				get
				{
					return m.Name;
				}
			}

			/// <summary>
			/// Returns full name of member.
			/// </summary>
			public string FullName
			{
				get
				{
					return m.FullName;
				}
			}

			/// <summary>
			/// Returns id of member.
			/// </summary>
			public int Id
			{
				get
				{
					return m.Id;
				}
			}

			/// <summary>
			/// Returns 'true', if member is public.
			/// </summary>
			public bool IsPublic
			{
				get
				{
					return m.Public;
				}
			}

			/// <summary>
			/// Returns 'true', if member is protected.
			/// </summary>
			public bool IsProtected
			{
				get
				{
					return m.Protected;
				}
			}

			/// <summary>
			/// Returns 'true', if member is private.
			/// </summary>
			public bool IsPrivate
			{
				get
				{
					return m.Private;
				}
			}

			/// <summary>
			/// Returns 'true', if member is static.
			/// </summary>
			public bool IsStatic
			{
				get
				{
					return m.Static;
				}
			}
		}
		#endregion PaxMemberInfo Class

		#region PaxType Class
		/// <summary>
		/// Represents a type in a script.
		/// </summary>
		public class PaxType: PaxMemberInfo
		{
			/// <summary>
			/// Creates a script-defined object.
			/// </summary>
			public PaxObject CreateObject(params object[] p)
			{
				ClassObject c = m as ClassObject;
				ObjectObject o = c.CreateObject();
				PaxObject result = new PaxObject();
				result.o = o;
				result.t = this;

				int constructor_id = c.FindConstructorId(p.Length);
				if (constructor_id != 0)
				{
					c.Scripter.code.CallMethodEx(RunMode.Run, o, constructor_id, p);
				}

				return result;
			}

			/// <summary>
			/// Returns 'true', if type is host-defined.
			/// </summary>
			public bool Imported
			{
				get
				{
					return (m as ClassObject).Imported;
				}
			}

			/// <summary>
			/// Returns 'true', if this is a class type.
			/// </summary>
			public bool IsClass
			{
				get
				{
					return (m as ClassObject).Class_Kind == ClassKind.Class;
				}
			}

			/// <summary>
			/// Returns 'true', if this is a structure type.
			/// </summary>
			public bool IsStruct
			{
				get
				{
					return (m as ClassObject).Class_Kind == ClassKind.Struct;
				}
			}

			/// <summary>
			/// Returns 'true', if this is a enumeration type.
			/// </summary>
			public bool IsEnum
			{
				get
				{
					return (m as ClassObject).Class_Kind == ClassKind.Enum;
				}
			}

			/// <summary>
			/// Returns 'true', if this is an array type.
			/// </summary>
			public bool IsArray
			{
				get
				{
					return (m as ClassObject).Class_Kind == ClassKind.Array;
				}
			}

			/// <summary>
			/// Returns 'true', if this is a delegate type.
			/// </summary>
			public bool IsDelegate
			{
				get
				{
					return (m as ClassObject).Class_Kind == ClassKind.Delegate;
				}
			}

			/// <summary>
			/// Returns 'true', if this is an interface type.
			/// </summary>
			public bool IsInterface
			{
				get
				{
					return (m as ClassObject).Class_Kind == ClassKind.Interface;
				}
			}

			/// <summary>
			/// Returns array of nested namepaces. (The namespace in the
			/// paxScript representation is just a static class type).
			/// </summary>
			public virtual PaxNamespaceInfo[] GetNamespaces()
			{
				IntegerList a = GetClassKindIds(ClassKind.Namespace);
				PaxNamespaceInfo[] result = new PaxNamespaceInfo[a.Count];
				for (int i = 0; i < result.Length; i++)
				{
					PaxNamespaceInfo info = new PaxNamespaceInfo();
					info.m = m.Scripter.GetMemberObject(a[i]);
					result[i] = info;
				}
				return result;
			}

			/// <summary>
			/// Returns array of nested class types.
			/// </summary>
			public PaxType[] GetClasses()
			{
				IntegerList a = GetClassKindIds(ClassKind.Class);
				PaxType[] result = new PaxType[a.Count];
				for (int i = 0; i < result.Length; i++)
				{
					PaxType info = new PaxType();
					info.m = m.Scripter.GetMemberObject(a[i]);
					result[i] = info;
				}
				return result;
			}

			/// <summary>
			/// Returns array of nested delegate types.
			/// </summary>
			public PaxType[] GetDelegates()
			{
				IntegerList a = GetClassKindIds(ClassKind.Delegate);
				PaxType[] result = new PaxType[a.Count];
				for (int i = 0; i < result.Length; i++)
				{
					PaxType info = new PaxType();
					info.m = m.Scripter.GetMemberObject(a[i]);
					result[i] = info;
				}
				return result;
			}

			/// <summary>
			/// Returns array of nested structure types.
			/// </summary>
			public PaxType[] GetStructures()
			{
				IntegerList a = GetClassKindIds(ClassKind.Struct);
				PaxType[] result = new PaxType[a.Count];
				for (int i = 0; i < result.Length; i++)
				{
					PaxType info = new PaxType();
					info.m = m.Scripter.GetMemberObject(a[i]);
					result[i] = info;
				}
				return result;
			}

			/// <summary>
			/// Returns array of nested array types.
			/// </summary>
			public PaxType[] GetArrays()
			{
				IntegerList a = GetClassKindIds(ClassKind.Array);
				PaxType[] result = new PaxType[a.Count];
				for (int i = 0; i < result.Length; i++)
				{
					PaxType info = new PaxType();
					info.m = m.Scripter.GetMemberObject(a[i]);
					result[i] = info;
				}
				return result;
			}

			/// <summary>
			/// Returns array of nested enumeration types.
			/// </summary>
			public PaxType[] GetEnums()
			{
				IntegerList a = GetClassKindIds(ClassKind.Enum);
				PaxType[] result = new PaxType[a.Count];
				for (int i = 0; i < result.Length; i++)
				{
					PaxType info = new PaxType();
					info.m = m.Scripter.GetMemberObject(a[i]);
					result[i] = info;
				}
				return result;
			}

			/// <summary>
			/// Returns array of nested interface types.
			/// </summary>
			public PaxType[] GetInterfaces()
			{
				IntegerList a = GetClassKindIds(ClassKind.Interface);
				PaxType[] result = new PaxType[a.Count];
				for (int i = 0; i < result.Length; i++)
				{
					PaxType info = new PaxType();
					info.m = m.Scripter.GetMemberObject(a[i]);
					result[i] = info;
				}
				return result;
			}

			/// <summary>
			/// Returns ancestor classd type.
			/// </summary>
			public PaxType AncestorClass
			{
				get
				{
					ClassObject c = (m as ClassObject).AncestorClass;
					PaxType result = new PaxType();
					result.m = c;
					return result;
				}
			}

			/// <summary>
			/// Returns constructors of type.
			/// </summary>
			public PaxConstructorInfo[] GetConstructors()
			{
				IntegerList a = GetMemberKindIds(MemberKind.Constructor);
				PaxConstructorInfo[] result = new PaxConstructorInfo[a.Count];
				for (int i = 0; i < result.Length; i++)
				{
					PaxConstructorInfo info = new PaxConstructorInfo();
					info.m = m.Scripter.GetMemberObject(a[i]);
					result[i] = info;
				}
				return result;
			}

			/// <summary>
			/// Returns methods of type.
			/// </summary>
			public PaxMethodInfo[] GetMethods()
			{
				IntegerList a = GetMemberKindIds(MemberKind.Method);
				PaxMethodInfo[] result = new PaxMethodInfo[a.Count];
				for (int i = 0; i < result.Length; i++)
				{
					PaxMethodInfo info = new PaxMethodInfo();
					result[i] = info;
					info.m = m.Scripter.GetMemberObject(a[i]);
				}
				return result;
			}

			/// <summary>
			/// Returns fields of type.
			/// </summary>
			public PaxFieldInfo[] GetFields()
			{
				IntegerList a = GetMemberKindIds(MemberKind.Field);
				PaxFieldInfo[] result = new PaxFieldInfo[a.Count];
				for (int i = 0; i < result.Length; i++)
				{
					PaxFieldInfo info = new PaxFieldInfo();
					result[i] = info;
					info.m = m.Scripter.GetMemberObject(a[i]);
				}
				return result;
			}

			/// <summary>
			/// Returns properties of type.
			/// </summary>
			public PaxPropertyInfo[] GetProperties()
			{
				IntegerList a = GetMemberKindIds(MemberKind.Property);
				PaxPropertyInfo[] result = new PaxPropertyInfo[a.Count];
				for (int i = 0; i < result.Length; i++)
				{
					PaxPropertyInfo info = new PaxPropertyInfo();
					result[i] = info;
					info.m = m.Scripter.GetMemberObject(a[i]);
				}
				return result;
			}

			/// <summary>
			/// Returns events of type.
			/// </summary>
			public PaxEventInfo[] GetEvents()
			{
				IntegerList a = GetMemberKindIds(MemberKind.Event);
				PaxEventInfo[] result = new PaxEventInfo[a.Count];
				for (int i = 0; i < result.Length; i++)
				{
					PaxEventInfo info = new PaxEventInfo();
					result[i] = info;
					info.m = m.Scripter.GetMemberObject(a[i]);
				}
				return result;
			}
		}
		#endregion PaxType Class

		#region PaxFieldInfo Class
		/// <summary>
		/// Represents the field member information.
		/// </summary>
		public class PaxFieldInfo: PaxMemberInfo
		{
			/// <summary>
			/// Returns type of field.
			/// </summary>
			public PaxType FieldType
			{
				get
				{
					PaxType result = new PaxType();
					int type_id = m.Scripter.symbol_table[m.Id].TypeId;
					result.m = m.Scripter.GetMemberObject(type_id);
					return result;
				}
			}
		}
		#endregion PaxFieldInfo Class

		#region PaxPropertyInfo Class
		/// <summary>
		/// Represents the property member information.
		/// </summary>
		public class PaxPropertyInfo: PaxMemberInfo
		{
			/// <summary>
			/// Returns type of property.
			/// </summary>
			public PaxType PropertyType
			{
				get
				{
					PaxType result = new PaxType();
					int type_id = m.Scripter.symbol_table[m.Id].TypeId;
					result.m = m.Scripter.GetMemberObject(type_id);
					return result;
				}
			}

			/// <summary>
			/// Returns 'get' method of property.
			/// </summary>
			public PaxMethodInfo GetGetMethod()
			{
				PropertyObject p = m as PropertyObject;
				int read_id = p.ReadId;
				if (read_id != 0)
				{
					PaxMethodInfo result = new PaxMethodInfo();
					result.m = m.Scripter.GetMemberObject(read_id);
					return result;
				}
				else
					return null;
			}

			/// <summary>
			/// Returns 'set' method of property.
			/// </summary>
			public PaxMethodInfo GetSetMethod()
			{
				PropertyObject p = m as PropertyObject;
				int write_id = p.WriteId;
				if (write_id != 0)
				{
					PaxMethodInfo result = new PaxMethodInfo();
					result.m = m.Scripter.GetMemberObject(write_id);
					return result;
				}
				else
					return null;
			}
		}
		#endregion PaxPropertyInfo Class

		#region PaxEventInfo Class
		/// <summary>
		/// Represents the event member information.
		/// </summary>
		public class PaxEventInfo: PaxMemberInfo
		{
			/// <summary>
			/// Returns type of event.
			/// </summary>
			public PaxType EventType
			{
				get
				{
					PaxType result = new PaxType();
					int type_id = m.Scripter.symbol_table[m.Id].TypeId;
					result.m = m.Scripter.GetMemberObject(type_id);
					return result;
				}
			}

			/// <summary>
			/// Returns 'add' method of event.
			/// </summary>
			public PaxMethodInfo GetAddMethod()
			{
				EventObject p = m as EventObject;
				int add_id = p.AddId;
				if (add_id != 0)
				{
					PaxMethodInfo result = new PaxMethodInfo();
					result.m = m.Scripter.GetMemberObject(add_id);
					return result;
				}
				else
					return null;
			}

			/// <summary>
			/// Returns 'remove' method of event.
			/// </summary>
			public PaxMethodInfo GetRemoveMethod()
			{
				EventObject p = m as EventObject;
				int remove_id = p.RemoveId;
				if (remove_id != 0)
				{
					PaxMethodInfo result = new PaxMethodInfo();
					result.m = m.Scripter.GetMemberObject(remove_id);
					return result;
				}
				else
					return null;
			}
		}
		#endregion PaxEventInfo Class

		#region PaxMethodBase Class
		/// <summary>
		/// The PaxMethodBase class is the base class of PaxMethodInfo
		/// and PaxConstructorInfo classes.
		/// </summary>
		public class PaxMethodBase: PaxMemberInfo
		{
			/// <summary>
			/// Returns representation of parameters.
			/// </summary>
			public PaxParameterInfo[] GetParameters()
			{
				FunctionObject f = m as FunctionObject;
				PaxParameterInfo[] result = new PaxParameterInfo[f.ParamCount];
				for (int i = 0; i < f.ParamCount; i++)
				{
					PaxParameterInfo info = new PaxParameterInfo();
					result[i] = info;

					int param_id = f.GetParamId(i);
					info.Name = m.Scripter.symbol_table[param_id].Name;

					info.ParameterType = new PaxType();
					int type_id = m.Scripter.symbol_table[param_id].TypeId;
					info.ParameterType.m = m.Scripter.GetMemberObject(type_id);

					info.pm = f.GetParamMod(i);
				}
				return result;
			}
		}
		#endregion PaxMethodBase Class

		#region PaxConstructorInfo Class
		/// <summary>
		/// Represents a constructor of type.
		/// </summary>
		public class PaxConstructorInfo: PaxMethodBase
		{
		}
		#endregion PaxConstructorInfo Class

		#region PaxMethodInfo Class
		/// <summary>
		/// Represents a method of type.
		/// </summary>
		public class PaxMethodInfo: PaxMethodBase
		{
			/// <summary>
			/// Returns return type of method.
			/// </summary>
			public PaxType ReturnTypeInfo
			{
				get
				{
					PaxType result = new PaxType();
					int res_id = m.Scripter.symbol_table.GetResultId(m.Id);
					int res_type_id = m.Scripter.symbol_table[res_id].TypeId;
					result.m = m.Scripter.GetMemberObject(res_type_id);
					return result;
				}
			}

			/// <summary>
			/// Invokes method.
			/// </summary>
			public object Invoke(object target,	params object[] parameters)
			{
				return m.Scripter.code.CallMethodEx(RunMode.Run, target,
													Id, parameters);
			}
		}
		#endregion PaxMethodInfo Class

		#region PaxParameterInfo Class
		/// <summary>
		/// Represents a parameter of method.
		/// </summary>
		public class PaxParameterInfo
		{
			/// <summary>
			/// Internal field that represents a modifier of parameter.
			/// </summary>
			 internal ParamMod pm;

			/// <summary>
			/// Name of parameter.
			/// </summary>
			 public string Name;

			/// <summary>
			/// Type of parameter.
			/// </summary>
			 public PaxType ParameterType;

			/// <summary>
			/// Gets a value indicating whether this is an input parameter.
			/// </summary>
			 public bool IsIn
			 {
				 get
				 {
					 return pm == ParamMod.None;
				 }
			 }

			/// <summary>
			/// Gets a value indicating whether this is a Retval parameter.
			/// </summary>
			 public bool IsRetval
			 {
				 get
				 {
					 return pm == ParamMod.RetVal;
				 }
			 }

			/// <summary>
			/// Gets a value indicating whether this is a output parameter.
			/// </summary>
			 public bool IsOut
			 {
				 get
				 {
					 return pm == ParamMod.Out;
				 }
			 }
		}
		#endregion PaxParameterInfo Class

		#region PaxNamespaceInfo Class
		/// <summary>
		/// Represents a namespace. (Note, that in paxScript implementation
		/// the namespace is just a static class type).
		/// </summary>
		public class PaxNamespaceInfo: PaxType
		{
			/// <summary>
			/// Returns root (noname) namespace.
			/// </summary>
			public static PaxNamespaceInfo GetNonameNamespaceInfo(PaxScripter scripter)
			{
				PaxNamespaceInfo result = new PaxNamespaceInfo();
				result.m = scripter.scripter.GetMemberObject(0);
				return result;
			}

			/// <summary>
			/// Returns namespace by name.
			/// </summary>
			public static PaxNamespaceInfo LookupNamespace(PaxScripter scripter, string full_name)
			{
				PaxNamespaceInfo result = null;
				int id = scripter.scripter.symbol_table.LookupTypeByFullName(full_name, false);
				if (id == 0) return null;
				MemberObject mo = scripter.scripter.GetMemberObject(id);

				if (mo is ClassObject)
				{
					if ((mo as ClassObject).IsNamespace)
					{
						result = new PaxNamespaceInfo();
						result.m = mo;
					}
				}

				return result;
			}

			/// <summary>
			/// Returns array of nested namespaces.
			/// </summary>
			public override PaxNamespaceInfo[] GetNamespaces()
			{
				IntegerList a = GetClassKindIds(ClassKind.Namespace);
				PaxNamespaceInfo[] result = new PaxNamespaceInfo[a.Count];
				for (int i = 0; i < result.Length; i++)
				{
					PaxNamespaceInfo info = new PaxNamespaceInfo();
					info.m = m.Scripter.GetMemberObject(a[i]);
					result[i] = info;
				}
				return result;
			}
		}
		#endregion PaxNamespaceInfo Class
	}
	#endregion Reflection Namespace
}
