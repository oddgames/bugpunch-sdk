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
using SL;

namespace PaxScript.Net
{
	#region InstanceProperty Class
	/// <summary>
	/// Represents value of property of a script-defined object.
	/// </summary>
	internal class InstanceProperty
	{
		/// <summary>
		/// Value of property.
		/// </summary>
		object value;

		/// <summary>
		/// MemberObject object which represents given property.
		/// </summary>
		MemberObject m;

		/// <summary>
		/// FieldInfo for host-defined property.
		/// </summary>
		public FieldInfo Field_Info = null;

		/// <summary>
		/// Id of type.
		/// </summary>
		public int TypeId;

		/// <summary>
		/// Constructor.
		/// </summary>
		public InstanceProperty(MemberObject m, int type_id)
		{
			this.m = m;
			TypeId = type_id;
		}

		/// <summary>
		/// Returns a copy of property.
		/// </summary>
		public InstanceProperty Clone()
		{
			InstanceProperty result = new InstanceProperty(m, TypeId);
			result.Field_Info = Field_Info;
			result.Value = Value;
			return result;
		}

		/// <summary>
		/// Returns name index of property name.
		/// </summary>
		public int NameIndex
		{
			get
			{
				return m.NameIndex;
			}
		}

		/// <summary>
		/// Returns property name.
		/// </summary>
		public string Name
		{
			get
			{
				return m.Name;
			}
		}

		/// <summary>
		/// Returns upcase property name.
		/// </summary>
		public string UpcaseName
		{
			get
			{
				return m.UpcaseName;
			}
		}

		/// <summary>
		/// Returns value of property.
		/// </summary>
		public object Value
		{
			get
			{
				return value;
			}
			set
			{
				this.value = value;
			}
		}
	}
	#endregion InstanceProperty Class

	#region InstancePropertyList Class
	/// <summary>
	/// Represents list of values of properties of a script-defined object.
	/// </summary>
	internal class InstancePropertyList
	{
		/// <summary>
		/// Represents list of values of properties.
		/// </summary>
		IntegerList items;

		BaseScripter scripter;

		/// <summary>
		/// Constructor.
		/// </summary>
		public InstancePropertyList(BaseScripter scripter)
		{
			items = new IntegerList(true);
			this.scripter = scripter;
		}

		/// <summary>
		/// Returns number of items in the property list.
		/// </summary>
		public int Count
		{
			get
			{
				return items.Count;
			}
		}

		/// <summary>
		/// Adds new property.
		/// </summary>
		public int Add(InstanceProperty p)
		{
			items.AddObject(p.NameIndex, p);
			return Count;
		}

		/// <summary>
		/// Returns property by name index of property name.
		/// </summary>
		public InstanceProperty FindProperty(int name_index, int type_id)
		{
			bool ok = false;
			for (int i = 0; i < Count; i++)
			{
				InstanceProperty p = (InstanceProperty) items.Objects[i];
				if (p.TypeId == type_id)
					ok = true;

				if (ok && (items[i] == name_index))
				{
					return p;
				}
			}

			string upcase_name = scripter.GetUpcaseNameByNameIndex(name_index);

			ok = false;
			for (int i = 0; i < Count; i++)
			{
				InstanceProperty p = (InstanceProperty) items.Objects[i];
				if (p.TypeId == type_id)
					ok = true;

				if (ok && (p.UpcaseName == upcase_name))
				{
					return p;
				}
			}

			return null;
		}

		/// <summary>
		/// Returns property by name index of property name.
		/// </summary>
		public InstanceProperty FindProperty(int name_index)
		{
			for (int i = 0; i < Count; i++)
			{
				InstanceProperty p = (InstanceProperty) items.Objects[i];
				if (items[i] == name_index)
				{
					return p;
				}
			}

			string upcase_name = scripter.GetUpcaseNameByNameIndex(name_index);

			for (int i = 0; i < Count; i++)
			{
				InstanceProperty p = (InstanceProperty) items.Objects[i];
				if (p.UpcaseName == upcase_name)
				{
					return p;
				}
			}

			return null;
		}

		/// <summary>
		/// Returns property by index.
		/// </summary>
		public InstanceProperty this[int i]
		{
			get
			{
				return (InstanceProperty) items.Objects[i];
			}
		}
	}
	#endregion InstancePropertyList Class

	#region ObjectObject Class
	/// <summary>
	/// Represents definition of script-defined object.
	/// </summary>
	internal class ObjectObject: ScriptObject
	{
		/// <summary>
		/// Represents type of script-defined object.
		/// </summary>
		ClassObject class_object;

		/// <summary>
		/// Represents list of properties of script-defined object.
		/// </summary>
		public InstancePropertyList Properties;

		/// <summary>
		/// Instance of host-defined object which is wrapped by given object.
		/// </summary>
		public object Instance;

		/// <summary>
		/// List of delegate targets.
		/// </summary>
		PaxArrayList invocation_listX;

		/// <summary>
		/// List of delegate methods.
		/// </summary>
		PaxArrayList invocation_listF;

		/// <summary>
		/// Current invocation index.
		/// </summary>
		int invocation_index;

		/// <summary>
		/// Constructor.
		/// </summary>
		internal ObjectObject(BaseScripter scripter, ClassObject class_object): base(scripter)
		{
			this.class_object = class_object;
			Properties = new InstancePropertyList(scripter);
			Instance = null;
			invocation_listX = new PaxArrayList();
			invocation_listF = new PaxArrayList();
			invocation_index = -1;
		}

		/// <summary>
		/// Returns a copy of object.
		/// </summary>
		public ObjectObject Clone()
		{
			ObjectObject result = new ObjectObject(Scripter, class_object);
			result.Instance = Instance;
			for (int i = 0; i < Properties.Count; i++)
			{
				InstanceProperty p = Properties[i].Clone();
				result.Properties.Add(p);
			}
			return result;
		}

		/// <summary>
		/// Adds script-defined delegate to invocation list.
		/// </summary>
		public void AddInvocation(object x, FunctionObject f)
		{
			invocation_listX.Add(x);
			invocation_listF.Add(f);
		}

		/// <summary>
		/// Subtracts script-defined delegate from invocation list.
		/// </summary>
		public void SubInvocation(object x, FunctionObject f)
		{
			for (;;)
			{
				bool found = false;
				for (int i = 0; i < invocation_listX.Count; i++)
				{
					object temp_x = invocation_listX[i];
					FunctionObject temp_f = (FunctionObject) invocation_listF[i];
					if ((x == temp_x) && (f == temp_f))
					{
						invocation_listX.RemoveAt(i);
						invocation_listF.RemoveAt(i);
						found = true;
					}
				}
				if (!found)
					break;
			}
		}

		/// <summary>
		/// Returns number of elements of invocation list.
		/// </summary>
		public int InvocationCount
		{
			get
			{
				return invocation_listX.Count;
			}
		}

		/// <summary>
		/// Returns first delegate from invocation list.
		/// </summary>
		public bool FindFirstInvocation(out object x, out FunctionObject f)
		{
			if (invocation_listX.Count == 0)
			{
				x = null;
				f = null;
				invocation_index = -1;
				return false;
			}
			else
			{
				invocation_index = 0;
				x = invocation_listX[invocation_index];
				f = (FunctionObject) invocation_listF[invocation_index];
				return true;
			}
		}

		/// <summary>
		/// Returns next delegate from invocation list.
		/// </summary>
		public bool FindNextInvocation(out object x, out FunctionObject f)
		{
			invocation_index++;
			if (invocation_index >= invocation_listX.Count)
			{
				x = null;
				f = null;
				invocation_index = -1;
				return false;
			}
			else
			{
				x = invocation_listX[invocation_index];
				f = (FunctionObject) invocation_listF[invocation_index];
				return true;
			}
		}

		/// <summary>
		/// Returns script-defined type of object.
		/// </summary>
		public ClassObject Class_Object
		{
			get
			{
				return class_object;
			}
		}

		/// <summary>
		/// Returns 'true', if it is an imported object.
		/// </summary>
		public bool Imported
		{
			get
			{
				return class_object.Imported;
			}
		}

		/// <summary>
		/// Returns property of object by name index of property name.
		/// </summary>
		public InstanceProperty FindProperty(int name_index, int type_id)
		{
			return Properties.FindProperty(name_index, type_id);
		}

		/// <summary>
		/// Returns property of object by name index of property name.
		/// </summary>
		public InstanceProperty FindProperty(int name_index)
		{
			return Properties.FindProperty(name_index);
		}

		/// <summary>
		/// Returns true, if object has property with name index of property name.
		/// </summary>
		public bool HasProperty(int name_index)
		{
			return Properties.FindProperty(name_index) != null;
		}

		/// <summary>
		/// Assigns property of object by name index of property name.
		/// </summary>
		public void PutProperty(int name_index, int type_id, object value)
		{
			InstanceProperty p = FindProperty(name_index, type_id);
			if (p == null)
			{
				p = FindProperty(name_index);
				if (p == null)
				{
					// 'type' does not contain a definition for 'function'
					string s = Scripter.names[name_index];
					Scripter.RaiseExceptionEx(Errors.CS0117, class_object.Name, s);
				}
			}

			if (p.Field_Info != null)
				p.Field_Info.SetValue(Instance, value);
			else
				p.Value = value;
		}

		/// <summary>
		/// Returns property of object by name index of property name.
		/// </summary>
		public object GetProperty(int name_index, int type_id)
		{
			InstanceProperty p = FindProperty(name_index, type_id);
			if (p == null)
			{
				p = FindProperty(name_index);
				if (p == null)
				{
					// 'type' does not contain a definition for 'function'
					string s = Scripter.names[name_index];
					Scripter.RaiseExceptionEx(Errors.CS0117, class_object.Name, s);
				}
			}

			if (p.Field_Info != null)
				return p.Field_Info.GetValue(Instance);
			else
				return p.Value;
		}
	}
	#endregion ObjectObject Class
}

