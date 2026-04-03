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
using System.IO;
using System.Collections;
using System.Reflection;
#if full
using System.Reflection.Emit;
#endif
using SL;

namespace PaxScript.Net
{
	#region BaseScripter Class
    /// <summary>
    /// Represents kernel of scripter.
    /// </summary>
    public sealed class BaseScripter
    {
        /// <summary>
        /// List of standard types.
        /// </summary>
        internal StandardTypeList StandardTypes = new StandardTypeList();

        /// <summary>
        /// List of types which are available for scripter.
        /// </summary>
        internal StringList available_types;

        /// <summary>
        /// Undocumented.
        /// </summary>
        PaxHashTable conv_list = new PaxHashTable();

        /// <summary>
        /// Undocumented.
        /// </summary>
        EventDispatcher event_dispatcher;

        /// <summary>
        /// List of modules.
        /// </summary>
        internal ModuleList module_list;

        /// <summary>
        /// Symbol table.
        /// </summary>
        internal SymbolTable symbol_table;

        /// <summary>
        /// List of parsers registered by scripter.
        /// </summary>
        internal ParserList parser_list;

        /// <summary>
        /// Represents p-code.
        /// </summary>
        internal Code code;

        /// <summary>
        /// List of names.
        /// </summary>
        internal StringList names = new StringList(false);

        /// <summary>
        /// List of errors of scripter.
        /// </summary>
        internal ErrorList Error_List;

        /// <summary>
        /// List of warnings at compile time.
        /// </summary>
        internal ErrorList Warning_List;

        /// <summary>
        /// List of registered types.
        /// </summary>
        internal RegisteredTypeList RegisteredTypes;

        /// <summary>
        /// List of registered namespaces.
        /// </summary>
        internal StringList RegisteredNamespaces;

        /// <summary>
        /// List of forbidden namespaces.
        /// </summary>
        internal StringList ForbiddenNamespaces;

        /// <summary>
        /// List of forbidden types.
        /// </summary>
        internal StringList ForbiddenTypes;

        /// <summary>
        /// List of user types.
        /// </summary>
        internal PaxHashTable UserTypes;

        /// <summary>
        /// List of user constructors.
        /// </summary>
        internal PaxArrayList UserConstructors;

        /// <summary>
        /// List of user methods.
        /// </summary>
        internal PaxArrayList UserMethods;

        /// <summary>
        /// List of user fields.
        /// </summary>
        internal PaxArrayList UserFields;

        /// <summary>
        /// List of user properties.
        /// </summary>
        internal PaxArrayList UserProperties;

        /// <summary>
        /// List of user events.
        /// </summary>
        internal PaxArrayList UserEvents;

        /// <summary>
        /// List of user namespaces.
        /// </summary>
        internal StringList UserNamespaces;

        /// <summary>
        /// List of instances registered by RegisterInstance.
        /// </summary>
        internal PaxHashTable UserInstances;

        /// <summary>
        /// List of instances registered by RegisterVariable.
        /// </summary>
        internal PaxHashTable UserVariables;

        /// <summary>
        /// List of conditional directives.
        /// </summary>
        internal StringList PPDirectiveList;

        /// <summary>
        /// PaxScripter object which ownes BaseScripter object.
        /// </summary>
        internal PaxScripter Owner;

#if full
		/// <summary>
		/// Current domain.
		/// </summary>
		internal AppDomain currentDomain;

		/// <summary>
		/// AssemblyName object of generated assembly.
		/// </summary>
		internal AssemblyName PaxAssemblyName;

		/// <summary>
		/// AssemblyBuilder object of generated assembly.
		/// </summary>
		internal AssemblyBuilder PaxAssemblyBuilder;

		/// <summary>
		/// ModuleBuilder object of generated assembly.
		/// </summary>
		internal ModuleBuilder PaxModuleBuilder;
#endif

        /// <summary>
        /// Sign which shows that reflection is used by interpreter.
        /// </summary>
        internal bool SIGN_REFLECTION = true;

        /// <summary>
        /// Sign which shows that brief syntax of C# programs is allowed.
        /// </summary>
        internal bool SIGN_BRIEF_SYNTAX = true;

        /// <summary>
        /// Id of 'Main' method.
        /// </summary>
        internal int EntryId;

        /// <summary>
        /// Binding flags of protected members.
        /// </summary>
        public BindingFlags protected_binding_flags = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

        public BindingFlags public_binding_flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

        /// <summary>
        /// Returns 'true', if paxScript seaches protected members.
        /// </summary>
        public bool SearchProtected = true;

        /// <summary>
        /// If 'true', you can refer to methods of a registered instance without
        /// having to specify the instance
        /// </summary>
        internal bool DefaultInstanceMethods = false;

        internal bool SwappedArguments = false;

        internal PaxHashTable OperatorHelpers;

        bool RESET_COMPILE_STAGE_SWITCH = false;

        internal bool TerminatedFlag = false;

        internal Conversion conversion = new Conversion();

        internal ResultList Result_List;

        /// <summary>
        /// Constructor.
        /// </summary>
        internal BaseScripter(PaxScripter owner)
        {
            this.Owner = owner;

            symbol_table = new SymbolTable(this);
            code = new Code(this);
            module_list = new ModuleList(this);
            parser_list = new ParserList();

            Error_List = new ErrorList(this);
            Warning_List = new ErrorList(this);

            PPDirectiveList = new StringList(false);

            RegisteredTypes = new RegisteredTypeList();
            RegisteredNamespaces = new StringList(false);

            UserTypes = new PaxHashTable();
            UserConstructors = new PaxArrayList();
            UserMethods = new PaxArrayList();
            UserFields = new PaxArrayList();
            UserProperties = new PaxArrayList();
            UserEvents = new PaxArrayList();

            UserNamespaces = new StringList(false);
            ForbiddenNamespaces = new StringList(false);
            UserInstances = new PaxHashTable();
            UserVariables = new PaxHashTable();
            OperatorHelpers = new PaxHashTable();

            available_types = new StringList(false);
            ForbiddenTypes = new StringList(false);

            EntryId = 0;
            Result_List = new ResultList(this);

#if full
			// Get the current application domain for the current thread.
			currentDomain = AppDomain.CurrentDomain;

			// Create assembly in current currentDomain
			PaxAssemblyName = new AssemblyName();
			PaxAssemblyName.Name = "PaxAssembly";

			// Define a dynamic assembly in the 'currentDomain'.
			PaxAssemblyBuilder =
			   currentDomain.DefineDynamicAssembly
						   (PaxAssemblyName, AssemblyBuilderAccess.Run);

			// Define a dynamic module in "TempAssembly" assembly.
			PaxModuleBuilder = PaxAssemblyBuilder.DefineDynamicModule("PaxModule");

#endif
            event_dispatcher = new EventDispatcher(this, "PaxEventDispatcher");

            if (PaxScripter.AUTO_IMPORTING_SWITCH)
            {
                RegisterAvailableNamespaces();
            }
        }

        /// <summary>
        /// Imports all types from an assembly instance.
        /// RPR NEW
        /// </summary>
        internal void RegisterAssembly(Assembly assembly)
        {
            if (assembly == null)
                throw new ArgumentNullException("BaseScripter.RegisterAssembly: 'assembly' parameter must not be null.");
            try
            { 

                Type[] types = assembly.GetTypes();
                foreach (Type t in types)
                {
                    if (t != null)
                    {
                        available_types.AddObject(t.FullName, t);

                        string s = t.Namespace;
                        if ((s != null) && (s != ""))
                        {
                            UserNamespaces.Add(s);
                        }
                    }
                }

            }
            catch (ReflectionTypeLoadException) {  }

        }

        /// <summary>
        /// Imports all types from assembly.
        /// </summary>
        internal void RegisterAssembly(string path)
        {
#if PORTABLE
            RegisterAssembly(Assembly.Load(path));
#else
            AssemblyName assem_name = new AssemblyName();
            assem_name.CodeBase = path;
            RegisterAssembly(Assembly.Load(assem_name));
#endif
        }


        /// <summary>
        /// Imports all types from assembly.
        /// </summary>
        internal void RegisterAssemblyWithPartialName(string name)
        {
#if cf || SILVERLIGHT
#if PORTABLE
            RegisterAssembly(Assembly.Load(name));
#else
			AssemblyName assem_name = new AssemblyName();
			assem_name.Name = name;
			RegisterAssembly(Assembly.Load(assem_name));
#endif
#else
            Assembly assem = Assembly.LoadWithPartialName(name);
            RegisterAssembly(assem);
#endif
        }

        /// <summary>
        /// Imports all namespaces from assembly.
        /// </summary>
        internal void RegisterAvailableNamespaces()
        {
#if cf || SILVERLIGHT
			RegisterAssembly(Assembly.GetExecutingAssembly());
#else
            Assembly[] assems = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly assem in assems)
            {
                RegisterAssembly(assem);
            }
#endif
        }

        /// <summary>
        /// Return 'true', if there is a predefined namespace with name full_name.
        /// </summary>
        internal bool HasPredefinedNamespace(string full_name)
        {
            return UserNamespaces.IndexOf(full_name) >= 0;
        }

        /// <summary>
        /// Searches available type with name full_name.
        /// </summary>
        internal Type FindAvailableType(string full_name, bool upcase)
        {
            int i = available_types.IndexOf(full_name);
            if (i == - 1)
            {
                if (upcase)
                    i = available_types.UpcaseIndexOf(full_name);
            }
            if (i == -1)
                return null;
            else
                return available_types.Objects [i] as Type;
        }

        /// <summary>
        /// Registeres type for scripter.
        /// </summary>
        internal void RegisterAvailableType(Type t)
        {
            available_types.AddObject(t.FullName, t);
        }

        /// <summary>
        /// Searches imported namespaces member. Returns MemberObject object.
        /// </summary>
        internal MemberObject FindImportedNamespaceMember(ClassObject n, string member_name, bool upcase)
        {
            string s = n.FullName + "." + member_name;
            Type t = FindAvailableType(s, upcase);
            if (t != null)
            {
                int type_id = symbol_table.RegisterType(t, false);
                return GetMemberObject(type_id);
            }
            string s1 = s + ".";
            for (int i = 0; i < UserNamespaces.Count; i++)
            {
                string s2 = UserNamespaces [i] + ".";
                if (s1.Length < s2.Length)
                    s2 = s2.Substring(0, s1.Length - 1);

                if (s1 == s2)
                {
                    int namespace_id = symbol_table.RegisterNamespace(s);
                    return GetMemberObject(namespace_id);
                }
            }

            return null;
        }

        /// <summary>
        /// Registeres parser for scripter.
        /// </summary>
        internal void RegisterParser(BaseParser p)
        {
            parser_list.Add(p);
        }

        /// <summary>
        /// Registeres type for scripter.
        /// </summary>
        internal void RegisterType(Type t, bool recursive)
        {
            if (UserTypes.ContainsKey(t))
            {
                UserTypes [t] = recursive;
            } else
                UserTypes.Add(t, recursive);
        }

        /// <summary>
        /// Registeres constructor for scripter.
        /// </summary>
        internal void RegisterConstructor(ConstructorInfo info)
        {
            if (!UserConstructors.Contains(info))
                UserConstructors.Add(info);
        }

        /// <summary>
        /// Registeres method for scripter.
        /// </summary>
        internal void RegisterMethod(MethodInfo info)
        {
            if (!UserMethods.Contains(info))
                UserMethods.Add(info);
        }

        /// <summary>
        /// Registeres field for scripter.
        /// </summary>
        internal void RegisterField(FieldInfo info)
        {
            if (!UserFields.Contains(info))
                UserFields.Add(info);
        }

        /// <summary>
        /// Registeres property for scripter.
        /// </summary>
        internal void RegisterProperty(PropertyInfo info)
        {
            if (!UserProperties.Contains(info))
                UserProperties.Add(info);
        }

        /// <summary>
        /// Registeres event for scripter.
        /// </summary>
        internal void RegisterEvent(EventInfo info)
        {
            if (!UserEvents.Contains(info))
                UserEvents.Add(info);
        }

        /// <summary>
        /// Registers namespace for scripter.
        /// </summary>
        internal void RegisterNamespace(string name)
        {
            UserNamespaces.Add(name);
        }

        /// <summary>
        /// Registers instance for scripter.
        /// </summary>
        internal void RegisterInstance(string name, object instance)
        {
            if (instance == null)
				// Error in RegisterInstance method. You have tried to register object which is not initialized
                CreateErrorObjectEx(Errors.PAX0009, name);
            else
            {
                if (UserVariables.ContainsKey(name))
                {
                    UserVariables.Remove(name);
                }

                if (UserInstances.ContainsKey(name))
                {
                    UserInstances [name] = instance;
                } else
                    UserInstances.Add(name, instance);
            }
        }

        /// <summary>
        /// Registers variable for scripter.
        /// </summary>
        internal void RegisterVariable(string name, Type type)
        {
            if (UserInstances.ContainsKey(name))
            {
                UserInstances.Remove(name);
            }

            if (UserVariables.ContainsKey(name))
            {
                UserVariables [name] = type;
            } else
                UserVariables.Add(name, type);
        }

        internal void RegisterOperatorHelper(string name, MethodInfo m)
        {
            OperatorHelpers.Add(name, m);
        }

        /// <summary>
        /// Adds module to scripter.
        /// </summary>
        internal Module AddModule(string module_name, string language_name)
        {
            int i = module_list.IndexOf(module_name);
            Module m;
            if (i == -1)
            {
                m = new Module(this, module_name, language_name);
                module_list.Add(m);
            } else
                m = module_list [i];
            return m;
        }

        /// <summary>
        /// Loads compiled module from a stream.
        /// </summary>
        internal Module LoadCompiledModule(string module_name, Stream s)
        {
            int i = module_list.IndexOf(module_name);
            Module m;
            if (i == -1)
            {
                m = new Module(this, module_name, "");
                module_list.Add(m);
            } else
                m = module_list [i];
            m.PreLoadFromStream(s);
            return m;
        }
#if !PORTABLE
        /// <summary>
        /// Loads compiled module from a file.
        /// </summary>
        internal Module LoadCompiledModuleFromFile(string module_name, string file_name)
        {
            using (FileStream fs = new FileStream(file_name, FileMode.Open))
            {
                Module m = LoadCompiledModule(module_name, fs);
//				AddCodeFromFile(module_name, m.FileName);
                return m;
            }
        }
#endif
        /// <summary>
        /// Saves compiled module to a stream.
        /// </summary>
        internal Module SaveCompiledModule(string module_name, Stream s)
        {
            int i = module_list.IndexOf(module_name);
            Module m;
            if (i != -1)
            {
                m = module_list [i];
                m.SaveToStream(s);
            } else
                m = null;
            return m;
        }
#if !PORTABLE
        /// <summary>
        /// Saves compiled module to a file.
        /// </summary>
        internal Module SaveCompiledModuleToFile(string module_name, string file_name)
        {
            using (FileStream fs = new FileStream(file_name, FileMode.Create))
            {
                return SaveCompiledModule(module_name, fs);
            }
        }
#endif
        /// <summary>
        /// Adds code to a module.
        /// </summary>
        internal Module AddCode(string module_name, string text)
        {
            int i = module_list.IndexOf(module_name);
            if (i == -1)
				// module not found
                RaiseException(Errors.PAX0001);
            Module m = module_list [i];
            m.Text += text;
            return m;
        }

        /// <summary>
        /// Adds a code line to a module and appends  '\u000D' +  '\u000A'
        /// </summary>
        internal Module AddCodeLine(string module_name, string text)
        {
            int i = module_list.IndexOf(module_name);
            if (i == -1)
				// module not found
                RaiseException(Errors.PAX0001);
            Module m = module_list [i];
            m.Text += text + '\u000D' + '\u000A';
            return m;
        }
#if !PORTABLE
        /// <summary>
        /// Adds code to a module from a file.
        /// </summary>
        internal Module AddCodeFromFile(string module_name, string path)
        {
            if (File.Exists(path))
            {
                int i = module_list.IndexOf(module_name);
                if (i == -1)
					// module not found
                    RaiseException(Errors.PAX0001);
                Module m = module_list [i];

                using (StreamReader sr = new StreamReader(path))
                {
                    m.Text = sr.ReadToEnd();
                }
                m.FileName = path;
                return m;
            } else
            {
                // Required file '{0}' could not be found.
                CreateErrorObjectEx(Errors.CS0014, path);
                return null;
            }
        }
#endif
        /// <summary>
        /// Compiles module.
        /// </summary>
        void CompileModule(Module m, BaseParser p)
        {
            m.BeforeCompile();
#if !PORTABLE
            if (m.IsSourceCodeModule)
            {
                try
                {
                    m.LoadFromStream();
                    if (File.Exists(m.FileName))
                        AddCodeFromFile(m.Name, m.FileName);
                } catch (Exception e)
                {
                    CreateErrorObject(e.Message);
                    // Cannot load compiled module
                    CreateErrorObjectEx(Errors.PAX0003, m.Name, m.FileName);
                }
            } else
#endif
            {
                if (p == null)
                {
                    // unknown language
                    CreateErrorObjectEx(Errors.PAX0002, m.LanguageName);
                } else
                {
                    try
                    {
                        p.Init(this, m);

                        p.Gen(code.OP_BEGIN_MODULE, m.NameIndex, (int)m.Language, 0);
                        p.Gen(code.OP_SEPARATOR, m.NameIndex, 0, 0);
                        p.Gen(code.OP_BEGIN_USING, p.RootNamespaceId, 0, 0);
                        p.Gen(code.OP_BEGIN_USING, p.SystemNamespaceId, 0, 0);
                        p.Gen(code.OP_CHECKED, symbol_table.TRUE_id, 0, 0);

                        if (m.Language == PaxLanguage.VB)
                            p.Gen(code.OP_UPCASE_ON, 0, 0, 0);
                        else
                            p.Gen(code.OP_UPCASE_OFF, 0, 0, 0);
                        p.Gen(code.OP_EXPLICIT_ON, 0, 0, 0);

                        p.Call_SCANNER();
                        p.Parse_Program();

                        p.Gen(code.OP_RESTORE_CHECKED_STATE, 0, 0, 0);
                        p.Gen(code.OP_END_USING, p.RootNamespaceId, 0, 0);
                        p.Gen(code.OP_END_USING, p.SystemNamespaceId, 0, 0);
                        p.Gen(code.OP_HALT, 0, 0, 0);
                        p.Gen(code.OP_END_MODULE, m.NameIndex, 0, 0);

                        if (!p.ConditionalDirectivesAreCompleted())
							// #endif directive expected
                            p.RaiseError(true, Errors.CS1027);
                    } catch (Errors.PaxScriptException)
                    {
                        // already added
                    } catch (Exception e)
                    {
                        Error_List.Add(new ScriptError(this, e.Message));
                        LastError.E = e;
                    }
                }
            }
            m.AfterCompile();
        }

        /// <summary>
        /// Compiles all modules.
        /// </summary>
        internal void Compile()
        {
            int i;

            if (RESET_COMPILE_STAGE_SWITCH)
            {
                foreach (string key in UserInstances.Keys)
                {
                    object v = UserInstances [key];
                    symbol_table.RegisterInstance(key, v, true);
                }

                foreach (string key in UserVariables.Keys)
                {
                    Type v = UserVariables [key] as Type;
                    symbol_table.RegisterVariable(key, v, true);
                }
            } else
            {
                RegisteredTypes.Clear();
                RegisteredNamespaces.Clear();
                symbol_table.Init();

                for (i = 0; i < UserNamespaces.Count; i++)
                {
                    string s = UserNamespaces [i];
                    symbol_table.RegisterNamespace(s);
                }

                foreach (Type key in UserTypes.Keys)
                {
                    bool v = (bool)UserTypes [key];
                    symbol_table.RegisterType(key, v);
                }

                foreach (ConstructorInfo key in UserConstructors)
                    symbol_table.RegisterConstructor(key);
                foreach (MethodInfo key in UserMethods)
                    symbol_table.RegisterMethod(key);
                foreach (FieldInfo key in UserFields)
                    symbol_table.RegisterField(key);
                foreach (PropertyInfo key in UserProperties)
                    symbol_table.RegisterProperty(key);
                foreach (EventInfo key in UserEvents)
                    symbol_table.RegisterEvent(key);

                foreach (string key in UserInstances.Keys)
                {
                    object v = UserInstances [key];
                    symbol_table.RegisterInstance(key, v, false);
                }

                foreach (string key in UserVariables.Keys)
                {
                    Type v = UserVariables [key] as Type;
                    symbol_table.RegisterVariable(key, v, false);
                }
            }
            symbol_table.RESET_COMPILE_STAGE_CARD = symbol_table.Card;

            for (i = 0; i < module_list.Count; i++)
            {
                Module m = module_list [i];
                BaseParser p = parser_list.FindParser(m.LanguageName);
                CompileModule(m, p);

                if (IsError())
                    break;
            }
        }

        /// <summary>
        /// Linkes all modules.
        /// </summary>
        internal void Link()
        {
            Dump();
            code.CreateClassObjects();
            if (IsError())
                return;
            code.RemoveEvalOp();
            if (IsError())
                return;
            code.SetTypes();
            if (IsError())
                return;
            code.CheckTypes();
            if (IsError())
                return;

            code.InsertEventHandlers();
            if (IsError())
                return;

            code.AdjustCalls();
            if (IsError())
                return;

            foreach (string key in UserInstances.Keys)
            {
                object v = UserInstances [key];
                if (v is Delegate)
                {
                    code.CreatePatternMethod(v as Delegate);
                    code.Card++;
                    code.SetInstruction(code.Card, code.OP_HALT, 0, 0, 0);
                }
            }

            EntryId = GetEntryId();
            if (EntryId == 0)
            {
                CreateErrorObjectEx(Errors.CS5001, "");
                return;
            } else if (EntryId < 0)
            {
                CreateErrorObject(Errors.CS0017); // more than 1 entry point
                return;
            }

            code.Optimization();
            if (IsError())
                return;
            code.LinkGoTo();
            Dump();
        }

        /// <summary>
        /// Evaluates an expression.
        /// </summary>
        internal object Eval(string expr)
        {
            Module m = GetModule(code.n);
            if (m == null)
                return null;

            BaseParser p = parser_list.FindParser(m.LanguageName);

            if (p == null)
            {
                // unknown language
                CreateErrorObjectEx(Errors.PAX0002, m.LanguageName);
                return null;
            }

            int sub_id = code.GetSubId(code.n);

            if (sub_id == - 1)
                return null;

            code.SaveState();
            symbol_table.SaveState();
            int code_card = code.Card;
            int curr_n = code.n;
            int result_id = symbol_table.AppVar();
            int id = 0;

            try
            {
                p.InitExpression(this, m, sub_id, expr);
                if (m.Language == PaxLanguage.VB)
                    p.Gen(code.OP_UPCASE_ON, 0, 0, 0);
                else
                    p.Gen(code.OP_UPCASE_OFF, 0, 0, 0);
                p.Call_SCANNER();
                id = p.Parse_Expression();
            } catch (Errors.PaxScriptException)
            {
                // already added
            } catch (Exception e)
            {
                Error_List.Add(new ScriptError(this, e.Message));
                LastError.E = e;
            }

            if (IsError())
            {
                code.RestoreState();
                symbol_table.RestoreState();
                return null;
            }

            if (code_card == code.Card)
                result_id = id;
            else
            {
                IntegerStack level_stack = code.RecreateLevelStack(curr_n);
                if (level_stack.Count == 0)
                {
                    code.RestoreState();
                    symbol_table.RestoreState();
                    return null;
                }

                IntegerStack class_stack = code.RecreateClassStack(curr_n);

                p.Gen(code.OP_ASSIGN, result_id, id, result_id);
                p.Gen(code.OP_HALT, 0, 0, 0);

                code.RemoveEvalOpEx(code_card, level_stack);
                code.SetTypesEx(code_card);
                code.CheckTypesEx(code_card, level_stack.Peek(), class_stack);
                code.AdjustCallsEx(code_card);
                code.LinkGoToEx(code_card);

                code.n = code_card;
                code.Run(RunMode.Run);
            }

            object result = GetValue(result_id);

            code.RestoreState();
            symbol_table.RestoreState();

            return result;
        }

        /// <summary>
        /// Calls static constructors.
        /// </summary>
        internal void CallStaticConstructors()
        {
            code.CallStaticConstructors();
            if (IsError())
                return;
#if full
			if (SIGN_REFLECTION)
			{
				symbol_table.CreateReflectedTypes();
				CreateDispatchType();
			}
#endif
        }

        /// <summary>
        /// Returns last error from error list.
        /// </summary>
        internal ScriptError LastError
        {
            get
            {
                if (Error_List.Count == 0)
                    return null;
                else
                    return Error_List [Error_List.Count - 1] as ScriptError;
            }
        }

        /// <summary>
        /// Raises exception.
        /// </summary>
        internal void RaiseException(string message)
        {
            Errors.RaiseException(message);
        }

        /// <summary>
        /// Raises exception.
        /// </summary>
        internal void RaiseExceptionEx(string message, params object[] p)
        {
            Errors.RaiseException(String.Format(message, p));
        }

        /// <summary>
        /// Returns 'true', if scripter has an error.
        /// </summary>
        internal bool IsError()
        {
            return Error_List.Count > 0;
        }

        /// <summary>
        /// Discards errors.
        /// </summary>
        internal void DiscardError()
        {
            Error_List.Clear();
            Warning_List.Clear();
        }

        /// <summary>
        /// Creates error object and adds it to error list.
        /// </summary>
        internal void CreateErrorObject(string message)
        {
            int pcode_line = code.n;
            if (pcode_line == 0)
                pcode_line = code.Card;
            Dump();
            if (!Error_List.HasError(message, pcode_line))
            {
                ScriptError error_object = new ScriptError(this, message);
                Error_List.Add(error_object);
            }
        }

        /// <summary>
        /// Creates error object and adds it to error list.
        /// </summary>
        internal void CreateErrorObjectEx(string message, params object[] p)
        {
            CreateErrorObject(String.Format(message, p));
        }

        /// <summary>
        /// Creates warning object and adds it to error list.
        /// </summary>
        internal void CreateWarningObject(string message)
        {
            int pcode_line = code.n;
            if (pcode_line == 0)
                pcode_line = code.Card;
            if (!Warning_List.HasError(message, pcode_line))
                Warning_List.Add(new ScriptError(this, message));
        }

        /// <summary>
        /// Creates warning object and adds it to error list.
        /// </summary>
        internal void CreateWarningObjectEx(string message, params object[] p)
        {
            CreateWarningObject(String.Format(message, p));
        }
#if !PORTABLE
        /// <summary>
        /// Undocumented.
        /// </summary>
        internal void ShowErrors()
        {
            foreach (ScriptError error_object in Error_List)
            {
                Console.WriteLine("Error:");
                Console.WriteLine(error_object.Message);
                Console.WriteLine("Module: " + error_object.ModuleName);
                Console.WriteLine("LineNumber: " + error_object.LineNumber.ToString());
                Console.WriteLine("PCodeLineNumber: " + error_object.PCodeLineNumber.ToString());
                Console.WriteLine(error_object.Line);
            }
        }

        /// <summary>
        /// Undocumented.
        /// </summary>
        internal void ShowWarnings()
        {
            foreach (ScriptError error_object in Warning_List)
            {
                Console.WriteLine("Warning:");
                Console.WriteLine(error_object.Message);
                Console.WriteLine("Module: " + error_object.ModuleName);
                Console.WriteLine("LineNumber: " + error_object.LineNumber.ToString());
                Console.WriteLine("PCodeLineNumber: " + error_object.PCodeLineNumber.ToString());
                Console.WriteLine(error_object.Line);
            }
        }
#endif
        /// <summary>
        /// Resumes paused script.
        /// </summary>
        internal void Resume(RunMode rm)
        {
            code.Run(rm);
        }

        /// <summary>
        /// Returns 'true', if id represents a standard type.
        /// </summary>
        internal bool IsStandardType(int id)
        {
            return (id >= 1) && (id <= StandardTypes.Count);
        }

        /// <summary>
        /// Returns id of 'Main' method.
        /// </summary>
        internal int GetEntryId()
        {
            IntegerList l = new IntegerList(false);
            for (int i = 0; i < symbol_table.Card; i++)
            {
                if ((symbol_table [i].Kind == MemberKind.Method) && (symbol_table [i].Name == "Main"))
                {
                    for (int j = 1; j < code.Card; j++)
                    {
                        if ((code [j].op == code.OP_CREATE_METHOD) && (code [j].arg1 == i))
                        {
                            int k = j + 1;
                            while (code[k].op == code.OP_ADD_MODIFIER)
                            {
                                if (code [k].arg2 == (int)Modifier.Static)
                                {
                                    l.Add(i);
                                    break;
                                }
                                k++;
                            }
                        }
                    }
                }
            }
            if (l.Count == 0)       // there is not an entry point
                return 0;
            else if (l.Count == 1)  // ok
                return l [0];
            else                    // there is more than 1 entry point
                return -1;
        }

        /// <summary>
        /// Returns id of type with name type_name.
        /// </summary>
        internal int GetTypeId(string type_name)
        {
            for (int i = symbol_table.Card; i >= 1; i--)
                if (symbol_table [i].Kind == MemberKind.Type)
                if (symbol_table [i].Name == type_name)
                    return i;
            return 0;
        }

        /// <summary>
        /// Returns type id of id.
        /// </summary>
        internal int GetTypeId(int id)
        {
            return symbol_table [id].TypeId;
        }

        /// <summary>
        /// Assigns type id of id with type_id.
        /// </summary>
        internal void SetTypeId(int id, int type_id)
        {
            symbol_table [id].TypeId = type_id;
        }

        /// <summary>
        /// Returns id of method.
        /// </summary>
        internal int GetMethodId(string full_name)
        {
            return symbol_table.GetMethodId(full_name);
        }

        /// <summary>
        /// Returns id of method.
        /// </summary>
        internal int GetMethodIdEx(string full_name)
        {
            return symbol_table.GetMethodIdEx(full_name);
        }

        /// <summary>
        /// Returns id of method.
        /// </summary>
        internal int GetMethodId(string full_name, string signature)
        {
            return symbol_table.GetMethodId(full_name, signature);
        }

        /// <summary>
        /// Calls method defined in script.
        /// </summary>
        internal object CallMethod(RunMode rm, object target, int sub_id, params object[] p)
        {
            return code.CallMethod(rm, target, sub_id, p);
        }

        /// <summary>
        /// Calls 'Main' method defined in script.
        /// </summary>
        internal object CallMain(RunMode rm, params object[] p)
        {
            return CallMethod(rm, null, EntryId, p);
        }

        /// <summary>
        /// Returns current instruction of p-code.
        /// </summary>
        internal ProgRec GetCurrentIstruction()
        {
            return code.GetCurrentIstruction();
        }

        /// <summary>
        /// Assigns value to id.
        /// </summary>
        internal void PutValue(int id, object value)
        {
            symbol_table [id].Value = value;
        }

        /// <summary>
        /// Returns value of id.
        /// </summary>
        internal object GetValue(int id)
        {
            return symbol_table [id].Value;
        }

        /// <summary>
        /// Assigns value to id.
        /// </summary>
        internal void PutVal(int id, object value)
        {
            symbol_table [id].Val = value;
        }

        /// <summary>
        /// Returns value of id.
        /// </summary>
        internal object GetVal(int id)
        {
            return symbol_table [id].Val;
        }

        /// <summary>
        /// Returns ClassObject object of id which represents a class.
        /// </summary>
        internal ClassObject GetClassObject(int id)
        {
            return (ClassObject)symbol_table [id].Val;
        }

        /// <summary>
        /// Returns ClassObject object of id which represents a class.
        /// </summary>
        internal ClassObject GetClassObjectEx(int id)
        {
            ClassObject c = GetClassObject(id);
            if (c.IsRefType)
                c = FindOriginalType(c);
            return c;
        }

        /// <summary>
        /// Returns MemberObject object of id which represents a member.
        /// </summary>
        internal MemberObject GetMemberObject(int id)
        {
            return (MemberObject)symbol_table [id].Val;
        }

        /// <summary>
        /// Returns PropertyObject object of id which represents a property.
        /// </summary>
        internal PropertyObject GetPropertyObject(int id)
        {
            return (PropertyObject)symbol_table [id].Val;
        }

        /// <summary>
        /// Returns EventObject object of id which represents an event.
        /// </summary>
        internal EventObject GetEventObject(int id)
        {
            return (EventObject)symbol_table [id].Val;
        }

        /// <summary>
        /// Returns IndexObject object of id which represents an element of array
        /// or indexer.
        /// </summary>
        internal IndexObject GetIndexObject(int id)
        {
            return (IndexObject)symbol_table [id].Val;
        }

        /// <summary>
        /// Returns FunctionObject object of id which represents a method.
        /// </summary>
        internal FunctionObject GetFunctionObject(int id)
        {
            return (FunctionObject)symbol_table [id].Val;
        }

        /// <summary>
        /// Performes type match of assignment [id1] = [id2]
        /// </summary>
        internal bool MatchAssignment(int id1, int id2)
        {
            if (conversion.ExistsImplicitConversion(this, id2, id1))
                return true;

            int t1 = symbol_table [id1].TypeId;
            int t2 = symbol_table [id2].TypeId;
            ClassObject c1 = GetClassObject(t1);
            ClassObject c2 = GetClassObject(t2);

            if (c1.Name == c2.Name)
                return true;

            if (c1.Class_Kind == ClassKind.Enum)
            {
                if (c2.Class_Kind == ClassKind.Enum)
                    return false;
                else
                    return MatchTypes(c1.UnderlyingType, c2);
            }
            if (c2.Class_Kind == ClassKind.Enum)
            {
                if (c1.Class_Kind == ClassKind.Enum)
                    return false;
                else
                    return MatchTypes(c1, c2.UnderlyingType);
            }
            return false;
        }

        /// <summary>
        /// Returns origin type of a reference type.
        /// </summary>
        internal ClassObject FindOriginalType(ClassObject c)
        {
            if (!c.IsRefType)
                return c;
            int l = c.Name.Length - 1;
            if (l <= 0)
                return null;
            string s = c.Name.Substring(0, l);
            int level = c.OwnerId;
            int id = symbol_table.LookupID(s, level, false);
            if (id > 0)
                return (ClassObject)GetVal(id);
            else
                return null;
        }

        /// <summary>
        /// Matches 2 script-defined types.
        /// </summary>
        internal bool MatchTypes(ClassObject c1, ClassObject c2)
        {
            if (c1 == null)
                return false;
            if (c2 == null)
                return false;

            if (c1.IsRefType)
            {
                c1 = FindOriginalType(c1);
                if (c1 == null)
                    return false;
            }

            if (c2.IsRefType)
            {
                c2 = FindOriginalType(c2);
                if (c2 == null)
                    return false;
            }

            if (c1 == c2) // identity
                return true;
            else if (conversion.ExistsImplicitNumericConversion(c1.Id, c2.Id))
                return true;
            else
            {
                if (c1.Class_Kind == ClassKind.Enum)
                {
                    if (c2.Class_Kind == ClassKind.Enum)
                        return false;
                    else
                    {
                        if (c1.UnderlyingType == null)
                            return false;
                        return MatchTypes(c1.UnderlyingType, c2);
                    }
                } else if (c2.Class_Kind == ClassKind.Enum)
                {
                    if (c1.Class_Kind == ClassKind.Enum)
                        return false;
                    else
                    {
                        if (c2.UnderlyingType == null)
                            return false;
                        return MatchTypes(c1, c2.UnderlyingType);
                    }
                } else
                    return false;
            }
        }
#if !PORTABLE
        /// <summary>
        /// Undocumented.
        /// </summary>
        internal void ShowType(Type t)
        {
            ConstructorInfo[] constructors = t.GetConstructors();
            foreach (ConstructorInfo c in constructors)
            {
                Console.WriteLine("constructor:");
                Console.WriteLine(c.ToString());
            }

            FieldInfo[] fields = t.GetFields();
            foreach (FieldInfo c in fields)
            {
                Console.WriteLine("field:");
                Console.WriteLine(c.ToString());
            }

            MethodInfo[] methods = t.GetMethods();
            foreach (MethodInfo c in methods)
            {
                Console.WriteLine("method:");
                Console.WriteLine(c.ToString());
            }

            PropertyInfo[] properties = t.GetProperties();
            foreach (PropertyInfo c in properties)
            {
                Console.WriteLine("property:");
                Console.WriteLine(c.ToString());
            }

            EventInfo[] events = t.GetEvents();
            foreach (EventInfo c in events)
            {
                Console.WriteLine("event:");
                Console.WriteLine(c.ToString());
            }
        }
#endif
        /// <summary>
        /// Removes all modules from scripter.
        /// </summary>
        internal void Reset()
        {
            Owner.RunCount = 0; // October 26, 2007

            RESET_COMPILE_STAGE_SWITCH = false;

            TerminatedFlag = false;

            EntryId = 0;

            DiscardError();

            symbol_table.Reset();
            code.Reset();

            module_list.Clear();
            PPDirectiveList.Clear();
            RegisteredTypes.Clear();
            RegisteredNamespaces.Clear();

            UserInstances.Clear();
            UserVariables.Clear();
            OperatorHelpers.Clear();

            event_dispatcher.Reset();

            conv_list.Clear();

            ForbiddenNamespaces.Clear();
            ForbiddenTypes.Clear();
        }

        /// <summary>
        /// Removes all modules from scripter.
        /// </summary>
        internal void ResetModules()
        {
            Owner.RunCount = 0; 

            RESET_COMPILE_STAGE_SWITCH = false;

            TerminatedFlag = false;

            EntryId = 0;

            DiscardError();

            symbol_table.Reset();
            code.Reset();
            module_list.Clear();
            conv_list.Clear();
        }

        /// <summary>
        /// Removes all modules from scripter.
        /// </summary>
        internal void ResetCompileStage()
        {
            Owner.RunCount = 0; // October 26, 2007

            RESET_COMPILE_STAGE_SWITCH = true;

            EntryId = 0;

            DiscardError();

            symbol_table.ResetCompileStage();
            code.Reset();
            module_list.Clear();
            PPDirectiveList.Clear();

            for (int i = RegisteredTypes.Count - 1; i >= 0; i--)
            {
                RegisteredType rt = RegisteredTypes [i];
                if (rt.Id > symbol_table.RESET_COMPILE_STAGE_CARD)
                    RegisteredTypes.Delete(i);
            }

            for (int i = RegisteredNamespaces.Count - 1; i >= 0; i--)
            {
                int id = (int)RegisteredNamespaces.Objects [i];
                if (id > symbol_table.RESET_COMPILE_STAGE_CARD)
                    RegisteredNamespaces.Delete(i);
            }
        }

        /// <summary>
        /// Resets run-time structures.
        /// </summary>
        internal void ResetRunStage()
        {
            Owner.RunCount = 0; // October 26, 2007

            conv_list.Clear();
            code.ResetRunStageStructs();
        }

        /// <summary>
        /// Convertes a value to script object.
        /// </summary>
        internal ObjectObject ToScriptObject(object v)
        {
            if (v == null)
                return null;

            Type t = v.GetType();

            if (t == typeof(ObjectObject))
                return (ObjectObject)v;
            else if (t == typeof(IndexObject))
            {
                return ToScriptObject((v as IndexObject).Value);
            } else
            {
                ObjectObject result = (ObjectObject)conv_list [v];
                if (result != null)
                    return result;

                int class_id = RegisteredTypes.FindRegisteredTypeId(t);
                ClassObject c = (ClassObject)GetVal(class_id);
                result = c.CreateObject();
                result.Instance = v;
#if full
				if (c.IsDelegate)
				{
					Delegate[] l = (v as Delegate).GetInvocationList();

					MethodInfo m;
					object target;

					foreach (Delegate d in l)
					{
						m = d.Method;
						target = d.Target;

						t = m.DeclaringType;
						int type_id = symbol_table.RegisterType(t, false);
						int sub_id = symbol_table.RegisterMethod(m, type_id);
						FunctionObject f = GetFunctionObject(sub_id);

						result.AddInvocation(target, f);
					}
				}
#endif
                conv_list.Add(v, result); 
                return result;
            }
        }

        /// <summary>
        /// Registeres event handler. (.NET CF)
        /// </summary>
        public void RegisterEventHandler(Type event_handler_type,
										 string event_name,
										 Delegate d)
        {
            event_dispatcher.RegisterEventHandler(event_handler_type, event_name, d);
        }

        /// <summary>
        /// Unregisteres event handler. (.NET CF)
        /// </summary>
        public void UnregisterEventHandler(Type event_handler_type,
										   string event_name)
        {
            event_dispatcher.UnregisterEventHandler(event_handler_type, event_name);
        }

        /// <summary>
        /// Defines event handler.
        /// </summary>
        internal void DefineEventHandler(EventInfo e, FunctionObject pattern_method)
        {
            event_dispatcher.DefineEventHandler(e, pattern_method);
        }

        /// <summary>
        /// Creates dispatch type.
        /// </summary>
        internal void CreateDispatchType()
        {
            event_dispatcher.CreateDispatchType();
        }

        /// <summary>
        /// Creates delegate.
        /// </summary>
        internal Delegate CreateDelegate(object instance, EventInfo e, FunctionObject pattern_method,
									   object script_delegate)
        {
            return event_dispatcher.CreateDelegate(instance, e, pattern_method, script_delegate);
        }

        /// <summary>
        /// Applies delegate.
        /// </summary>
        public static void ApplyDelegate(object scripter, object d, params object[] p)
        {
            BaseScripter s = scripter as BaseScripter;
            s.ApplyDel(s.ToScriptObject(d), p);
        }

        /// <summary>
        /// Applies script-defined delegate by host-defined delegate.
        /// </summary>
        public void ApplyDelegateHost(string event_name, params object[] p)
        {
            object sender = p [0];
            object d = event_dispatcher.LookupScriptDelegate(sender, event_name);
            if (d == null)
            {
                event_dispatcher.ResetProcessedState(sender, event_name);
                d = event_dispatcher.LookupScriptDelegate(sender, event_name);
            }

            ApplyDel(ToScriptObject(d), p);
        }

        /// <summary>
        /// Applies delegate.
        /// </summary>
        void ApplyDel(ObjectObject d, params object[] p)
        {
            object x;
            FunctionObject f;

            if (d == null)
                return;

            d.FindFirstInvocation(out x, out f);
            while (f != null)
            {
                code.CallMethodEx(RunMode.Run, x, f.Id, p);
                d.FindNextInvocation(out x, out f);
            }
        }

        /// <summary>
        /// Adds new breakpoint.
        /// </summary>
        internal Breakpoint AddBreakpoint(string module_name, int line_number)
        {
            Breakpoint bp = new Breakpoint(this, module_name, line_number);
            code.AddBreakpoint(bp);
            return bp;
        }

        /// <summary>
        /// Adds delegates.
        /// </summary>
        internal ObjectObject AddDelegates(ObjectObject d1, ObjectObject d2)
        {
            ClassObject c = d1.Class_Object;
            ObjectObject result = c.CreateObject();

            object x;
            FunctionObject f;
            bool b;

            b = d1.FindFirstInvocation(out x, out f);
            while (b)
            {
                result.AddInvocation(x, f);
                b = d1.FindNextInvocation(out x, out f);
            }

            b = d2.FindFirstInvocation(out x, out f);
            while (b)
            {
                result.AddInvocation(x, f);
                b = d2.FindNextInvocation(out x, out f);
            }
            return result;
        }

        /// <summary>
        /// Removes breakpoint.
        /// </summary>
        internal void RemoveBreakpoint(Breakpoint bp)
        {
            code.RemoveBreakpoint(bp);
        }

        /// <summary>
        /// Removes breakpoint.
        /// </summary>
        internal void RemoveBreakpoint(string module_name, int line_number)
        {
            code.RemoveBreakpoint(module_name, line_number);
        }

        /// <summary>
        /// Removes all breakpoints.
        /// </summary>
        internal void RemoveAllBreakpoints()
        {
            code.RemoveAllBreakpoints();
        }

        /// <summary>
        /// Returns breakpoint list.
        /// </summary>
        internal BreakpointList Breakpoint_List
        {
            get
            {
                return code.Breakpoints;
            }
        }

        /// <summary>
        /// Returns module list.
        /// </summary>
        internal ModuleList Modules
        {
            get
            {
                return module_list;
            }
        }

        /// <summary>
        /// Returns call stack.
        /// </summary>
        internal CallStack Call_Stack
        {
            get
            {
                return code.Call_Stack;
            }
        }

        /// <summary>
        /// Convertes source code line number to p-code line number.
        /// </summary>
        internal int SourceLineToPCodeLine(string module_name, int line_number)
        {
            int i = module_list.IndexOf(module_name);
            if (i == -1)
                return -1;

            Module m = module_list [i];
            for (int n = m.P1; n <= m.P2; n++)
            {
                if (code [n].op == code.OP_SEPARATOR)
                {
                    if (code [n].arg2 == line_number)
                        return n;
                }
            }

            return -1;
        }

        /// <summary>
        /// Returns module which corresponds to p-code line n.
        /// </summary>
        internal Module GetModule(int n)
        {
            return code.GetModule(n);
        }

        /// <summary>
        /// Returns source line number which coresponds to p-code line n.
        /// </summary>
        internal int GetLineNumber(int n)
        {
            return code.GetLineNumber(n);
        }

        /// <summary>
        /// Returns current source line number which coresponds to current
        /// p-code line.
        /// </summary>
        internal int CurrentLineNumber
        {
            get
            {
                return code.CurrentLineNumber;
            }
        }

        /// <summary>
        /// Returns current source line which corresponds to current
        /// p-code line.
        /// </summary>
        internal string CurrentLine
        {
            get
            {
                return code.CurrentLine;
            }
        }

        /// <summary>
        /// Returns module which corresponds to current
        /// p-code line.
        /// </summary>
        internal string CurrentModule
        {
            get
            {
                return code.CurrentModule;
            }
        }

        /// <summary>
        /// Returns 'true', if scripter has been paused.
        /// </summary>
        internal bool Paused
        {
            get
            {
                return code.Paused;
            }
        }

        /// <summary>
        /// Returns 'true', if scripter has been terminated.
        /// </summary>
        internal bool Terminated
        {
            get
            {
                return code.Terminated;
            }
        }

        /// <summary>
        /// Undocumented.
        /// </summary>
        internal void Dump()
        {
            #if dump
            /*names.Dump("names.txt");
			code.Dump("code.txt");
			symbol_table.DumpSymbolTable("symbol_table.txt");
			symbol_table.DumpClasses("classes.txt");*/
            #endif
        }

        /// <summary>
        /// Forbids usage of namespace in a script.
        /// </summary>
        internal void ForbidNamespace(string namespace_name)
        {
            ForbiddenNamespaces.Add(namespace_name);
        }

        /// <summary>
        /// Forbids usage of type in a script.
        /// </summary>
        internal void ForbidType(Type t)
        {
            ForbiddenTypes.Add(t.FullName);
        }

        internal string GetUpcaseNameByNameIndex(int name_index)
        {
            return names [name_index].ToUpper();
        }

        internal void CheckForbiddenType(int id)
        {
            if (symbol_table [id].Kind == MemberKind.Type)
            {
                ClassObject c = GetClassObject(id);
                if (c.Imported && c.ImportedType != null)
                {
                    Attribute[] attrs = Attribute.GetCustomAttributes(c.ImportedType);
                    foreach (Attribute attr in attrs)
                    {
                        if (attr is PaxScriptForbid)
                        {
                            CreateErrorObjectEx(Errors.PAX0007, c.FullName);
                            return;
                        }
                    }
                }

                if (ForbiddenTypes.IndexOf(c.FullName) >= 0)
                {
                    CreateErrorObjectEx(Errors.PAX0007, c.FullName);
                } else
                {
                    char cc;
                    string s = PaxSystem.ExtractOwner(c.FullName, out cc);
                    if (CheckForbiddenNamespace(s))
                    {
                        CreateErrorObjectEx(Errors.PAX0007, c.FullName);
                    }
                }
            }
        }

        internal bool CheckForbiddenNamespace(string s)
        {
            if (ForbiddenNamespaces.IndexOf(s) >= 0)
            {
                return true;
            }
            char cc;
            string c = PaxSystem.ExtractOwner(s, out cc);
            if (c.Length == 0)
            {
                return false;
            }
            return CheckForbiddenNamespace(c);
        }
#if !PORTABLE
        /// <summary>
        /// Adds code to a module from a file.
        /// </summary>
        internal string ParseASPXFile(string path)
        {
            if (File.Exists(path))
            {
                string s;
                using (StreamReader sr = new StreamReader(path))
                {
                    s = sr.ReadToEnd();
                }

                BaseParser p = parser_list.FindParser("CSharp");
                s = p.ParseASPXPage(s);
                return s;
            } else
            {
                // Required file '{0}' could not be found.
                CreateErrorObjectEx(Errors.CS0014, path);
                return null;
            }
        }
#endif
        public string CodeDump()
        {
            return string.Empty; //code.Dump("code.txt");
        }
    }


	#endregion BaseScripter Class
}
