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

#undef full

using System;
using System.Reflection;
using System.Collections;
#if full
using System.Reflection.Emit;
#endif
using SL;

namespace PaxScript.Net
{
	#region EventRec Class
	/// <summary>
	/// Saves info about registered event handlers and senders. (.NET CF)
	/// </summary>
	internal class EventRec
	{
		/// <summary>
		/// Host-defined sender.
		/// </summary>
		public object Sender = null;

		/// <summary>
		/// Script-defined delegate.
		/// </summary>
		public object ScriptDelegate = null;

		/// <summary>
		/// Host-defined delegate.
		/// </summary>
		public Delegate HostDelegate = null;

		/// <summary>
		/// Type of event handler.
		/// </summary>
		public Type EventHandlerType = null;

		/// <summary>
		/// Event name.
		/// </summary>
		public string EventName = "";

		/// <summary>
		/// Undocumented.
		/// </summary>
		public bool Processed = false;
	}
	#endregion EventRec Class

	#region EventDispatcher Class
	/// <summary>
	/// Implements processing of script-defined event handlers.
	/// </summary>
	internal class EventDispatcher
	{
		/// <summary>
		/// Represents kernel of scripter.
		/// </summary>
		BaseScripter scripter;
#if full
		/// <summary>
		/// Type builder.
		/// </summary>
		TypeBuilder type_builder;

		/// <summary>
		/// Saves BaseScripter object.
		/// </summary>
		FieldBuilder fld_scripter;

		/// <summary>
		/// Saves script-defined delegate.
		/// </summary>
		FieldBuilder fld_script_delegate;

		/// <summary>
		/// Dispatch type.
		/// </summary>
		Type DispatchType = null;

		/// <summary>
		/// Undocumented.
		/// </summary>
		int cnt = 0;
#endif

		/// <summary>
		/// List of pairs (event handler type, delegate). (.NET CF).
		/// </summary>
		PaxArrayList registered_event_handlers;

		/// <summary>
		/// List of EventRec instances. (.NET CF).
		/// </summary>
		PaxArrayList event_rec_list;

		/// <summary>
		/// Name of type.
		/// </summary>
		string type_name;

		/// <summary>
		/// Constructor.
		/// </summary>
		internal EventDispatcher(BaseScripter scripter, string type_name)
		{
			this.scripter = scripter;
			this.type_name = type_name;
#if full
			type_builder = scripter.PaxModuleBuilder.DefineType(type_name,
				TypeAttributes.Class & TypeAttributes.Public);

			fld_scripter = type_builder.DefineField("scripter",
				typeof(object), FieldAttributes.Public);
			fld_script_delegate = type_builder.DefineField("script_delegate",
				typeof(object), FieldAttributes.Public);
#endif
			registered_event_handlers = new PaxArrayList();
			event_rec_list = new PaxArrayList();

			RegisterStandardHandlers();
		}

		/// <summary>
		/// Resets registered event handler list.
		/// </summary>
		public void Reset()
		{
			registered_event_handlers.Clear();
			event_rec_list.Clear();
			RegisterStandardHandlers();
#if full
			type_builder = scripter.PaxModuleBuilder.DefineType(type_name + (cnt++),
				TypeAttributes.Class & TypeAttributes.Public);
			fld_scripter = type_builder.DefineField("scripter",
				typeof(object), FieldAttributes.Public);
			fld_script_delegate = type_builder.DefineField("script_delegate",
				typeof(object), FieldAttributes.Public);
#endif
		}

		/// <summary>
		/// Adds method to the dispatch type.
		/// </summary>
		public void DefineEventHandler(EventInfo event_info, FunctionObject f)
		{
#if full
			int type_id = scripter.symbol_table[f.ResultId].TypeId;
			ClassObject c = scripter.GetClassObject(type_id);
			Type result_type = c.ImportedType;

			Type[] param_types = new Type[f.ParamCount];
			for (int i = 0; i < param_types.Length; i++)
			{
				type_id = scripter.symbol_table[f.GetParamId(i)].TypeId;
				c = scripter.GetClassObject(type_id);
				param_types[i] = c.ImportedType;
			}

			string method_name = event_info.Name + "_" +  f.Name;
			MethodBuilder method_builder;

			method_builder = type_builder.DefineMethod(method_name, MethodAttributes.Public, result_type, 
				param_types);

			MethodInfo caller_info = typeof(BaseScripter).GetMethod("ApplyDelegate");

			// Generate IL for

			ILGenerator il_generator = method_builder.GetILGenerator();

			il_generator.Emit(OpCodes.Ldarg_0);
			il_generator.Emit(OpCodes.Ldfld, fld_scripter); // pass field 1

			il_generator.Emit(OpCodes.Ldarg_0);
			il_generator.Emit(OpCodes.Ldfld, fld_script_delegate); // pass field 2

			// create array

			il_generator.Emit(OpCodes.Ldc_I4, param_types.Length);
			il_generator.Emit(OpCodes.Newarr, typeof(object));

			// array on the top

			for (int i = 0; i < param_types.Length; i++)
			{
				il_generator.Emit(OpCodes.Dup); // array
				il_generator.Emit(OpCodes.Ldc_I4, i); // index
				il_generator.Emit(OpCodes.Ldarg, i + 1); // value
				il_generator.Emit(OpCodes.Stelem_Ref); // a[i] = value
			}

			// array on the top here; pass array

			il_generator.Emit(OpCodes.Call, caller_info);
			il_generator.Emit(OpCodes.Ret);
#endif
		}

		/// <summary>
		/// Creates dispatch type.
		/// </summary>
		public void CreateDispatchType()
		{
#if full
			DispatchType = scripter.FindAvailableType(type_builder.FullName, false);
			if (DispatchType == null)
			{
				DispatchType = type_builder.CreateType();
				scripter.RegisterAvailableType(DispatchType);
			}
#endif
		}

		/// <summary>
		/// Creates delegate of dispatch type.
		/// </summary>
		public Delegate CreateDelegate(object instance,
									   EventInfo event_info,
									   FunctionObject f,
									   object script_delegate)
		{
#if full
			object target = Activator.CreateInstance(DispatchType);
			FieldInfo fld_scripter = DispatchType.GetField("scripter");
			FieldInfo fld_script_delegate = DispatchType.GetField("script_delegate");

			fld_scripter.SetValue(target, scripter);
			fld_script_delegate.SetValue(target, script_delegate);

			Type delegate_type = event_info.EventHandlerType;
			string method_name = event_info.Name + "_" + f.Name;
			Delegate d = Delegate.CreateDelegate(delegate_type, target, method_name);
			return d;
#else
			Type t = event_info.EventHandlerType;
			string s = event_info.Name;

			for (int i = 0; i < registered_event_handlers.Count; i++)
			{
				EventRec er = registered_event_handlers[i] as EventRec;
				Delegate host_delegate = er.HostDelegate;

				if (t == er.EventHandlerType && s == er.EventName)
				{
					EventRec er2 = new EventRec();
					er2.Sender = instance;
					er2.EventHandlerType = t;
					er2.ScriptDelegate = script_delegate;
					er2.HostDelegate = host_delegate;
					er2.EventName = s;
					event_rec_list.Add(er2);

					return host_delegate;
				}
			}
			return null;
#endif
		}

		/// <summary>
		/// Registeres event handler. (.NET CF)
		/// </summary>
		public void RegisterEventHandler(Type event_handler_type,
										 string event_name,
										 Delegate d)
		{
			EventRec er = new EventRec();
			er.EventHandlerType = event_handler_type;
			er.EventName = event_name;
			er.HostDelegate = d;
			registered_event_handlers.Add(er);
		}

		/// <summary>
		/// Unregisteres event handler. (.NET CF)
		/// </summary>
		public void UnregisterEventHandler(Type event_handler_type,
										   string event_name)
		{
			for (int i = 0; i < registered_event_handlers.Count; i++)
			{
				EventRec er = registered_event_handlers[i] as EventRec;
				if (event_handler_type == er.EventHandlerType && event_name == er.EventName)
					registered_event_handlers.RemoveAt(i);
			}
		}

		/// <summary>
		/// Returns script-defined delegate. (.NET CF)
		/// </summary>
		public object LookupScriptDelegate(object sender,
										   string event_name)
		{
			for (int i = 0; i < event_rec_list.Count; i++)
			{
				EventRec er = event_rec_list[i] as EventRec;
				if (sender == er.Sender && event_name == er.EventName && !er.Processed)
				{
					er.Processed = true;
					return er.ScriptDelegate;
				}
			}
			return null;
		}

		/// <summary>
		/// Undocumented. (.NET CF)
		/// </summary>
		public void ResetProcessedState(object sender, string event_name)
		{
			for (int i = 0; i < event_rec_list.Count; i++)
			{
				EventRec er = event_rec_list[i] as EventRec;
				if (sender == er.Sender && event_name == er.EventName)
					er.Processed = false;
			}
		}

		/// <summary>
		/// Register standard handlers. (.NET CF)
		/// </summary>
		void RegisterStandardHandlers()
		{
			RegisterEventHandler(typeof(EventHandler), "Click", new EventHandler(OnClickHandler));
			RegisterEventHandler(typeof(EventHandler), "CursorChanged", new EventHandler(OnCursorChangedHandler));
			RegisterEventHandler(typeof(EventHandler), "DockChanged", new EventHandler(OnDockChangedHandler));
			RegisterEventHandler(typeof(EventHandler), "DoubleClick", new EventHandler(OnDoubleClickHandler));
			RegisterEventHandler(typeof(EventHandler), "EnabledChanged", new EventHandler(OnEnabledChangedHandler));
			RegisterEventHandler(typeof(EventHandler), "Enter", new EventHandler(OnEnterHandler));
			RegisterEventHandler(typeof(EventHandler), "Leave", new EventHandler(OnLeaveHandler));
			RegisterEventHandler(typeof(EventHandler), "MouseEnter", new EventHandler(OnMouseEnterHandler));
			RegisterEventHandler(typeof(EventHandler), "MouseLeave", new EventHandler(OnMouseLeaveHandler));
			RegisterEventHandler(typeof(EventHandler), "Resize", new EventHandler(OnResizeHandler));
			RegisterEventHandler(typeof(EventHandler), "TextChanged", new EventHandler(OnTextChangedHandler));

//			RegisterEventHandler(typeof(EventHandler), "GotFocus", new EventHandler(OnGotFocusHandler));
//			RegisterEventHandler(typeof(EventHandler), "LostFocus", new EventHandler(OnLostFocusHandler));
		}

		/// <summary> OnGotFocus </summary>
		void OnGotFocusHandler(object sender, EventArgs x)
		{
			scripter.ApplyDelegateHost("GotFocus", sender, x);
		}

		/// <summary> OnLostFocus </summary>
		void OnLostFocusHandler(object sender, EventArgs x)
		{
			scripter.ApplyDelegateHost("LostFocus", sender, x);
		}

		/// <summary> OnClick </summary>
		void OnClickHandler(object sender, EventArgs x)
		{
			scripter.ApplyDelegateHost("Click", sender, x);
		}

		/// <summary> OnCursorChanged </summary>
		void OnCursorChangedHandler(object sender, EventArgs x)
		{
			scripter.ApplyDelegateHost("CursorChanged", sender, x);
		}

		/// <summary> OnDockChanged </summary>
		void OnDockChangedHandler(object sender, EventArgs x)
		{
			scripter.ApplyDelegateHost("DockChanged", sender, x);
		}

		/// <summary> OnDoubleClick </summary>
		void OnDoubleClickHandler(object sender, EventArgs x)
		{
			scripter.ApplyDelegateHost("DoubleClick", sender, x);
		}

		/// <summary> OnEnabledChanged </summary>
		void OnEnabledChangedHandler(object sender, EventArgs x)
		{
			scripter.ApplyDelegateHost("EnabledChanged", sender, x);
		}

		/// <summary> OnEnter </summary>
		void OnEnterHandler(object sender, EventArgs x)
		{
			scripter.ApplyDelegateHost("Enter", sender, x);
		}

		/// <summary> OnLeave </summary>
		void OnLeaveHandler(object sender, EventArgs x)
		{
			scripter.ApplyDelegateHost("Leave", sender, x);
		}

		/// <summary> OnMouseEnter </summary>
		void OnMouseEnterHandler(object sender, EventArgs x)
		{
			scripter.ApplyDelegateHost("MouseEnter", sender, x);
		}

		/// <summary> OnMouseLeave </summary>
		void OnMouseLeaveHandler(object sender, EventArgs x)
		{
			scripter.ApplyDelegateHost("MouseLeave", sender, x);
		}

		/// <summary> OnResize </summary>
		void OnResizeHandler(object sender, EventArgs x)
		{
			scripter.ApplyDelegateHost("Resize", sender, x);
		}

		/// <summary> OnTextChanged </summary>
		void OnTextChangedHandler(object sender, EventArgs x)
		{
			scripter.ApplyDelegateHost("TextChanged", sender, x);
		}
	}
	#endregion EventDispatcher Class
}
