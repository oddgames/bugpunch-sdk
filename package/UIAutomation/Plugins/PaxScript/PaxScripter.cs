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

#define release

using System;
using System.ComponentModel;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.IO;
using SL;

namespace PaxScript.Net
{
	#region PaxScripter Class
	/// <summary>
	/// paxScripter component class.
	/// </summary>
	public class PaxScripter: PaxComponent
	{
		/// <summary>
		/// Represents kernel of scripter.
		/// </summary>
		public BaseScripter scripter;

		/// <summary>
		/// Sign which shows search of all available types is allowed.
		/// </summary>
		public static bool AUTO_IMPORTING_SWITCH = true;

		/// <summary>
		/// Represents scripter state.
		/// </summary>
		private ScripterState state;

		/// <summary>
		/// ChangeStateHandler private field.
		/// </summary>
		private ChangeStateHandler ch = null;

		/// <summary>
		/// ChangeStateHandler event.
		/// </summary>
		public event ChangeStateHandler OnChangeState
		{
			add
			{
				ch += value;
			}
			remove
			{
				ch -= value;
			}
		}

        /// <summary>
        /// RunCount field shows if there were nested calls of
        /// Code.Run method (RunCount > 1)
        /// </summary>
        public int RunCount = 0; // October 26, 2007

		/// <summary>
		/// RunningHandler private field.
		/// </summary>
		internal RunningHandler rh = null;

		/// <summary>
		/// RunningHandler event.
		/// </summary>
		public event RunningHandler OnRunning
		{
			add
			{
				rh += value;
			}
			remove
			{
				rh -= value;
			}
		}

		/// <summary>
		/// PaxExceptionHandler private field.
		/// </summary>
		internal PaxExceptionHandler re = null;

		/// <summary>
		/// RunningHandler event.
		/// </summary>
		public event PaxExceptionHandler OnPaxException
		{
			add
			{
				re += value;
			}
			remove
			{
				re -= value;
			}
		}

#if !cf && !SILVERLIGHT
		/// <summary>
		/// PaxScripter constructor.
		/// </summary>
		public PaxScripter(IContainer container)
		{
			scripter = new BaseScripter(this);
			scripter.RegisterParser(new CSharp_Parser());
			scripter.RegisterParser(new VB_Parser());
			scripter.RegisterParser(new Pascal_Parser());
            state = ScripterState.None;
			SetState(ScripterState.Init);

			if (container != null)
				container.Add(this);
		}
#endif
		/// <summary>
		/// PaxScripter constructor.
		/// </summary>
		public PaxScripter()
		{
			scripter = new BaseScripter(this);
			scripter.RegisterParser(new CSharp_Parser());
			scripter.RegisterParser(new VB_Parser());
			scripter.RegisterParser(new Pascal_Parser());
            state = ScripterState.None;
			SetState(ScripterState.Init);
		}

		/// <summary>
		/// Sets up scripter state.
		/// </summary>
		void SetState(ScripterState value)
		{
			if (ch != null && state != value)
			{
				ch(this, new ChangeStateEventArgs(state, value));
			}
			state = value;
		}

		/// <summary>
		/// Creates new error object.
		/// </summary>
		void RaiseError(string message)
		{
			scripter.CreateErrorObject(message);
			SetState(ScripterState.Error);
		}

		/// <summary>
		/// Creates new error object.
		/// </summary>
		void RaiseErrorEx(string message, params object[] p)
		{
			scripter.CreateErrorObjectEx(message, p);
			SetState(ScripterState.Error);
		}

		/// <summary>
		/// Matches scripter state.
		/// </summary>
		void MatchState(ScripterState s)
		{
			if (state != s)
			{
				// incorect scripter state
				RaiseErrorEx(Errors.PAX0004, s.ToString());
			}
		}

		/// <summary>
		/// Returns id of method Main.
		/// </summary>
		int GetEntryId()
		{
			return scripter.GetEntryId();
		}

		/// <summary>
		/// Returns id of a method.
		/// </summary>
		int GetMethodId(string full_name)
		{
			int id = scripter.GetMethodIdEx(full_name);
//			if (id <= 0)
//				RaiseErrorEx(Errors.PAX0005, full_name);

			return id;
		}

        /// <summary>
        /// Returns id of a method.
        /// </summary>
        int GetMethodIdSafe(string full_name)
        {
            return scripter.GetMethodIdEx(full_name);
        }

		/// <summary>
		/// Returns id of a method.
		/// </summary>
		int GetMethodId(string full_name, string signature)
		{
			int id = scripter.GetMethodId(full_name, signature);
			if (id <= 0)
				RaiseErrorEx(Errors.PAX0005, full_name + signature);
			return id;
		}

		/// <summary>
		/// Registeres host-defined object for scripter.
		/// </summary>
		public void RegisterInstance(string instance_name, object instance)
		{
			scripter.RegisterInstance(instance_name, instance);
		}

		/// <summary>
		/// Reregisteres instance of a host-defined type.
		/// </summary>
		public void ReregisterInstance(string instance_name, object instance)
		{
			if (!scripter.symbol_table.ReregisterInstance(instance_name, instance))
				scripter.RegisterInstance(instance_name, instance);
		}

		/// <summary>
		/// Registeres host-defined variable for scripter.
		/// </summary>
		public void RegisterVariable(string instance_name, Type type)
		{
			scripter.RegisterVariable(instance_name, type);
		}

		/// <summary>
		/// Registeres all types of assembly for scripter.
		/// </summary>
		public void RegisterAssembly(string path)
		{
			scripter.RegisterAssembly(path);
		}

		/// <summary>
		/// Registeres all types of assembly for scripter.
		/// </summary>
		public void RegisterAssembly(Assembly assembly)
		{
			scripter.RegisterAssembly(assembly);
		}

		/// <summary>
		/// Registeres all types of assembly for scripter.
		/// </summary>
		public void RegisterAssemblyWithPartialName(string name)
		{
            scripter.RegisterAssemblyWithPartialName(name);
        }

		/// <summary>
		/// Registeres a host-defined type for scripter.
		/// </summary>
		public void RegisterType(Type t)
		{
			scripter.RegisterType(t, true);
		}

		/// <summary>
		/// Registeres a host-defined type for scripter.
		/// </summary>
		public void RegisterType(Type t, bool recursive)
		{
			scripter.RegisterType(t, recursive);
		}

        /// <summary>
        /// Registeres a constructor of host-defined type for scripter.
        /// </summary>
        public void RegisterConstructor(ConstructorInfo info)
        {
            scripter.RegisterConstructor(info);
        }

        /// <summary>
        /// Registeres a method of host-defined type for scripter.
        /// </summary>
        public void RegisterMethod(MethodInfo info)
        {
            scripter.RegisterMethod(info);
        }

        /// <summary>
        /// Registeres a field of host-defined type for scripter.
        /// </summary>
        public void RegisterField(FieldInfo info)
        {
            scripter.RegisterField(info);
        }

        /// <summary>
        /// Registeres a property of host-defined type for scripter.
        /// </summary>
        public void RegisterProperty(PropertyInfo info)
        {
            scripter.RegisterProperty(info);
        }

        /// <summary>
        /// Registeres an event of host-defined type for scripter.
        /// </summary>
        public void RegisterEvent(EventInfo info)
        {
            scripter.RegisterEvent(info);
        }

		/// <summary>
		/// Removes all modules from scripter.
		/// </summary>
		public void Reset()
		{
			scripter.Reset();
			SetState(ScripterState.Init);
		}

        /// <summary>
        /// Removes all modules from scripter.
        /// </summary>
        public void ResetModules()
        {
            scripter.ResetModules();
            SetState(ScripterState.Init);
        }

		/// <summary>
		/// Removes all modules from scripter.
		/// </summary>
		public void ResetCompileStage()
		{
			scripter.ResetCompileStage();
			SetState(ScripterState.Init);
		}

		/// <summary>
		/// Discards error of scripter.
		/// </summary>
		public void DiscardError()
		{
			scripter.DiscardError();
		}

		/// <summary>
		/// Adds new module to scripter.
		/// </summary>
		public void AddModule(string module_name)
		{
			if (HasErrors) return;
//			MatchState(ScripterState.Init);
			scripter.AddModule(module_name, "CSharp");
		}

		/// <summary>
		/// Adds new module to scripter.
		/// </summary>
		public void AddModule(string module_name, string language_name)
		{
			if (HasErrors) return;

			switch (state)
			{
				case ScripterState.Init:
				case ScripterState.ReadyToCompile:
				{
					scripter.AddModule(module_name, language_name);
					if (HasErrors)
						SetState(ScripterState.Error);
					else
						state = ScripterState.ReadyToCompile;
					break;
				}
				default:
				{
					MatchState(ScripterState.Init);
					break;
				}
			}
		}

		/// <summary>
		/// Adds a scrap of source code script to scripter.
		/// </summary>
		public void AddCode(string module_name, string text)
		{
			if (HasErrors) return;
			switch (state)
			{
				case ScripterState.Init:
				case ScripterState.ReadyToCompile:
				{
					scripter.AddCode(module_name, text);
					if (HasErrors)
						SetState(ScripterState.Error);
					else
						state = ScripterState.ReadyToCompile;
					break;
				}
				default:
				{
					MatchState(ScripterState.Init);
					break;
				}
			}
		}

		/// <summary>
		/// Adds a code line to a module and appends  '\u000D' +  '\u000A'
		/// </summary>
		public void AddCodeLine(string module_name, string text)
		{
			if (HasErrors) return;
			switch (state)
			{
				case ScripterState.Init:
				case ScripterState.ReadyToCompile:
				{
					scripter.AddCodeLine(module_name, text);
					if (HasErrors)
						SetState(ScripterState.Error);
					else
						state = ScripterState.ReadyToCompile;
					break;
				}
				default:
				{
					MatchState(ScripterState.Init);
					break;
				}
			}
		}
#if !PORTABLE
		/// <summary>
		/// Adds a scrap of source code script from file to scripter.
		/// </summary>
		public void AddCodeFromFile(string module_name, string path)
		{
			if (HasErrors) return;
			switch (state)
			{
				case ScripterState.Init:
				case ScripterState.ReadyToCompile:
				{
					scripter.AddCodeFromFile(module_name, path);
					if (HasErrors)
						SetState(ScripterState.Error);
					else
						state = ScripterState.ReadyToCompile;
					break;
				}
				default:
				{
					MatchState(ScripterState.Init);
					break;
				}
			}
		}
#endif
		/// <summary>
		/// Loads compiled module from a stream and adds it to scripter.
		/// </summary>
		public void LoadCompiledModule(string module_name, Stream s)
		{
			#if !release
			RaiseError("This feature is available only in registered version of paxScript.NET!");
			#else
			if (HasErrors) return;
			switch (state)
			{
				case ScripterState.Init:
				case ScripterState.ReadyToCompile:
				{
					scripter.LoadCompiledModule(module_name, s);
					if (HasErrors)
						SetState(ScripterState.Error);
					else
						state = ScripterState.ReadyToCompile;
					break;
				}
				default:
				{
					MatchState(ScripterState.Init);
					break;
				}
			}
			#endif
		}
#if !PORTABLE
		/// <summary>
		/// Loads compiled module from a file and adds it to scripter.
		/// </summary>
		public void LoadCompiledModuleFromFile(string module_name, string file_name)
		{
			#if !release
			RaiseError("This feature is available only in registered version of paxScript.NET!");
			#else
			if (HasErrors) return;
			switch (state)
			{
				case ScripterState.Init:
				case ScripterState.ReadyToCompile:
				{
					scripter.LoadCompiledModuleFromFile(module_name, file_name);
					if (HasErrors)
						SetState(ScripterState.Error);
					else
						state = ScripterState.ReadyToCompile;
					break;
				}
				default:
				{
					MatchState(ScripterState.Init);
					break;
				}
			}
			#endif
		}
#endif
		/// <summary>
		/// Saves compiled module to a stream.
		/// </summary>
		public void SaveCompiledModule(string module_name, Stream s)
		{
			#if !release
			RaiseError("This feature is available only in registered version of paxScript.NET!");
			#else
			if (HasErrors) return;
			MatchState(ScripterState.ReadyToLink);
			if (HasErrors) return;
			scripter.SaveCompiledModule(module_name, s);
			if (HasErrors)
				SetState(ScripterState.Error);
			#endif
		}
#if !PORTABLE
		/// <summary>
		/// Saves compiled module to a file.
		/// </summary>
		public void SaveCompiledModuleToFile(string module_name, string file_name)
		{
			#if !release
			RaiseError("This feature is available only in registered version of paxScript.NET!");
			#else
			if (HasErrors) return;
			MatchState(ScripterState.ReadyToLink);
			if (HasErrors) return;
			scripter.SaveCompiledModuleToFile(module_name, file_name);
			if (HasErrors)
				SetState(ScripterState.Error);
			#endif
		}
#endif
		/// <summary>
		/// Complies all modules.
		/// </summary>
		public void Compile()
		{
			if (HasErrors) return;

			switch (state)
			{
				case ScripterState.Init:
				{
					SetState(ScripterState.ReadyToCompile);
					break;
				}
				case ScripterState.ReadyToCompile:
				{
					// ok
					break;
				}
			}

			MatchState(ScripterState.ReadyToCompile);
			if (HasErrors) return;

			SetState(ScripterState.Compiling);
			scripter.Compile();
			if (HasErrors)
				SetState(ScripterState.Error);
			else
				SetState(ScripterState.ReadyToLink);
		}

		/// <summary>
		/// Links all compiled modules.
		/// </summary>
		public void Link()
		{
			if (HasErrors) return;

			switch (state)
			{
				case ScripterState.Init:
				case ScripterState.ReadyToCompile:
				{
					Compile();
					if (HasErrors) return;
					break;
				}
				case ScripterState.ReadyToLink:
				{
					// ok
					break;
				}
			}

			MatchState(ScripterState.ReadyToLink);
			if (HasErrors) return;

			state = ScripterState.Linking;
			scripter.Link();
			if (HasErrors)
				SetState(ScripterState.Error);
			else
				SetState(ScripterState.ReadyToRun);
		}

		/// <summary>
		/// Runs script.
		/// </summary>
		public void Run(RunMode rm, params object[] parameters)
		{
			scripter.TerminatedFlag = false;

			if (HasErrors) return;

			switch (state)
			{
				case ScripterState.Init:
				case ScripterState.ReadyToCompile:
				case ScripterState.ReadyToLink:
				{
					Link();
					if (HasErrors) return;
					SetState(ScripterState.Running);
					scripter.CallStaticConstructors();
					if (HasErrors)
						break;
					scripter.CallMain(rm, parameters);
					break;
				}
				case ScripterState.ReadyToRun:
				{
					SetState(ScripterState.Running);
					scripter.CallStaticConstructors();
					if (HasErrors)
						break;
					scripter.CallMain(rm, parameters);
					break;
				}
				case ScripterState.Paused:
				{
					SetState(ScripterState.Running);
					scripter.Resume(rm);
					break;
				}
				case ScripterState.Terminated:
				{
					scripter.ResetRunStage();
					SetState(ScripterState.Running);
					scripter.CallStaticConstructors();
					if (HasErrors) break;
					scripter.CallMain(rm, parameters);
					break;
				}
				default:
				{
					MatchState(ScripterState.ReadyToRun);
					break;
				}
			}

			if (HasErrors)
				SetState(ScripterState.Error);
			else
			{
				if (scripter.Paused)
					SetState(ScripterState.Paused);
				else
					SetState(ScripterState.Terminated);
			}
		}

		/// <summary>
		/// Calls a script-defined function.
		/// </summary>
		public object Invoke(RunMode rm, object target, string method_name,
							params object[] parameters)
		{
			object result = null;

			if (HasErrors) return result;

			switch (state)
			{
				case ScripterState.Init:
				case ScripterState.ReadyToCompile:
				case ScripterState.ReadyToLink:
				{
					Link();
					if (HasErrors) return result;
					SetState(ScripterState.Running);
					scripter.CallStaticConstructors();
					if (HasErrors)
						break;
					int method_id = GetMethodId(method_name);
					if (HasErrors) return result;
					result = scripter.code.CallMethodEx(rm, target, method_id, parameters);
					break;
				}
				case ScripterState.Running:
				{
					SetState(ScripterState.Running);
					int method_id = GetMethodId(method_name);
					if (HasErrors) return result;
					result = scripter.code.CallMethodEx(rm, target, method_id, parameters);
					break;
				}
				case ScripterState.ReadyToRun:
				{
					SetState(ScripterState.Running);
					scripter.CallStaticConstructors();
					if (HasErrors)
						break;
					int method_id = GetMethodId(method_name);
					if (HasErrors) return result;
					result = scripter.code.CallMethodEx(rm, target, method_id, parameters);
					break;
				}
				case ScripterState.Paused:
				{
					SetState(ScripterState.Running);
					scripter.Resume(rm);
					break;
				}
				case ScripterState.Terminated:
				{
                    if (RunCount == 0)
    					scripter.ResetRunStage(); // October 26, 2007
					SetState(ScripterState.Running);
					int method_id = GetMethodId(method_name);
					if (HasErrors) return result;
					result = scripter.code.CallMethodEx(rm, target, method_id, parameters);
					break;
				}
				default:
				{
					MatchState(ScripterState.ReadyToRun);
					break;
				}
			}

			if (HasErrors)
				SetState(ScripterState.Error);
			else
			{
				if (scripter.Paused)
					SetState(ScripterState.Paused);
				else
					SetState(ScripterState.Terminated);
			}

			return result;
		}

		/// <summary>
		/// Calls a script-defined function by id.
		/// </summary>
		public object Invoke(RunMode rm, object target, int method_id,
							params object[] parameters)
		{
			object result = null;

			if (HasErrors) return result;

			switch (state)
			{
				case ScripterState.Init:
				case ScripterState.ReadyToCompile:
				case ScripterState.ReadyToLink:
				{
					Link();
					if (HasErrors) return result;
					SetState(ScripterState.Running);
					scripter.CallStaticConstructors();
					if (HasErrors)
						break;
					if (HasErrors) return result;
					result = scripter.code.CallMethodEx(rm, target, method_id, parameters);
					break;
				}
				case ScripterState.Running:
				case ScripterState.ReadyToRun:
				{
					SetState(ScripterState.Running);
					scripter.CallStaticConstructors();
					if (HasErrors)
						break;
					if (HasErrors) return result;
					result = scripter.code.CallMethodEx(rm, target, method_id, parameters);
					break;
				}
				case ScripterState.Paused:
				{
					SetState(ScripterState.Running);
					scripter.Resume(rm);
					break;
				}
				case ScripterState.Terminated:
				{
                    if (RunCount == 0)
                        scripter.ResetRunStage();
					SetState(ScripterState.Running);
//					scripter.CallStatickConstructors();
//					if (HasErrors) break;
					if (HasErrors) return result;
					result = scripter.code.CallMethodEx(rm, target, method_id, parameters);
					break;
				}
				default:
				{
					MatchState(ScripterState.ReadyToRun);
					break;
				}
			}

			if (HasErrors)
				SetState(ScripterState.Error);
			else
			{
				if (scripter.Paused)
					SetState(ScripterState.Paused);
				else
					SetState(ScripterState.Terminated);
			}

			return result;
		}

		/// <summary>
		/// Evaluates an expression.
		/// </summary>
		public object Eval(string expr)
		{
			if (!HasErrors)
				switch (state)
				{
					case ScripterState.Running:
					case ScripterState.Paused:
					case ScripterState.Terminated:
					{
						return scripter.Eval(expr);
					}
					default:
					{
						return null;
					}
				}
			return null;
		}

		/// <summary>
		/// Evaluates an expression.
		/// </summary>
		public object Eval(string expr, PaxLanguage language)
		{
			if (state == ScripterState.Init)
			{
				EvalHelper eh = new EvalHelper();
				
				RegisterType(typeof(EvalHelper));
				RegisterInstance("_eh", eh);
				AddModule("1", language.ToString());
				if (language == PaxLanguage.CSharp)
					AddCode("1", "_eh.result=" + expr + ";");
				else
					AddCode("1", "_eh.result=" + expr);
				Run(RunMode.Run);
				Reset();
				return eh.result;
			}
			else
				return null;
		}


		/// <summary>
		/// Adds a breakpoint to script.
		/// </summary>
		public Breakpoint AddBreakpoint(string module_name, int line_number)
		{
			return scripter.AddBreakpoint(module_name, line_number);
		}

		/// <summary>
		/// Removes a breakpoint from script.
		/// </summary>
		public void RemoveBreakpoint(Breakpoint bp)
		{
			scripter.RemoveBreakpoint(bp);
		}

		/// <summary>
		/// Removes a breakpoint from script.
		/// </summary>
		public void RemoveBreakpoint(string module_name, int line_number)
		{
			scripter.RemoveBreakpoint(module_name, line_number);
		}

		/// <summary>
		/// Removes all breakpoints.
		/// </summary>
		public void RemoveAllBreakpoints()
		{
			scripter.RemoveAllBreakpoints();
		}

		/// <summary>
		/// Registeres event handler. (.NET CF)
		/// </summary>
		public void RegisterEventHandler(Type event_handler_type,
										 string event_name,
										 Delegate d)
		{
			scripter.RegisterEventHandler(event_handler_type, event_name, d);
		}

		/// <summary>
		/// Unregisteres event handler. (.NET CF)
		/// </summary>
		public void UnregisterEventHandler(Type event_handler_type,
										 string event_name)
		{
			scripter.UnregisterEventHandler(event_handler_type, event_name);
		}

		/// <summary>
		/// Applies script-defined delegate by host-defined delegate. (.NET CF)
		/// </summary>
		public void ApplyDelegate(string event_name, params object[] p)
		{
			scripter.ApplyDelegateHost(event_name, p);
		}

		/// <summary>
		/// Convertes a script-defined object to host object.
		/// </summary>
		public object ScriptObjectToHostObject(object target)
		{
			if (target.GetType() == typeof(ObjectObject))
				return (target as ObjectObject).Instance;
			else
				return target;
		}

		/// <summary>
		/// Forbids usage of namespace in a script.
		/// </summary>
		public void ForbidNamespace(string namespace_name)
		{
			scripter.ForbidNamespace(namespace_name);
		}

		/// <summary>
		/// Forbids usage of type in a script.
		/// </summary>
		public void ForbidType(Type t)
		{
			scripter.ForbidType(t);
		}

		/// <summary>
		/// Convertes a host-defined object to script object.
		/// </summary>
		public object HostObjectToScriptObject(object target)
		{
			return scripter.ToScriptObject(target);
		}

		/// <summary>
		/// Terminates script.
		/// </summary>
		public void Terminate()
		{
			scripter.TerminatedFlag = true;
		}

        /// <summary>
        /// Returns list of results.
        /// </summary>
        public ResultList Result_List
        {
            get
            {
                return scripter.Result_List;
            }
        }

		/// <summary>
		/// Returns 'true', if script has errors.
		/// </summary>
		public bool HasErrors
		{
			get
			{
				return scripter.IsError();
			}
		}

		/// <summary>
		/// Returns 'true', if script has warnings.
		/// </summary>
		public bool HasWarnings
		{
			get
			{
				return Warning_List.Count > 0;
			}
		}

		/// <summary>
		/// Returns list of errors.
		/// </summary>
		public ErrorList Error_List
		{
			get
			{
				return scripter.Error_List;
			}
		}

		/// <summary>
		/// Returns list of warnings.
		/// </summary>
		public ErrorList Warning_List
		{
			get
			{
				return scripter.Warning_List;
			}
		}

		/// <summary>
		/// Returns list of breakpoints.
		/// </summary>
		public BreakpointList Breakpoint_List
		{
			get
			{
				return scripter.Breakpoint_List;
			}
		}

		/// <summary>
		/// Returns list of registered objects.
		/// </summary>
		public PaxHashTable RegisteredInstances
		{
			get
			{
				return scripter.UserInstances;
			}
		}

		/// <summary>
		/// Returns CallStack object.
		/// </summary>
		public CallStack Call_Stack
		{
			get
			{
				return scripter.Call_Stack;
			}
		}

		/// <summary>
		/// Returns list of modules.
		/// </summary>
		public ModuleList Module_List
		{
			get
			{
				return scripter.Modules;
			}
		}

		/// <summary>
		/// Returns current line number (at compile-time or at run-time).
		/// </summary>
		public int CurrentLineNumber
		{
			get
			{
				return scripter.CurrentLineNumber;
			}
		}

		/// <summary>
		/// Returns current line (at compile-time or at run-time).
		/// </summary>
		public string CurrentLine
		{
			get
			{
				return scripter.CurrentLine;
			}
		}

		/// <summary>
		/// Returns current module (at compile-time or at run-time).
		/// </summary>
		public string CurrentModule
		{
			get
			{
				return scripter.CurrentModule;
			}
		}

		/// <summary>
		/// Returns state of scripter without checking (never use it explicitly).
		/// </summary>
		public void SetInternalState(ScripterState value)
		{
			state = value;
		}

		/// <summary>
		/// Returns state of scripter.
		/// </summary>
		public ScripterState State
		{
			get
			{
				return state;
			}
			set
			{
				if (state == value)
					return;

				switch (state)
				{
					case ScripterState.Init:
					{
						SetState(value);
						break;
					}
					case ScripterState.Terminated:
					{
						if (value == ScripterState.Running)
							SetState(value);
						else
							MatchState(ScripterState.Terminated);
						break;
					}
					default:
					{
						MatchState(ScripterState.Terminated);
						break;
					}
				}
			}
		}

		/// <summary>
		/// If 'true', you can refer to methods of a registered instance without
		/// having to specify the instance
		/// </summary>
		public bool DefaultInstanceMethods
		{
			get
			{
				return scripter.DefaultInstanceMethods;
			}
			set
			{
				scripter.DefaultInstanceMethods = value;
			}
		}

		public bool SwappedArguments
		{
			get
			{
				return scripter.SwappedArguments;
			}
		}

		public void RegisterOperatorHelper(string name, MethodInfo m)
		{
			scripter.RegisterOperatorHelper(name, m);
		}
#if !PORTABLE
        /// <summary>
        /// Adds code to a module from a file.
        /// </summary>
        public string ParseASPXFile(string path)
        {
            return scripter.ParseASPXFile(path);
        }
#endif
        public bool DebugMode
        {
            get
            {
                return scripter.code.debugging;
            }
            set
            {
                scripter.code.debugging = value;
            }

        }
		
	}
	#endregion PaxScripter Class

	#region ScripterState Enum
	/// <summary>
	/// Represents scripter states.
	/// </summary>
	public enum ScripterState
	{
		/// <summary>
		/// Initial state.
		/// </summary>
		None,

		/// <summary>
		/// Scripter does not contain any module.
		/// </summary>
		Init,

		/// <summary>
		/// Scripter contains at least one module and code.
		/// </summary>
		ReadyToCompile,

		/// <summary>
		/// Scripter compiles modules.
		/// </summary>
		Compiling,

		/// <summary>
		/// All modules are compiled. Scripter is ready for linking stage.
		/// </summary>
		ReadyToLink,

		/// <summary>
		/// Scripter links modules
		/// </summary>
		Linking,

		/// <summary>
		/// All modules are compiled and linked. Scripter is ready to run script.
		/// </summary>
		ReadyToRun,

		/// <summary>
		/// Scripter runs script.
		/// </summary>
		Running,

		/// <summary>
		/// Script running has been terminated.
		/// </summary>
		Terminated,

		/// <summary>
		/// Script running has been paused.
		/// </summary>
		Paused,

		/// <summary>
		/// Error has raised.
		/// </summary>
		Error
	};
	#endregion ScripterState Enum

	#region ChangeStateEventArgs Class
	/// <summary>
	/// Represents arguments of ChangeStateHandler events
	/// </summary>
	public class ChangeStateEventArgs: System.EventArgs
	{
		/// <summary>
		/// Represents old state of scripter.
		/// </summary>
		public ScripterState OldState;
		/// <summary>
		/// Represents new state of scripter.
		/// </summary>
		public ScripterState NewState;

		/// <summary>
		/// Constructor.
		/// </summary>
		public ChangeStateEventArgs(ScripterState old_state, ScripterState new_state)
		{
			OldState = old_state;
			NewState = new_state;
		}

		/// <summary>
		/// Returns old state scripter as string.
		/// </summary>
		public string OldStateAsString
		{
			get
			{
				return OldState.ToString();
			}
		}

		/// <summary>
		/// Returns new state scripter as string.
		/// </summary>
		public string NewStateAsString
		{
			get
			{
				return NewState.ToString();
			}
		}
	}
	#endregion ChangeStateEventArgs Class

	#region ChangeStateHandler Delegate
	public delegate void ChangeStateHandler(PaxScripter sender, ChangeStateEventArgs e);
	#endregion ChangeStateHandler Delegate

	#region RunningHandler Delegate
	public delegate void RunningHandler(PaxScripter sender);
	#endregion RunningHandler Delegate

	#region PaxExceptionHandler Delegate
	public delegate void PaxExceptionHandler(PaxScripter sender, Exception e);
	#endregion PaxExceptionHandler Delegate
}

public class EvalHelper
{
	public object result;
}
