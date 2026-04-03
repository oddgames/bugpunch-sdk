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
using SL;

namespace PaxScript.Net
{
	#region IndexObject Class
	/// <summary>
	/// Represents array element definition or string element definition.
	/// </summary>
	internal class IndexObject: ScriptObject
	{
		/// <summary>
		/// List of indexes.
		/// </summary>
		PaxArrayList indexes;

		/// <summary>
		/// List of indexes.
		/// </summary>
		int[] p;

		/// <summary>
		/// Number of indexes.
		/// </summary>
		int param_count;

		/// <summary>
		/// Instance of host-defined array.
		/// </summary>
		System.Array array_instance;

		/// <summary>
		/// Instance of host-defined string.
		/// </summary>
		string string_instance;

		/// <summary>
		/// If what = 1, this is a string element. if what = 2, this is an array
		/// element.
		/// </summary>
		int what;

		/// <summary>
		/// Undocumented.
		/// </summary>
		int index1;

		internal int MinValue = 0;

        /// <summary>
        /// what = 3
        /// </summary>
        object another_instance = null;
        int read_id = 0;
        int write_id = 0;
        object[] p_o;

		/// <summary>
		/// Constructor.
		/// </summary>
		internal IndexObject(BaseScripter scripter, ObjectObject o): base(scripter)
		{
			indexes = new PaxArrayList();
            Type tinstance = o.Instance.GetType();
            if (tinstance == typeof(string))
			{
				string_instance = (string) o.Instance;
				what = 1;
			}
            else if (tinstance.IsArray)
			{
				array_instance = (System.Array) o.Instance;
				what = 2;
			}
            else 
            {
                PropertyObject prop_object = o.Class_Object.FindIndexer();
                if (prop_object != null)
                {
                    read_id = prop_object.ReadId;
                    write_id = prop_object.WriteId;
                }
                another_instance = o;
                what = 3;
            }
        }

		/// <summary>
		/// Adds new index.
		/// </summary>
		public void AddIndex(object v)
		{
			if (what == 1) 
				index1 = (int) v;
			else
				indexes.Add(v);
		}

		/// <summary>
		/// Sets up set of indexes.
		/// </summary>
		public void Setup()
		{
			if (what == 2)
			{
				param_count = indexes.Count;
				p = new int[param_count];
				for (int i = 0; i < indexes.Count; i++)
				{
					p[i] = (int) Conversion.ChangeType(indexes[i], typeof(int)) - MinValue;
				}
			}
		}

		/// <summary>
		/// Returns value of element.
		/// </summary>
		public object Value
		{
			get
			{
				if (what == 1)
					return string_instance[index1];
                else if (what == 2)
                    return array_instance.GetValue(p);
                else
                {
                    param_count = indexes.Count;
                    p_o = new object[param_count];
                    for (int i = 0; i < indexes.Count; i++)
                    {
                        p_o[i] = indexes[i];
                    }

                    MethodInfo Method_Info = Scripter.code.GetFunctionObject(read_id).Method_Info;
                    if (Method_Info != null)
                    {
                        object host_instance = (another_instance as ObjectObject).Instance;
                        return Method_Info.Invoke(host_instance, p_o);
                    }
                    else
                        return
                            Scripter.CallMethod(RunMode.Run, another_instance, read_id, p_o);
                }
			}
			set
			{
				if (what == 2)
				{
					if (value.GetType() == typeof(ObjectObject))
					{
						if ((value as ObjectObject).Imported)
							array_instance.SetValue(((ObjectObject) value).Instance, p);
						else
							array_instance.SetValue(value, p);
					}
					else
						array_instance.SetValue(value, p);
				}
                else if (what == 3)
                {
                    param_count = indexes.Count;
                    p_o = new object[param_count + 1];
                    p_o[0] = value;
                    for (int i = 0; i < indexes.Count; i++)
                    {
                        p_o[i+1] = indexes[i];
                    }
                    MethodInfo Method_Info = Scripter.code.GetFunctionObject(read_id).Method_Info;
                    if (Method_Info != null)
                    {
                        object host_instance = (another_instance as ObjectObject).Instance;
                        Method_Info.Invoke(host_instance, p_o);
                    }
                    else
                        Scripter.CallMethod(RunMode.Run, another_instance, write_id, p_o);
                }
			}
		}
	}
	#endregion IndexObject Class
}
