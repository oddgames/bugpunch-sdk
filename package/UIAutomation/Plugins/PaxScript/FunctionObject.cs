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
using System.Reflection;

namespace PaxScript.Net
{
	#region FunctionObject Class
	/// <summary>
	/// Represents method definition.
	/// </summary>
	internal class FunctionObject: MemberObject
	{
		/// <summary>
		/// Entry point of method.
		/// </summary>
		ProgRec init = null;

		/// <summary>
		/// Id of field which keeps returned value.
		/// </summary>
		int res_id;

		/// <summary>
		/// Id of 'this' parameter.
		/// </summary>
		int this_id;

		/// <summary>
		/// Id of 'params' parameter.
		/// </summary>
		int params_id = 0;

		/// <summary>
		/// Undocumented.
		/// </summary>
		int params_element_id = 0; // just to simplify the type checking

		/// <summary>
		/// Undocumented.
		/// </summary>
		int low_bound = 0; // records in symbol_table in [Id..low_bound]

		/// <summary>
		/// Undocumented.
		/// </summary>
		public ClassObject ExplicitInterface = null;

		/// <summary>
		/// List of ids of formal parameters.
		/// </summary>
		public IntegerList Param_Ids;

		/// <summary>
		/// List of modifiers of formal parameters.
		/// </summary>
		public IntegerList Param_Mod; // 0 - None, 1 - RetVal, 2 - Out

		/// <summary>
		/// List of default values.
		/// </summary>
		public IntegerList Default_Ids;

		/// <summary>
		/// If given method represents a host-defined constructor, this field
		/// keeps ConstructorInfo.
		/// </summary>
		public ConstructorInfo Constructor_Info;

		/// <summary>
		/// If given method represents a host-defined method, this field
		/// keeps MethodInfo.
		/// </summary>
		public MethodInfo Method_Info;

		/// <summary>
		/// If given method represents a host-defined method, this field
		/// keeps arguments of the method.
		/// </summary>
		public object[] Params;

		/// <summary>
		/// Name index of method signature.
		/// </summary>
		public int SignatureIndex = -1;

		/// <summary>
		/// Constructor.
		/// </summary>
		internal FunctionObject(BaseScripter scripter, int sub_id, int owner_id)
		: base(scripter, sub_id, owner_id)
		{
			Param_Ids = new IntegerList(false);
			Param_Mod = new IntegerList(true);
			Default_Ids = new IntegerList(true);
			res_id = scripter.symbol_table.GetResultId(sub_id);
			this_id = scripter.symbol_table.GetThisId(sub_id);
			Constructor_Info = null;
		}

		/// <summary>
		/// Adds id of new formal parameter.
		/// </summary>
		public void AddParam(int id, ParamMod mod)
		{
			Param_Ids.Add(id);
			Param_Mod.Add(mod);
			Default_Ids.Add(0);
		}

		/// <summary>
		/// Adds id of default value of parameter.
		/// </summary>
		public void AddDefaultValueId(int param_id, int default_value_id)
		{
			for (int i = 0; i < Param_Ids.Count; i++)
			{
				if (Param_Ids[i] == param_id)
					Default_Ids[i] = default_value_id;
			}
		}

		/// <summary>
		/// Returns number of default parameters.
		/// </summary>
		public int DefaultParamCount
		{
			get
			{
				int result = 0;
				for (int i = 0; i < Default_Ids.Count; i++)
				{
					if (Default_Ids[i] != 0)
						result ++;
				}
				return result;
			}
		}

		/// <summary>
		/// Assigns actual parameter value to formal parameter.
		/// </summary>
		public void PutParam(int param_number, object value)
		{
			int param_id = Param_Ids[param_number];
			Scripter.code.PutVal(param_id, value);
		}

		/// <summary>
		/// Undocumented.
		/// </summary>
		public void SetupLowBound(int id)
		{
			low_bound = id;
		}

		/// <summary>
		/// Creates Params object.
		/// </summary>
		public void SetupParameters()
		{
			Params = new object[ParamCount];
		}

		/// <summary>
		/// Returns id of formal parameter.
		/// </summary>
		public int GetParamId(int param_number)
		{
			if (param_number < ParamCount)
				return Param_Ids[param_number];
			else
				return params_element_id;
		}

		/// <summary>
		/// Returns modifier of formal parameter.
		/// </summary>
		public ParamMod GetParamMod(int param_number)
		{
			if (param_number < ParamCount)
				return (ParamMod) Param_Mod[param_number];
			else
				return (ParamMod) Param_Mod[ParamCount - 1];
		}

		/// <summary>
		/// Assigns value to 'this' parameter.
		/// </summary>
		public void PutThis(object value)
		{
			Scripter.code.PutVal(this_id, value);
		}

		/// <summary>
		/// Allocates fields of method.
		/// </summary>
		public void AllocateSub()
		{
			SymbolTable symbol_table = Scripter.symbol_table;
			for (int i = Id; i <= low_bound; i++)
			{
				SymbolRec s = symbol_table[i];
				if (s.Level == Id && (!s.is_static))
					s.IncValueLevel();
			}
		}

		/// <summary>
		/// Deallocates fields of method.
		/// </summary>
		public void DeallocateSub()
		{
			SymbolTable symbol_table = Scripter.symbol_table;
			for (int i = Id; i <= low_bound; i++)
			{
				SymbolRec s = symbol_table[i];
				if (s.Level == Id && (!s.is_static))
					s.DecValueLevel();
			}
		}

		/// <summary>
		/// Invokes constructor (for host-defined constructor).
		/// </summary>
		public object InvokeConstructor()
		{
			return Constructor_Info.Invoke(Params);
		}

		/// <summary>
		/// Invokes method (for host-defined method).
		/// </summary>
		public object InvokeMethod(object obj)
		{
			return Method_Info.Invoke(obj, Params);
		}

		/// <summary>
		/// Returns value of formal parameter.
		/// </summary>
		public object GetParamValue(int param_number)
		{
			if (Imported)
				return Params[param_number];
			else
			{
				int param_id = Param_Ids[param_number];
				Scripter.symbol_table[param_id].IncValueLevel();
				object v = Scripter.code.GetVal(param_id);
				Scripter.symbol_table[param_id].DecValueLevel();
				return v;
			}
		}

		/// <summary>
		/// Returns type id of formal parameter.
		/// </summary>
		public int GetParamTypeId(int param_number)
		{
			int param_id = Param_Ids[param_number];
			return Scripter.GetTypeId(param_id);
		}

		/// <summary>
		/// Returns 'true', if method must be called by OP_CALL_VIRT.
		/// </summary>
		public bool RequiresLateBinding() //run-time only
		{
			ClassObject o = Scripter.code.GetClassObject(OwnerId);
			if (o.Class_Kind == ClassKind.Interface)
				return true;
			else
			{
				return HasModifier(Modifier.Virtual) || HasModifier(Modifier.Override);
			}
		}

		/// <summary>
		/// Returns late binding FunctionObject.
		/// </summary>
		public FunctionObject GetLateBindingFunction(object v, int type_id, bool upcase)
		{
			FunctionObject result = this;
/*
			bool is_interface = Scripter.GetClassObject(OwnerId).IsInterface;
			ClassObject explicit_interface_type;
			if (is_interface)
				explicit_interface_type = Scripter.GetClassObject(OwnerId);
			else
				explicit_interface_type = null;
*/
			int method_name_index = NameIndex;

			ClassObject c;
			if (Static)
			{
				c = (ClassObject) v;
			}
			else
			{
				c = Scripter.GetClassObject(type_id);
			}

			string s = Name.ToUpper();

			for (int i = 0; i < c.Members.Count; i++)
			{
				MemberObject m = c.Members[i];
				bool ok;
				if (Static)
					ok = m.HasModifier(Modifier.Static);
				else
					ok = !m.HasModifier(Modifier.Static);

				ok = ok && m.Kind == MemberKind.Method;
				if (upcase)
				{
					if (s == m.Name.ToUpper())
					{
						FunctionObject f = m as FunctionObject;
						if (SignatureIndex == f.SignatureIndex)
						{
							return f;
						}
					}
				}
				else
				{
					if (method_name_index == m.NameIndex)
					{
						FunctionObject f = m as FunctionObject;
						if (SignatureIndex == f.SignatureIndex)
						{
							return f;
						}
					}
				}
			}

			return result;
		}

		/// <summary>
		/// Returns 'true', if methods have equal headers.
		/// </summary>
		public static bool CompareHeaders(FunctionObject fx, FunctionObject fy)
		{
			if (fx.ParamCount != fy.ParamCount)
				return false;
			SymbolTable symbol_table = fx.Scripter.symbol_table;
			bool b = true;
			int ix, iy, tx, ty;
			string typex, typey;
			for (int j = 0; j < fx.ParamCount; j++)
			{
				ix = fx.Param_Ids[j];
				iy = fy.Param_Ids[j];
				tx = symbol_table[ix].TypeId;
				ty = symbol_table[iy].TypeId;
				typex = symbol_table[tx].Name;
				typey = symbol_table[ty].Name;
				b &= (typex == typey);
			}
			ix = fx.ResultId;
			iy = fy.ResultId;
			tx = symbol_table[ix].TypeId;
			ty = symbol_table[iy].TypeId;
			typex = symbol_table[tx].Name;
			typey = symbol_table[ty].Name;
			b &= (typex == typey);
			return b;
		}

		/// <summary>
		/// Returns 'true', if id represents a formal parameter of method.
		/// </summary>
		public bool HasParameter(int id)
		{
			for (int i = 0; i < ParamCount; i++)
				if (Param_Ids[i] == id)
					return true;
			return false;
		}

		/// <summary>
		/// Creates signature of method.
		/// </summary>
		public void CreateSignature()
		{
			string signature = "(";
			for (int i = 0; i < ParamCount; i++)
			{
				int param_type_id = Scripter.symbol_table[Param_Ids[i]].TypeId;
				string s = Scripter.symbol_table[param_type_id].Name;
				signature += s;
				if (i != ParamCount - 1)
					signature += ",";
			}
			signature += ")";

			SignatureIndex = Scripter.names.Add(signature);
		}

		/// <summary>
		/// Returns signature of method.
		/// </summary>
		public string Signature
		{
			get
			{
				if (SignatureIndex == -1)
					return "unknown";
				else
					return Scripter.names[SignatureIndex];
			}
		}

		/// <summary>
		/// Returns id of 'params' parameter.
		/// </summary>
		public int ParamsId
		{
			get
			{
				return params_id;
			}
			set
			{
				params_id = value;
			}
		}

		/// <summary>
		/// Undocumented.
		/// </summary>
		public int ParamsElementId
		{
			get
			{
				return params_element_id;
			}
			set
			{
				params_element_id = value;
			}
		}

		/// <summary>
		/// Returns entry point of method.
		/// </summary>
		public ProgRec Init
		{
			get
			{
				return init;
			}
			set
			{
				this.init = value;
			}
		}

		/// <summary>
		/// Returns id of field which keeps value returned by method.
		/// </summary>
		public int ResultId
		{
			get
			{
				return res_id;
			}
		}

		/// <summary>
		/// Returns owner of method.
		/// </summary>
		public ClassObject Owner
		{
			get
			{
				return Scripter.GetClassObject(OwnerId);
			}
		}

		/// <summary>
		/// Returns number of parameters of method.
		/// </summary>
		public int ParamCount
		{
			get
			{
				return Param_Ids.Count;
			}
		}

		/// <summary>
		/// Returns id of 'this' parameter of method.
		/// </summary>
		public int ThisId
		{
			get
			{
				return this_id;
			}
		}
	}
	#endregion FunctionObject Class
}
