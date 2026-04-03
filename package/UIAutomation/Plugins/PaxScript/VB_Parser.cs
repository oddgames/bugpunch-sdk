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
using SL;

namespace PaxScript.Net
{
	#region VB_Parser Class
	/// <summary>
	/// Parser of VB.NET language.
	/// </summary>
	internal class VB_Parser:  BaseParser
	{

		#region LocalModifier Enum
		/// <summary>
		/// Modifier of local variable.
		/// </summary>
		public enum LocalModifier
		{
			Static,
			Dim,
			Const,
		}
		#endregion LocalModifier Enum

		#region ForLoopRec Class
		/// <summary>
		/// Saves info of for-loop record
		/// </summary>
		class ForLoopRec
		{
			public int id;
			public int step_id;
			public int lg;
			public int lf;
		}
		#endregion ForLoopRec Class

		#region ForLoopStack Class
		/// <summary>
		/// Saves info of nested for-loops
		/// </summary>
		class ForLoopStack
		{
			/// <summary>
			/// Private stack filed.
			/// </summary>
			ObjectStack s = new ObjectStack();

			/// <summary>
			/// Pushes new record into the stack
			/// </summary>
			public void Push(int id, int step_id, int lg, int lf)
			{
				ForLoopRec r = new ForLoopRec();
				r.id = id;
				r.step_id = step_id;
				r.lg = lg;
				r.lf = lf;
				s.Push(r);
			}

			/// <summary>
			/// Pops tomost record from the stack
			/// </summary>
			public void Pop()
			{
				s.Pop();
			}

			/// <summary>
			/// Clears the stack
			/// </summary>
			public void Clear()
			{
				s.Clear();
			}

			/// <summary>
			/// Returns number elements in stack
			/// </summary>
			public int Count
			{
				get
				{
					return s.Count;
				}
			}

			/// <summary>
			/// Returns topmost record of the stack.
			/// </summary>
			public ForLoopRec Top
			{
				get
				{
					return (ForLoopRec) s.Peek();
				}
			}
		}
		#endregion ForLoopStack Class


		IntegerList variable_initializers;
		IntegerList static_variable_initializers;
		IntegerList param_ids;
		IntegerList param_type_ids;
		IntegerList param_mods;
		IntegerList local_variables;

		StringList total_modifier_list;

		bool has_constructor = false;
		bool valid_this_context = false;
		int explicit_intf_id = 0;
		int curr_prop_id = 0;
		int new_type_id = 0;

		bool SKIP_STATEMENT_TERMINATOR = false;

		bool OPTION_STRICT = true;
		bool typeof_expression = false;

		ModifierList enum_modifiers;
		ModifierList class_modifiers;
		ModifierList structure_modifiers;
		ModifierList interface_modifiers;
		ModifierList event_modifiers;
		ModifierList method_modifiers;
		ModifierList constructor_modifiers;
		ModifierList property_modifiers;
		ModifierList delegate_modifiers;
		Types integral_types;

		ForLoopStack for_loop_stack;
		IntegerStack exit_kind_stack;
		IntegerStack with_stack;

		/// <summary>
		/// Parser VB_Parser constructor
		/// </summary>
		public VB_Parser(): base()
		{
			language = "VB";
			scanner = new VB_Scanner(this);
			upcase = true;
			AllowKeywordsInMemberAccessExpressions = true;

			variable_initializers = new IntegerList(false);
			static_variable_initializers = new IntegerList(false);
			param_ids = new IntegerList(true);
			param_type_ids = new IntegerList(true);
			param_mods = new IntegerList(true);
			local_variables = new IntegerList(false);

			total_modifier_list = new StringList(false);
			total_modifier_list.AddObject("Public", Modifier.Public);
			total_modifier_list.AddObject("Protected", Modifier.Protected);
			total_modifier_list.AddObject("Internal", Modifier.Internal);
			total_modifier_list.AddObject("Private", Modifier.Private);
			total_modifier_list.AddObject("Shared", Modifier.Static);
			total_modifier_list.AddObject("Overridable", Modifier.Virtual);
			total_modifier_list.AddObject("NotOverridable", Modifier.Sealed);
			total_modifier_list.AddObject("MustOverride", Modifier.Abstract);
			total_modifier_list.AddObject("Overrides", Modifier.Override);
			total_modifier_list.AddObject("Overloads", Modifier.Overloads);
			total_modifier_list.AddObject("ReadOnly", Modifier.ReadOnly);
			total_modifier_list.AddObject("Friend", Modifier.Friend);
			total_modifier_list.AddObject("Default", Modifier.Default);
			total_modifier_list.AddObject("MustInherit", Modifier.Abstract);
			total_modifier_list.AddObject("Shadows", Modifier.Shadows);
			total_modifier_list.AddObject("NotInheritable", Modifier.Sealed);
			total_modifier_list.AddObject("WithEvents", Modifier.WithEvents);

			keywords.Add("AddHandler");
			keywords.Add("AddressOf");
			keywords.Add("Alias");
			keywords.Add("And");
			keywords.Add("AndAlso");
			keywords.Add("Ansi");
			keywords.Add("As");
			keywords.Add("Assembly");
			keywords.Add("Auto");
			keywords.Add("Boolean");
			keywords.Add("ByRef");
			keywords.Add("Byte");
			keywords.Add("ByVal");
			keywords.Add("Call");
			keywords.Add("Case");
			keywords.Add("Catch");
			keywords.Add("CBool");
			keywords.Add("CByte");
			keywords.Add("CChar");
			keywords.Add("CDate");
			keywords.Add("CDbl");
			keywords.Add("CDec");
			keywords.Add("Char");
			keywords.Add("CInt");
			keywords.Add("Class");
			keywords.Add("CLng");
			keywords.Add("CObj");
			keywords.Add("Const");
			keywords.Add("CShort");
			keywords.Add("CSng");
			keywords.Add("CStr");
			keywords.Add("CType");
			keywords.Add("Date");
			keywords.Add("Decimal");
			keywords.Add("Declare");
			keywords.Add("Default");
			keywords.Add("Delegate");
			keywords.Add("Dim");
			keywords.Add("DirectCast");
			keywords.Add("Do");
			keywords.Add("Double");
			keywords.Add("Each");
			keywords.Add("Else");
			keywords.Add("ElseIf");
			keywords.Add("End");
			keywords.Add("EndIf");
			keywords.Add("Enum");
			keywords.Add("Erase");
			keywords.Add("Error");
			keywords.Add("Event");
			keywords.Add("Exit");
			keywords.Add("False");
			keywords.Add("Finally");
			keywords.Add("For");
			keywords.Add("Friend");
			keywords.Add("Function");
			keywords.Add("Get");
//			keywords.Add("GetType");
			keywords.Add("GoSub");
			keywords.Add("GoTo");
			keywords.Add("Handles");
			keywords.Add("If");
			keywords.Add("Implements");
			keywords.Add("Imports");
			keywords.Add("In");
			keywords.Add("Inherits");
			keywords.Add("Integer");
			keywords.Add("Interface");
			keywords.Add("Is");
            keywords.Add("IsNot");
            keywords.Add("Let");
			keywords.Add("Lib");
			keywords.Add("Like");
			keywords.Add("Long");
			keywords.Add("Loop");
			keywords.Add("Me");
			keywords.Add("Mod");
			keywords.Add("Module");
			keywords.Add("MustInherit");
			keywords.Add("MustOverride");
			keywords.Add("MyBase");
			keywords.Add("MyClass");
			keywords.Add("Namespace");
			keywords.Add("New");
			keywords.Add("Next");
			keywords.Add("Not");
			keywords.Add("Nothing");
			keywords.Add("NotInheritable");
			keywords.Add("NotOverridable");
			keywords.Add("Object");
			keywords.Add("On");
			keywords.Add("Option");
			keywords.Add("Optional");
			keywords.Add("Or");
			keywords.Add("Else");
			keywords.Add("Overloads");
			keywords.Add("Overridable");
			keywords.Add("Overrides");
			keywords.Add("ParamArray");
			keywords.Add("Preserve");
			keywords.Add("Private");
			keywords.Add("Property");
			keywords.Add("Protected");
			keywords.Add("Public");
			keywords.Add("RaiseEvent");
			keywords.Add("ReadOnly");
			keywords.Add("ReDim");
			keywords.Add("REM");
			keywords.Add("RemoveHandler");
			keywords.Add("Resume");
			keywords.Add("Return");
			keywords.Add("Select");
			keywords.Add("Set");
			keywords.Add("Shadows");
			keywords.Add("Shared");
			keywords.Add("Short");
			keywords.Add("Single");
			keywords.Add("Static");
			keywords.Add("Step");
			keywords.Add("Stop");
			keywords.Add("String");
			keywords.Add("Structure");
			keywords.Add("Sub");
			keywords.Add("SyncLock");
			keywords.Add("Then");
			keywords.Add("Throw");
			keywords.Add("To");
			keywords.Add("True");
			keywords.Add("Try");
			keywords.Add("TypeOf");
			keywords.Add("Unicode");
			keywords.Add("Until");
			keywords.Add("Variant");
			keywords.Add("Wend");
			keywords.Add("When");
			keywords.Add("While");
			keywords.Add("With");
			keywords.Add("WithEvents");
			keywords.Add("WriteOnly");
			keywords.Add("Xor");

			keywords.Add("print");
			keywords.Add("println");

			enum_modifiers = new ModifierList();
			enum_modifiers.Add(Modifier.New);
			enum_modifiers.Add(Modifier.Public);
			enum_modifiers.Add(Modifier.Protected);
			enum_modifiers.Add(Modifier.Internal);
			enum_modifiers.Add(Modifier.Private);

			class_modifiers = new ModifierList();
			class_modifiers.Add(Modifier.New);
			class_modifiers.Add(Modifier.Public);
			class_modifiers.Add(Modifier.Protected);
			class_modifiers.Add(Modifier.Internal);
			class_modifiers.Add(Modifier.Private);
			class_modifiers.Add(Modifier.Abstract);
			class_modifiers.Add(Modifier.Sealed);
			class_modifiers.Add(Modifier.Friend);

			structure_modifiers = new ModifierList();
			structure_modifiers.Add(Modifier.Public);
			structure_modifiers.Add(Modifier.Protected);
			structure_modifiers.Add(Modifier.Internal);
			structure_modifiers.Add(Modifier.Private);
			structure_modifiers.Add(Modifier.Friend);
			structure_modifiers.Add(Modifier.New);

			interface_modifiers = new ModifierList();
			interface_modifiers.Add(Modifier.New);
			interface_modifiers.Add(Modifier.Public);
			interface_modifiers.Add(Modifier.Protected);
			interface_modifiers.Add(Modifier.Internal);
			interface_modifiers.Add(Modifier.Private);
			interface_modifiers.Add(Modifier.Friend);

			event_modifiers = new ModifierList();
			event_modifiers.Add(Modifier.Public);
			event_modifiers.Add(Modifier.Protected);
			event_modifiers.Add(Modifier.Internal);
			event_modifiers.Add(Modifier.Private);
			event_modifiers.Add(Modifier.New);
			event_modifiers.Add(Modifier.Static);

			method_modifiers = new ModifierList();
			method_modifiers.Add(Modifier.Public);
			method_modifiers.Add(Modifier.Protected);
			method_modifiers.Add(Modifier.Internal);
			method_modifiers.Add(Modifier.Private);
			method_modifiers.Add(Modifier.New);
			method_modifiers.Add(Modifier.Static);
			method_modifiers.Add(Modifier.Virtual);
			method_modifiers.Add(Modifier.Sealed);
			method_modifiers.Add(Modifier.Abstract);
			method_modifiers.Add(Modifier.Override);
			method_modifiers.Add(Modifier.Overloads);
			method_modifiers.Add(Modifier.Friend);
			method_modifiers.Add(Modifier.Shadows);

			property_modifiers = method_modifiers.Clone();
			property_modifiers.Add(Modifier.Default);
			property_modifiers.Add(Modifier.ReadOnly);
			property_modifiers.Add(Modifier.WriteOnly);

			constructor_modifiers = new ModifierList();
			constructor_modifiers.Add(Modifier.Public);
			constructor_modifiers.Add(Modifier.Protected);
			constructor_modifiers.Add(Modifier.Internal);
			constructor_modifiers.Add(Modifier.Private);
			constructor_modifiers.Add(Modifier.Static);
			constructor_modifiers.Add(Modifier.Friend);

			delegate_modifiers = new ModifierList();
			delegate_modifiers.Add(Modifier.Public);
			delegate_modifiers.Add(Modifier.Protected);
			delegate_modifiers.Add(Modifier.Internal);
			delegate_modifiers.Add(Modifier.Private);
			delegate_modifiers.Add(Modifier.Static);
			delegate_modifiers.Add(Modifier.Friend);
			delegate_modifiers.Add(Modifier.Shadows);

			integral_types = new Types();
			integral_types.Add("Byte", StandardType.Byte);
			integral_types.Add("Short", StandardType.Short);
			integral_types.Add("Integer", StandardType.Int);
			integral_types.Add("Long", StandardType.Long);

			for_loop_stack = new ForLoopStack();
			exit_kind_stack = new IntegerStack();
			with_stack = new IntegerStack();
		}

		/// <summary>
		/// Initializes the parser.
		/// </summary>
		internal override void Init(BaseScripter scripter, Module m)
		{
			base.Init(scripter, m);

			variable_initializers.Clear();
			static_variable_initializers.Clear();
			param_ids.Clear();
			param_type_ids.Clear();
			param_mods.Clear();
			local_variables.Clear();

			has_constructor = false;
			valid_this_context = false;
			explicit_intf_id = 0;
			curr_prop_id = 0;
			new_type_id = 0;

			SKIP_STATEMENT_TERMINATOR = false;

			OPTION_STRICT = true;
   			typeof_expression = false;

			for_loop_stack.Clear();
			exit_kind_stack.Clear();
			with_stack.Clear();
		}

		/// <summary>
		/// Parses VB.NET program.
		/// </summary>
		public override void Parse_Program()
		{
			for_loop_stack.Clear();
			exit_kind_stack.Clear();
			PushExitKind(ExitKind.None);

			DECLARE_SWITCH = false;

			Gen(code.OP_UPCASE_ON, 0, 0, 0);
			Gen(code.OP_EXPLICIT_ON, 0, 0, 0);
			Gen(code.OP_STRICT_ON, 0, 0, 0);

			int base_id = NewVar();
			SetName(base_id, "System");
			int id = NewRef("Math");
			Gen(code.OP_EVAL_TYPE, 0, 0, base_id);
			Gen(code.OP_CREATE_TYPE_REFERENCE, base_id, 0, id);
			Gen(code.OP_BEGIN_USING, id, 0, 0);
/*
			base_id = NewVar();
			SetName(base_id, "Microsoft");
			id = NewRef("VisualBasic");
			Gen(code.OP_EVAL_TYPE, 0, 0, base_id);
			Gen(code.OP_CREATE_TYPE_REFERENCE, base_id, 0, id);
			Gen(code.OP_BEGIN_USING, id, 0, 0);
*/
			Parse_Start();
		}

		/// <summary>
		/// Parses VB.NET program.
		/// </summary>
        internal virtual void Parse_Start()
		{
			while (IsLineTerminator())
			{
				if (IsEOF())
					return;
				MatchLineTerminator();
			}

			DECLARE_SWITCH = true;
			while (IsCurrText("Option"))
				Parse_OptionStatement();
			while (IsCurrText("Imports"))
				Parse_ImportsStatement();

			for (;;)
			{
				Parse_NamespaceMemberDeclaration();
				if (IsEOF())
					return;
			}
		}

		/// <summary>
		/// Emits new p-code instruction.
		/// </summary>
		public override void Gen(int op, int arg1, int arg2, int res)
		{
			base.Gen(op, arg1, arg2, res);
			SetUpcase(true);
		}

		/// <summary>
		/// Parses Option statement.
		/// </summary>
        internal virtual void Parse_OptionStatement()
		{
			Match("Option");
			if (IsCurrText("Explicit"))
				Parse_OptionExplicitStatement();
			else if (IsCurrText("Strict"))
				Parse_OptionStrictStatement();
			else if (IsCurrText("Compare"))
				Parse_OptionCompareStatement();
		}

		/// <summary>
		/// Parses Option Explicit statement.
		/// </summary>
        internal virtual void Parse_OptionExplicitStatement()
		{
			Match("Explicit");
			if (IsCurrText("On"))
			{
				Match("On");
				Gen(code.OP_EXPLICIT_ON, 0, 0, 0);
				MatchLineTerminator();
			}
			else if (IsCurrText("Off"))
			{
				Match("Off");
				Gen(code.OP_EXPLICIT_OFF, 0, 0, 0);
				MatchLineTerminator();
			}
			else
				Match("On");
		}

		/// <summary>
		/// Parses Option Strict statement.
		/// </summary>
        internal virtual void Parse_OptionStrictStatement()
		{
			Match("Strict");
			if (IsCurrText("On"))
			{
				Match("On");
				Gen(code.OP_STRICT_ON, 0, 0, 0);
				MatchLineTerminator();
			}
			else if (IsCurrText("Off"))
			{
				Match("Off");
				Gen(code.OP_STRICT_OFF, 0, 0, 0);
				OPTION_STRICT = false;
				MatchLineTerminator();
			}
			else
				Match("On");
		}

		/// <summary>
		/// Parses Option Compare statement.
		/// </summary>
        internal virtual void Parse_OptionCompareStatement()
		{
			Match("Compare");
			if (IsCurrText("Binary"))
			{
				Match("Binary");
				MatchLineTerminator();
			}
			else if (IsCurrText("Text"))
			{
				Match("Text");
				MatchLineTerminator();
			}
			else
				Match("Binary");
		}

		/// <summary>
		/// Parses Imports statement.
		/// </summary>
        internal virtual void Parse_ImportsStatement()
		{
			Match("Imports");

			for (;;) // ImportsClauses
			{
				Parse_ImportsClause();
				if (!CondMatch(','))
					break;
			}

			DECLARE_SWITCH = false;

			MatchLineTerminator();
		}

		/// <summary>
		/// Parses Imports clause.
		/// </summary>
        internal virtual void Parse_ImportsClause()
		{
			int id = Parse_Ident();
			if (IsCurrText('='))
			{
				Match('=');
				int id2 = Parse_NamespaceOrTypeName();
				int owner_id = GetLevel(id);
				Gen(code.OP_CREATE_USING_ALIAS, id, owner_id, id2);
				Gen(code.OP_BEGIN_USING, id, 0, 0);
			}
			else // parse-namespace-name
			{
				Gen(code.OP_EVAL_TYPE, 0, 0, id);
				for (;;)
				{
					REF_SWITCH = true;
					if (!CondMatch('.')) break;
					int base_id = id;
					id = Parse_Ident();
					Gen(code.OP_CREATE_TYPE_REFERENCE, base_id, 0, id);
				}
				REF_SWITCH = false;
				Gen(code.OP_BEGIN_USING, id, 0, 0);
			}
		}

		/// <summary>
		/// Parses namespace member declaration.
		/// </summary>
        internal virtual void Parse_NamespaceMemberDeclaration()
		{
			if (IsCurrText("Namespace"))
				Parse_NamespaceDeclaration();
			else
				Parse_TypeDeclaration();
		}

		/// <summary>
		/// Parses namespace declaration.
		/// </summary>
        internal virtual void Parse_NamespaceDeclaration()
		{
			Match("Namespace");

			IntegerList l = new IntegerList(false);
			int namespace_id;

			for (;;) // ParseQualifiedIdentifier
			{
				namespace_id = Parse_Ident();
				l.Add(namespace_id);
				BeginNamespace(namespace_id);
				if (!CondMatch('.')) break;
			}

			// Parse namespace body

			for (;;)
			{
				Parse_NamespaceMemberDeclaration();
				if (IsCurrText("End"))
					break;
				if (IsEOF())
					Match("End");
			}

			for (int i=l.Count - 1; i >= 0; i--)
				EndNamespace(l[i]);

			Match("End");
			Match("Namespace");
			MatchLineTerminator();
		}

		/// <summary>
		/// Parses type declaration.
		/// </summary>
        internal virtual void Parse_TypeDeclaration()
		{
			Parse_Attributes();
			ModifierList ml = Parse_Modifiers();

			if (IsCurrText("Module"))
				Parse_ModuleDeclaration(ml);
			else
				Parse_NonModuleDeclaration(ml);
		}

		/// <summary>
		/// Parses module declaration.
		/// </summary>
        internal virtual void Parse_ModuleDeclaration(ModifierList ml)
		{
			Match("Module");

			DECLARE_SWITCH = true;
			has_constructor = false;

			CheckModifiers(ml, class_modifiers);
			int class_id = Parse_Ident();
			MatchLineTerminator();

			BeginClass(class_id, ml);
			Gen(code.OP_ADD_ANCESTOR, class_id, ObjectClassId, 0);

			Parse_ClassBody(class_id, ml, true);

			if (static_variable_initializers.Count > 0)
				CreateDefaultStaticConstructor(class_id);
			if (!has_constructor)
				CreateDefaultConstructor(class_id, false);

			EndClass(class_id);
			Match("End");
			Match("Module");
			if (IsLineTerminator())
				MatchLineTerminator();
		}

		/// <summary>
		/// Parses non-module declaration.
		/// </summary>
        internal virtual void Parse_NonModuleDeclaration(ModifierList ml)
		{
			if (IsCurrText("Enum"))
				Parse_EnumDeclaration(ml);
			else if (IsCurrText("Structure"))
				Parse_StructureDeclaration(ml);
			else if (IsCurrText("Interface"))
				Parse_InterfaceDeclaration(ml);
			else if (IsCurrText("Class"))
				Parse_ClassDeclaration(ml);
			else if (IsCurrText("Delegate"))
				Parse_DelegateDeclaration(ml);
			else
			{
				if (scripter.SIGN_BRIEF_SYNTAX)
				{
					DECLARE_SWITCH = false;

					ml.Add(Modifier.Public);
					int class_id = NewVar();
					SetName(class_id, "__Main");
					BeginClass(class_id, ml);
					Gen(code.OP_ADD_ANCESTOR, class_id, ObjectClassId, 0);

					ml.Add(Modifier.Static);
					int sub_id = NewVar();
					SetName(sub_id, "Main");
					BeginMethod(sub_id, MemberKind.Method, ml, (int) StandardType.Void);
					InitMethod(sub_id);

					MoveSeparator();

					Parse_Statements();
					EndMethod(sub_id);

					EndClass(class_id);
				}
				else
					Match("Class");
			}
		}

		/// <summary>
		/// Parses modifier list.
		/// </summary>
        internal virtual ModifierList Parse_Modifiers()
		{
			ModifierList result = new ModifierList();
			int protection_count = 0;
			for (;;)
			{
				int i = total_modifier_list.IndexOf(curr_token.Text);

				string s = curr_token.Text;

				if ((s == "private") ||
					(s == "protected") ||
					(s == "public") ||
					(s == "internal"))
					protection_count ++;

				if (i >= 0)
				{
					Modifier m = (Modifier) total_modifier_list.Objects[i];
					if (result.HasModifier(m))
					{
						string modifier_name = total_modifier_list[(int) m];
						// Duplicate 'modifier' modifier
						RaiseErrorEx(false, Errors.CS1004, modifier_name);
					}

					result.Add(m);
					Call_SCANNER();
				}
				else
					break;
			}

			if (protection_count > 1)
				// More than one protection modifier
				RaiseError(false, Errors.CS0107);

			if (result.HasModifier(Modifier.Private))
			{
				if (result.HasModifier(Modifier.Virtual) ||	result.HasModifier(Modifier.Abstract))
					// virtual or abstract members cannot be private
					RaiseError(false, Errors.CS0621);
			}

			return result;
		}

		/// <summary>
		/// Parses Enum declaration.
		/// </summary>
        internal virtual void Parse_EnumDeclaration(ModifierList ml)
		{
			CheckModifiers(ml, enum_modifiers);
			ml.Add(Modifier.Static);

			DECLARE_SWITCH = true;
			Match("Enum");
			int enum_id = Parse_Ident();
			int type_base = (int) StandardType.Int;
			if (IsCurrText("As")) // enum-base
			{
				Match("As");
				type_base = Parse_IntegralType();

				if (type_base == (int) StandardType.Char)
					// Type byte, sbyte, short, ushort, int, uint, long, or ulong expected
					RaiseError(true, Errors.CS1008);
			}
			BeginEnum(enum_id, ml, type_base);
			Gen(code.OP_ADD_UNDERLYING_TYPE, enum_id, type_base, 0);

			MatchLineTerminator();

			int k = -1;
			static_variable_initializers.Clear();

			for (;;)
			{
				if (IsCurrText("End"))
					break;
				if (IsEOF())
					Match("End");

				if (IsCurrText('['))
					Parse_Attributes();

				// parse enum field

				int id = Parse_Ident();
				BeginField(id, ml, enum_id);

				// create static method-initializer

				int sub_id = NewVar();
				BeginMethod(sub_id, MemberKind.Method, ml, (int)StandardType.Void);
				InitMethod(sub_id);
				static_variable_initializers.Add(sub_id);

				int expr_id;
				if (IsCurrText('='))
				{
					DECLARE_SWITCH = false;
					Match('=');
					expr_id = Parse_ConstantExpression();
					DECLARE_SWITCH = true;
					object v = GetVal(expr_id);
					if (v != null)
						k = (int) v;
					Gen(code.OP_ASSIGN, id, expr_id, id);
					SetTypeId(expr_id, type_base);
				}
				else
				{
					k++;
					expr_id = NewConst(k);
					Gen(code.OP_ASSIGN, id, expr_id, id);
					SetTypeId(expr_id, type_base);
				}
				EndMethod(sub_id);
				EndField(id);

				DECLARE_SWITCH = true;

				MatchLineTerminator();
			}

			CreateDefaultStaticConstructor(enum_id);

			DECLARE_SWITCH = true;
			EndEnum(enum_id);
			Match("End");
			Match("Enum");
			MatchLineTerminator();
		}

		///////////////// STRUCTURES /////////////////////////////////////

		/// <summary>
		/// Parses Structure declaration.
		/// </summary>
        internal virtual void Parse_StructureDeclaration(ModifierList ml)
		{
			DECLARE_SWITCH = true;
			has_constructor = false;

			CheckModifiers(ml, structure_modifiers);
			Match("Structure");
			int structure_id = Parse_Ident();
			MatchLineTerminator();

			BeginStruct(structure_id, ml);
			Gen(code.OP_ADD_ANCESTOR, structure_id, ObjectClassId, 0);

			if (!IsCurrText("End"))
				Parse_ClassBody(structure_id, ml, false);
			if (static_variable_initializers.Count > 0)
				CreateDefaultStaticConstructor(structure_id);
			if (!has_constructor)
				CreateDefaultConstructor(structure_id, false);
			EndStruct(structure_id);
			Match("End");
			Match("Structure");
			MatchLineTerminator();
		}

		///////////////// INTERFACES /////////////////////////////////////

		/// <summary>
		/// Parses Interface declaration.
		/// </summary>
        internal virtual void Parse_InterfaceDeclaration(ModifierList ml)
		{
			CheckModifiers(ml, interface_modifiers);
			ml.Add(Modifier.Abstract);

			DECLARE_SWITCH = true;
			Match("Interface");
			int interface_id = Parse_Ident();
			MatchLineTerminator();
			BeginInterface(interface_id, ml);

			if (IsCurrText("Inherits"))
				Parse_ClassBase(interface_id);

			for (;;)
			{
				if (IsCurrText("End"))
					break;
				if (IsEOF())
					Match("End");

				Parse_InterfaceMember(interface_id, ml);
			}

			DECLARE_SWITCH = false;
			EndInterface(interface_id);
			Match("End");
			Match("Interface");
			MatchLineTerminator();
		}

		/// <summary>
		/// Parses Interface member declaration.
		/// </summary>
        internal virtual void Parse_InterfaceMember(int interface_id, ModifierList owner_modifiers)
		{
			Parse_Attributes();
			ModifierList ml = Parse_Modifiers();
			ml.Add(Modifier.Abstract);

			if (owner_modifiers.HasModifier(Modifier.Public))
			{
				if (!ml.HasModifier(Modifier.Private))
					ml.Add(Modifier.Public);
			}

			if (IsCurrText("Enum"))
				Parse_NonModuleDeclaration(ml);
			else if (IsCurrText("Structure"))
				Parse_NonModuleDeclaration(ml);
			else if (IsCurrText("Interface"))
				Parse_NonModuleDeclaration(ml);
			else if (IsCurrText("Class"))
				Parse_NonModuleDeclaration(ml);
			else if (IsCurrText("Delegate"))
				Parse_NonModuleDeclaration(ml);
			else if (IsCurrText("Event"))
			{
				Parse_EventMemberDeclaration(interface_id, ml, owner_modifiers);
			}
			else if (IsCurrText("Property"))
			{
				Parse_PropertyMemberDeclaration(interface_id, ml, owner_modifiers, ClassKind.Interface);
			}
			else if (IsCurrText("Sub"))
			{
				Parse_MethodMemberDeclaration(interface_id, ml, owner_modifiers, ClassKind.Interface);
			}
			else if (IsCurrText("Function"))
			{
				Parse_MethodMemberDeclaration(interface_id, ml, owner_modifiers, ClassKind.Interface);
			}
		}

		///////////////// CLASSES ////////////////////////////////////////

		/// <summary>
		/// Creates default constructor.
		/// </summary>
        internal virtual void CreateDefaultConstructor(int class_id, bool is_struct)
		{
			ModifierList ml = new ModifierList();
			ml.Add(Modifier.Public);

			int constructor_id = NewVar();
			SetName(constructor_id, GetName(class_id));
			BeginMethod(constructor_id, MemberKind.Constructor, ml, (int)StandardType.Void);
			InitMethod(constructor_id);

			// call initializers

			for (int i = 0; i < variable_initializers.Count; i++)
			{
				int sub_id = variable_initializers[i];
				Gen(code.OP_BEGIN_CALL, sub_id, 0, 0);
				Gen(code.OP_PUSH, CurrThisID, 0, 0);
				Gen(code.OP_CALL, sub_id, 0, 0);
			}

			if (!is_struct)
			{
				int base_sub_id = NewVar();
				int base_type_id = NewVar();
				Gen(code.OP_EVAL_BASE_TYPE, class_id, base_sub_id, base_type_id);
				int object_id = NewVar();
				Gen(code.OP_CAST, base_type_id, CurrThisID, object_id);
				Gen(code.OP_BEGIN_CALL, base_sub_id, 0, 0);

				Gen(code.OP_PUSH, object_id, 0, 0);
				Gen(code.OP_CALL, base_sub_id, 0, 0);
			}

			EndMethod(constructor_id);
		}

		/// <summary>
		/// Creates default static constructor.
		/// </summary>
        internal virtual void CreateDefaultStaticConstructor(int class_id)
		{
			ModifierList ml = new ModifierList();
			ml.Add(Modifier.Static);

			int constructor_id = NewVar();
			BeginMethod(constructor_id, MemberKind.Constructor, ml, (int)StandardType.Void);
			InitMethod(constructor_id);

			// call initializers

			for (int i = 0; i < static_variable_initializers.Count; i++)
			{
				int sub_id = static_variable_initializers[i];
				Gen(code.OP_BEGIN_CALL, sub_id, 0, 0);
				Gen(code.OP_PUSH, class_id, 0, 0);
				Gen(code.OP_CALL, sub_id, 0, 0);
			}

			EndMethod(constructor_id);
		}

		/// <summary>
		/// Parses Class declaration.
		/// </summary>
        internal virtual void Parse_ClassDeclaration(ModifierList ml)
		{
			DECLARE_SWITCH = true;
			has_constructor = false;

			CheckModifiers(ml, class_modifiers);
			Match("Class");
			int class_id = Parse_Ident();
			MatchLineTerminator();

			if (ml.HasModifier(Modifier.Abstract) && (ml.HasModifier(Modifier.Sealed)))
			{
				// The class 'class' is abstract and sealed
				RaiseError(false, Errors.CS0502);
			}

			BeginClass(class_id, ml);

			if (IsCurrText("Inherits"))
				Parse_ClassBase(class_id);
			else
				Gen(code.OP_ADD_ANCESTOR, class_id, ObjectClassId, 0);

			if (IsCurrText("Implements"))
				Parse_TypeImplementsClause(class_id);

			if (!IsCurrText("End"))
				Parse_ClassBody(class_id, ml, false);
			if (static_variable_initializers.Count > 0)
				CreateDefaultStaticConstructor(class_id);
			if (!has_constructor)
				CreateDefaultConstructor(class_id, false);
			EndClass(class_id);
			Match("End");
			Match("Class");
			MatchLineTerminator();
		}

		/// <summary>
		/// Parses Class Base declaration.
		/// </summary>
        internal virtual void Parse_ClassBase(int class_id)
		{
			Match("Inherits");
			int id = Parse_Ident();
			Gen(code.OP_EVAL_TYPE, 0, 0, id);
			for (;;)
			{
				REF_SWITCH = true;
				if (!CondMatch('.')) break;
				int base_id = id;
				id = Parse_Ident();
				Gen(code.OP_CREATE_TYPE_REFERENCE, base_id, 0, id);
			}
			Gen(code.OP_ADD_ANCESTOR, class_id, id, 0);
			MatchLineTerminator();
		}

		/// <summary>
		/// Parses Implements clause.
		/// </summary>
        internal virtual void Parse_TypeImplementsClause(int class_id)
		{
			Match("Implements");
			for (;;)
			{
				int id = Parse_Ident();
				Gen(code.OP_EVAL_TYPE, 0, 0, id);
				for (;;)
				{
					REF_SWITCH = true;
					if (!CondMatch('.')) break;
					int base_id = id;
					id = Parse_Ident();
					Gen(code.OP_CREATE_TYPE_REFERENCE, base_id, 0, id);
				}
				Gen(code.OP_ADD_ANCESTOR, class_id, id, 0);
				if (!CondMatch(',')) break;
			}
			MatchLineTerminator();
		}

		/// <summary>
		/// Parses class body.
		/// </summary>
        internal virtual void Parse_ClassBody(int class_id, ModifierList owner_modifiers, bool IsModule)
		{
			variable_initializers.Clear();
			static_variable_initializers.Clear();
			for (;;)
			{
				if (IsCurrText("End"))
					break;
				if (IsEOF())
					Match("End");
				Parse_ClassMemberDeclaration(class_id, owner_modifiers, IsModule,
					ClassKind.Class);
			}
		}

		/// <summary>
		/// Parses class member declaration.
		/// </summary>
        internal virtual void Parse_ClassMemberDeclaration(int class_id, ModifierList owner_modifiers,
											bool IsModule, ClassKind ck)
		{
			Parse_Attributes();
			ModifierList ml = Parse_Modifiers();

			if (owner_modifiers.HasModifier(Modifier.Public))
			{
				if (!ml.HasModifier(Modifier.Private))
					ml.Add(Modifier.Public);
			}

			if (IsCurrText("Enum"))
				Parse_NonModuleDeclaration(ml);
			else if (IsCurrText("Structure"))
				Parse_NonModuleDeclaration(ml);
			else if (IsCurrText("Interface"))
				Parse_NonModuleDeclaration(ml);
			else if (IsCurrText("Class"))
				Parse_NonModuleDeclaration(ml);
			else if (IsCurrText("Delegate"))
				Parse_NonModuleDeclaration(ml);
			else if (IsCurrText("Event"))
			{
				if (IsModule)
					ml.Add(Modifier.Static);
				Parse_EventMemberDeclaration(class_id, ml, owner_modifiers);
			}
			else if (IsCurrText("Dim"))
			{
				if (IsModule)
					ml.Add(Modifier.Static);
				Parse_VariableMemberDeclaration(class_id, ml, owner_modifiers);
			}
			else if (IsCurrText("Const"))
			{
				ml.Add(Modifier.Static);
				Parse_ConstantMemberDeclaration(class_id, ml, owner_modifiers);
			}
			else if (IsCurrText("Property"))
			{
				if (IsModule)
					ml.Add(Modifier.Static);
				Parse_PropertyMemberDeclaration(class_id, ml, owner_modifiers, ck);
			}
			else if (IsCurrText("Declare"))
			{
				if (IsModule)
					ml.Add(Modifier.Static);
				Parse_MethodMemberDeclaration(class_id, ml, owner_modifiers, ck);
			}
			else if (IsCurrText("Sub"))
			{
				if (IsModule)
					ml.Add(Modifier.Static);
				Parse_MethodMemberDeclaration(class_id, ml, owner_modifiers, ck);
			}
			else if (IsCurrText("Function"))
			{
				if (IsModule)
					ml.Add(Modifier.Static);
				Parse_MethodMemberDeclaration(class_id, ml, owner_modifiers, ck);
			}
			else
			{
				if (IsModule)
					ml.Add(Modifier.Static);
				Parse_VariableMemberDeclaration(class_id, ml, owner_modifiers);
			}
		}

		/// <summary>
		/// Parses event member declaration.
		/// </summary>
        internal virtual void Parse_EventMemberDeclaration(int class_id, ModifierList ml, ModifierList owner_ml)
		{
			Match("Event");
			CheckModifiers(ml, event_modifiers);

			int id = Parse_Ident();
			int sub_id;

			if (ml.HasModifier(Modifier.Abstract) && (!owner_ml.HasModifier(Modifier.Abstract)))
			{
				string member_name = GetName(id);
				string class_name = GetName(class_id);
				// 'function' is abstract but it is contained in nonabstract class 'class'
				RaiseErrorEx(false, Errors.CS0513,
					member_name, class_name);
			}

			int type_id;

			if (IsCurrText('('))
			{
				int return_type_id = (int) StandardType.Void;

				int delegate_type_id = NewVar();
				SetName(delegate_type_id, GetName(id) + "EventHandler");

				BeginDelegate(delegate_type_id, ml);
				sub_id = NewVar();
				BeginMethod(sub_id, MemberKind.Method, ml, return_type_id);
				Gen(code.OP_ADD_PATTERN, delegate_type_id, sub_id, 0);

				if (IsCurrText('('))
				{
					Match('(');
					if (!IsCurrText(')'))
						Parse_ParameterList(sub_id, false);
					Match(')');
				}

				if (IsCurrText("As"))
				{
					Match("As");
					Parse_Attributes();
					return_type_id = Parse_Type();

					Gen(code.OP_ASSIGN_TYPE, sub_id, return_type_id, 0);
					Gen(code.OP_ASSIGN_TYPE, CurrResultId, return_type_id, 0);
				}

				InitMethod(sub_id);

				int code_id = NewVar();
				int data_id = NewVar();
				int res_id = GetResultId(sub_id);
				int this_id = GetThisId(sub_id);

				int break_label = NewLabel();
				int continue_label = NewLabel();

				Gen(code.OP_FIND_FIRST_DELEGATE, this_id, code_id, data_id);
				Gen(code.OP_GO_NULL, break_label, code_id, 0);
				for (int i = 0; i < param_ids.Count; i++)
					Gen(code.OP_PUSH, param_ids[i], param_mods[i], code_id);
				Gen(code.OP_PUSH, data_id, 0, 0);
				Gen(code.OP_CALL_SIMPLE, code_id, param_ids.Count, res_id);

				SetLabelHere(continue_label);

				Gen(code.OP_FIND_NEXT_DELEGATE, this_id, code_id, data_id);
				Gen(code.OP_GO_NULL, break_label, code_id, 0);

				for (int i = 0; i < param_ids.Count; i++)
					Gen(code.OP_PUSH, param_ids[i], param_mods[i], code_id);
				Gen(code.OP_PUSH, data_id, 0, 0);
				Gen(code.OP_CALL_SIMPLE, code_id, param_ids.Count, res_id);

				Gen(code.OP_GO, continue_label, 0, 0);
				SetLabelHere(break_label);

				EndMethod(sub_id);

				EndDelegate(delegate_type_id);

				type_id = delegate_type_id;
			}
			else
			{
				Match("As");
				type_id = Parse_Type();
			}

			// create a modifier list for private members
			ModifierList ml_private = ml.Clone();
			ml_private.Delete(Modifier.Public);

			// parse field declarator
			BeginField(id, ml_private, type_id);

			string event_name = GetName(id);
			SetName(id, "__" + event_name);

			EndField(id);

			int param_id, field_id, temp_id;

			// generate event declarator

			int event_id = NewVar();
			SetName(event_id, event_name);
			BeginEvent(event_id, ml, type_id, 0);

			Gen(code.OP_ADD_EVENT_FIELD, event_id, id, 0);

			// generate add-method declarator

			sub_id = NewVar();
			SetName(sub_id, "add_" + event_name);
			BeginMethod(sub_id, MemberKind.Method, ml_private, (int) StandardType.Void);

			param_id = NewVar();
			SetName(param_id, "value");
			Gen(code.OP_ADD_PARAM, sub_id, param_id, 0);
			Gen(code.OP_ASSIGN_TYPE, param_id, type_id, 0);

			InitMethod(sub_id);

			field_id = NewVar();
			SetName(field_id, GetName(id));
			temp_id = NewVar();

			Gen(code.OP_EVAL, 0, 0, field_id);
			Gen(code.OP_PLUS, field_id, param_id, temp_id);
			Gen(code.OP_ASSIGN, field_id, temp_id, field_id);

			EndMethod(sub_id);

			Gen(code.OP_ADD_ADD_ACCESSOR, event_id, sub_id, 0);
			// generate remove-method declarator

			sub_id = NewVar();
			SetName(sub_id, "remove_" + event_name);
			BeginMethod(sub_id, MemberKind.Method, ml_private, (int) StandardType.Void);

			param_id = NewVar();
			SetName(param_id, "value");
			Gen(code.OP_ADD_PARAM, sub_id, param_id, 0);
			Gen(code.OP_ASSIGN_TYPE, param_id, type_id, 0);

			InitMethod(sub_id);

			field_id = NewVar();
			SetName(field_id, GetName(id));
			temp_id = NewVar();

			Gen(code.OP_EVAL, 0, 0, field_id);
			Gen(code.OP_MINUS, field_id, param_id, temp_id);
			Gen(code.OP_ASSIGN, field_id, temp_id, field_id);

			EndMethod(sub_id);
			Gen(code.OP_ADD_REMOVE_ACCESSOR, event_id, sub_id, 0);

			EndEvent(event_id);

			MatchLineTerminator();
		}

		/// <summary>
		/// Parses variable member declaration.
		/// </summary>
        internal virtual void Parse_VariableMemberDeclaration(int class_id, ModifierList ml, ModifierList owner_modifiers)
		{
			if (IsCurrText("Dim"))
				Match("Dim");
			for (;;)
			{
				Parse_VariableDeclarator(ml);
				if (!CondMatch(',')) break;
			}
			MatchLineTerminator();
		}

		/// <summary>
		/// Parses variable declaration.
		/// </summary>
        internal virtual void Parse_VariableDeclarator(ModifierList ml)
		{
			PaxArrayList bound_list = new PaxArrayList();
			PaxArrayList name_modifier_list = new PaxArrayList();
			IntegerList l = Parse_VariableIdentifiers(bound_list, name_modifier_list);

			int type_id = ObjectClassId;
			if (IsCurrText("As"))
			{
				Match("As");

				bool is_date = curr_token.id == DATETIME_CLASS_id;
				if (IsCurrText("New") || is_date)
				{
					if (IsCurrText("New"))
					{
						Match("New");
						is_date = curr_token.id == DATETIME_CLASS_id;
						type_id = Parse_Type();
					}
					else
						type_id = Parse_TypeEx();

					if (l.Count == 1)
					{
						string s = (string) name_modifier_list[0];
						if (s != null && s != "")
						{
							// Arrays cannot be declared with 'New'.
							RaiseError(true, Errors.VB00003);
						}

						int id = l[0];
						Gen(code.OP_ASSIGN_TYPE, id, type_id, 0);
						BeginField(id, ml, type_id);

						DECLARE_SWITCH = false;
						int sub_id = NewVar();
						BeginMethod(sub_id, MemberKind.Method, ml, (int)StandardType.Void);
						InitMethod(sub_id);
						int field_id = id;
						if (!ml.HasModifier(Modifier.Static))
						{
							field_id = NewVar();
							SetName(field_id, GetName(id));
							Gen(code.OP_EVAL, 0, 0, field_id);

							variable_initializers.Add(sub_id);
						}
						else
							static_variable_initializers.Add(sub_id);

						Gen(code.OP_CREATE_OBJECT, type_id, 0, field_id);
						if (IsCurrText('('))
						{
							Match('(');
							Gen(code.OP_CALL, type_id, Parse_ArgumentList(')', type_id, field_id), 0);
							Match(')');
						}
						else
						{
							Gen(code.OP_BEGIN_CALL, type_id, 0, 0);

							if (is_date)
							{
								Gen(code.OP_PUSH, NewConst(0), 0, type_id);
							}

							Gen(code.OP_PUSH, field_id, 0, 0);
							Gen(code.OP_CALL, type_id, 0, 0);
						}

						EndMethod(sub_id);
						DECLARE_SWITCH = true;

						EndField(id);
					}
					else
					{
						// Explicit initialization is not permitted with multiple variables declared with a single type specifier.
						RaiseError(true, Errors.VB00002);
					}
					return;
				}
				type_id = Parse_TypeEx();
			}
			else
			{
				if (OPTION_STRICT)
					// Option Strict On requires all variable declarations to have an 'As' clause.
					RaiseError(false, Errors.VB00006);
			}

			for (int i = 0; i < l.Count; i++)
			{
				int id = l[i];

				if ((string) name_modifier_list[i] != "")
				{
					BeginField(id, ml, type_id);

					int sub_id = NewVar();
					BeginMethod(sub_id, MemberKind.Method, ml, (int)StandardType.Void);
					InitMethod(sub_id);
					int field_id = id;
					if (!ml.HasModifier(Modifier.Static))
					{
						field_id = NewVar();
						SetName(field_id, GetName(id));
						Gen(code.OP_EVAL, 0, 0, field_id);
						variable_initializers.Add(sub_id);
					}
					else
						static_variable_initializers.Add(sub_id);

					string s = (string) name_modifier_list[i];
					int element_type_id = type_id;
					int array_type_id = NewVar();
					SetName(array_type_id, GetName(element_type_id) + s);
					Gen(code.OP_EVAL_TYPE, element_type_id, 0, array_type_id);
					Gen(code.OP_ASSIGN_TYPE, field_id, array_type_id, 0);
					Gen(code.OP_ASSIGN_TYPE, id, array_type_id, 0);

					IntegerList bounds = (IntegerList) bound_list[i];
					Gen(code.OP_CREATE_OBJECT, array_type_id, 0, field_id);
					int index_count = bounds.Count;
					if (index_count > 0)
					{
						Gen(code.OP_BEGIN_CALL, array_type_id, 0, 0);
						for (int j = 0; j < index_count; j++)
						{
							Gen(code.OP_INC, bounds[j], 0, bounds[j]);
							Gen(code.OP_PUSH, bounds[j], 0, array_type_id);
						}
						Gen(code.OP_PUSH, field_id, 0, 0);
						Gen(code.OP_CALL, array_type_id, index_count, 0);
					}

					type_id = array_type_id;

					EndMethod(sub_id);

					EndField(id);
				}
				else
				{
					Gen(code.OP_ASSIGN_TYPE, id, type_id, 0);
					BeginField(id, ml, type_id);
					EndField(id);
				}
			}

			if (IsCurrText("="))
			{
				Match('=');
				if (l.Count == 1)
				{
					int id = l[0];
					Gen(code.OP_ASSIGN_TYPE, id, type_id, 0);
					Parse_VariableInitializer(id, type_id, ml);
				}
				else
				{
					//Explicit initialization is not permitted with multiple variables declared with a single type specifier.
					RaiseError(true, Errors.VB00002);
				}
			}
		}

		/// <summary>
		/// Parses array initializer.
		/// </summary>
        internal virtual int Parse_ArrayInitializer(int array_type_id)
		{
			string array_type_name = GetName(array_type_id);
			int bounds_count = PaxSystem.GetRank(array_type_name);
			
			scripter.Dump();

			string element_type_name = PaxSystem.GetElementTypeName(array_type_name);
			int element_type_id = NewVar();

			int id;

			SetName(element_type_id, element_type_name);
			Gen(code.OP_EVAL_TYPE, 0, 0, element_type_id);

			int result = NewVar();
			Gen(code.OP_CREATE_OBJECT, array_type_id, 0, result);
			Gen(code.OP_BEGIN_CALL, array_type_id, 0, 0);

			IntegerList ids = new IntegerList(true);
			IntegerList bound = new IntegerList(true);
			IntegerList curr_index = new IntegerList(true);
			IntegerList fixed_bound = new IntegerList(true);

			for (int i = 0; i < bounds_count; i++)
			{
				id = NewVar(-1);
				ids.Add(id);
				bound.Add(-1);
				curr_index.Add(-1);
				fixed_bound.Add(0);
				Gen(code.OP_PUSH, id, 0, array_type_id);
			}
			Gen(code.OP_PUSH, result, 0, 0);
			Gen(code.OP_CALL, array_type_id, bounds_count, 0);

			int level = -1;
			for (;;)
			{
				if (IsCurrText('{'))
				{
					Match('{');

					level ++;
					if (level == bounds_count - 1)
					{
						curr_index[level] = -1;
						for (;;)
						{
							if (IsCurrText('}'))
								break;

							if (fixed_bound[level] == 0)
								bound[level] ++;
							curr_index[level] ++;

							int index_object_id = NewVar();
							Gen(code.OP_CREATE_INDEX_OBJECT, result, 0, index_object_id);
							for (int k = 0; k < bounds_count; k++)
							{
								int index = NewConst(curr_index[k]);
								Gen(code.OP_ADD_INDEX, index_object_id, index, result);
							}
							Gen(code.OP_SETUP_INDEX_OBJECT, index_object_id, 0, 0);
							Gen(code.OP_ASSIGN, index_object_id, Parse_Expression(), index_object_id);

							if (!CondMatch(',')) break;
						}
					}
					else
					{
						if (fixed_bound[level] == 0)
							bound[level] ++;
						curr_index[level] ++;
					}
				}
				else if (IsCurrText(','))
				{
					Match(',');
					if (fixed_bound[level] == 0)
						bound[level] ++;
					curr_index[level] ++;

					for (int i = level + 1; i < bounds_count; i++)
						curr_index[i] = -1;
				}
				else if (IsCurrText('}'))
				{
					Match('}');

					if (bound[level] != curr_index[level])
						RaiseError(true, Errors.CS0178);

					fixed_bound[level] = 1;
					curr_index[level] --;

					level --;
					if (level == -1)
						break;
				}
				else
					Match('{'); // error
			}

			for (int i = 0; i < bounds_count; i++)
				PutVal(ids[i], bound[i] + 1);

			return result;
		}

		/// <summary>
		/// Parses variable initializer.
		/// </summary>
        internal virtual void Parse_VariableInitializer(int id, int type_id, ModifierList ml)
		{
			DECLARE_SWITCH = false;
			int sub_id = NewVar();
			BeginMethod(sub_id, MemberKind.Method, ml, (int)StandardType.Void);
			InitMethod(sub_id);
			int field_id = id;
			if (!ml.HasModifier(Modifier.Static))
			{
				field_id = NewVar();
				SetName(field_id, GetName(id));
				Gen(code.OP_EVAL, 0, 0, field_id);

				variable_initializers.Add(sub_id);
			}
			else
				static_variable_initializers.Add(sub_id);
			if (IsCurrText('{'))
				Gen(code.OP_ASSIGN, field_id, Parse_ArrayInitializer(type_id), field_id);
			else
				Gen(code.OP_ASSIGN, field_id, Parse_Expression(), field_id);
			EndMethod(sub_id);
			DECLARE_SWITCH = true;
		}

		/// <summary>
		/// Parses variable identifiers.
		/// </summary>
        internal virtual IntegerList Parse_VariableIdentifiers(PaxArrayList bounds_list, PaxArrayList sl)
		{
			IntegerList result = new IntegerList(false);
			for (;;)
			{
				IntegerList bounds = new IntegerList(true);
				string s;
				int id = Parse_VariableIdentifier(bounds, out s);
				result.Add(id);
				bounds_list.Add(bounds);
				sl.Add(s);

				if (!CondMatch(',')) break;
			}
			return result;
		}

		/// <summary>
		/// Parses variable identifier.
		/// </summary>
        internal virtual int Parse_VariableIdentifier(IntegerList bounds, out string s)
		{
			int result = Parse_Ident();
			if (IsCurrText('('))
				s = Parse_ArrayNameModifier(bounds);
			else
				s = "";
			return result;
		}

		/// <summary>
		/// Parses constant member declaration.
		/// </summary>
        internal virtual void Parse_ConstantMemberDeclaration(int class_id, ModifierList ml, ModifierList owner_modifiers)
		{
			DECLARE_SWITCH = true;
			Match("Const");
			for (;;)
			{
				Parse_ConstantDeclarator(ml);
				if (!CondMatch(',')) break;
			}
			MatchLineTerminator();
		}

		/// <summary>
		/// Parses constant declaration.
		/// </summary>
        internal virtual void Parse_ConstantDeclarator(ModifierList ml)
		{
			int id = Parse_Ident();
			int type_id = ObjectClassId;

			if (IsCurrText("As"))
			{
				Match("As");
				type_id = Parse_Type();
			}
			else
			{
				if (OPTION_STRICT)
					// Option Strict On requires all variable declarations to have an 'As' clause.
					RaiseError(false, Errors.VB00006);
			}

			Gen(code.OP_ASSIGN_TYPE, id, type_id, 0);

			BeginField(id, ml, type_id);
			int sub_id = NewVar();
			BeginMethod(sub_id, MemberKind.Method, ml, (int)StandardType.Void);
			InitMethod(sub_id);

			DECLARE_SWITCH = false;
			Match('=');
			int val_id = Parse_ConstantExpression();

			static_variable_initializers.Add(sub_id);
			Gen(code.OP_ASSIGN, id, val_id, id);
			EndMethod(sub_id);

			DECLARE_SWITCH = true;
			EndField(id);
		}

		/// <summary>
		/// Parses property member declaration.
		/// </summary>
        internal virtual void Parse_PropertyMemberDeclaration(int class_id, ModifierList ml, ModifierList owner_modifiers,
														ClassKind ck)
		{
			Match("Property");

			int id = Parse_Ident();

			param_ids.Clear();
			param_type_ids.Clear();
			param_mods.Clear();

			if (IsCurrText('('))
			{
				Match('(');
				if (!IsCurrText(')'))
					Parse_ParameterList(id, false);
				Match(')');
			}

			int type_id = (int) StandardType.Object;
			if (IsCurrText("As"))
			{
				Match("As");
				Parse_Attributes();
				type_id = Parse_TypeEx();

				DiscardInstruction(code.OP_ASSIGN_TYPE, id, -1, -1);
				Gen(code.OP_ASSIGN_TYPE, id, type_id, 0);
			}
			BeginProperty(id, ml, type_id, 0);

			if (ml.HasModifier(Modifier.Default))
			{
				if (param_ids.Count == 0)
				{
					// Properties with no required parameters cannot be declared 'Default'.
					RaiseError(false, Errors.VB00004);
				}
				else
				{
					Gen(code.OP_SET_DEFAULT, id, 0, 0);
				}
			}

			if (IsCurrText("Implements"))
				Parse_ImplementsClause(id);

			int count_get = 0;
			int count_set = 0;
			for (;;)
			{
				MatchLineTerminator();

				Parse_Attributes();
				if (IsCurrText("Get"))
				{
					curr_prop_id = id;

					Match("Get");
					DECLARE_SWITCH = false;
					valid_this_context = true;

					MatchLineTerminator();

					int sub_id = NewVar();
					SetName(sub_id, "get_" + GetName(id));
					BeginMethod(sub_id, MemberKind.Method, ml, type_id);
					count_get++;
					if (count_get > 1)
						// Property accessor already defined
						RaiseError(true, Errors.CS1008);
					for (int i = 0; i < param_ids.Count; i++)
					{
						DiscardInstruction(code.OP_ADD_PARAM, id, -1, -1);

						int param_id = NewVar();
						SetName(param_id, GetName(param_ids[i]));
						Gen(code.OP_ASSIGN_TYPE, param_id, param_type_ids[i], 0);
						Gen(code.OP_ADD_PARAM, sub_id, param_id, 0);
					}
					InitMethod(sub_id);
					Parse_Block();
					EndMethod(sub_id);

					Gen(code.OP_ADD_READ_ACCESSOR, id, sub_id, 0);

					DECLARE_SWITCH = true;
					valid_this_context = false;
					Match("End");
					Match("Get");

					curr_prop_id = 0;
				}
				else if (IsCurrText("Set"))
				{
					valid_this_context = true;
					Match("Set");

					int sub_id = NewVar();
					SetName(sub_id, "set_" + GetName(id));
					BeginMethod(sub_id, MemberKind.Method, ml, type_id);
					count_set++;
					if (count_set > 1)
						// Property accessor already defined
						RaiseError(true, Errors.CS1008);

					if (IsLineTerminator())
					{
						int param_id;
						for (int i = 0; i < param_ids.Count; i++)
						{
							param_id = NewVar();
							SetName(param_id, GetName(param_ids[i]));
							Gen(code.OP_ASSIGN_TYPE, param_id, param_type_ids[i], 0);
							Gen(code.OP_ADD_PARAM, sub_id, param_id, 0);
						}
						param_id = NewVar();
						SetName(param_id, "value");
						Gen(code.OP_ADD_PARAM, sub_id, param_id, 0);
						Gen(code.OP_ASSIGN_TYPE, param_id, type_id, 0);

						DECLARE_SWITCH = false;
						InitMethod(sub_id);
						MatchLineTerminator();
					}
					else
					{
						if (IsCurrText('('))
						{
							for (int i = 0; i < param_ids.Count; i++)
							{
								int param_id = NewVar();
								SetName(param_id, GetName(param_ids[i]));
								Gen(code.OP_ASSIGN_TYPE, param_id, param_type_ids[i], 0);
								Gen(code.OP_ADD_PARAM, sub_id, param_id, 0);
							}

							Match('(');
							if (!IsCurrText(')'))
								Parse_ParameterList(sub_id, false);
							Match(')');
						}
						DECLARE_SWITCH = false;
						InitMethod(sub_id);
						MatchLineTerminator();
					}

					Parse_Block();
					EndMethod(sub_id);

					Gen(code.OP_ADD_WRITE_ACCESSOR, id, sub_id, 0);

					DECLARE_SWITCH = true;
					valid_this_context = false;
					Match("End");
					Match("Set");
				}
				else
					break;
			}
			EndProperty(id);

			if (count_get + count_set == 0)
			{
				if (ml.HasModifier(Modifier.Abstract))
				{
					return;
				}
				else
				// 'property' : property or indexer must have at least one accessor
					RaiseErrorEx(true, Errors.CS0548, GetName(id));
			}

			Match("End");
			Match("Property");
			MatchLineTerminator();
		}

		/// <summary>
		/// Parses Implements clause.
		/// </summary>
        internal virtual void Parse_ImplementsClause(int member_id)
		{
			Match("Implements");
			for(;;)
			{
				int dest_id = Parse_Type();
				Gen(code.OP_ADD_IMPLEMENTS, member_id, CurrClassID, dest_id);
				if (!CondMatch(',')) break;
			}
		}

		/// <summary>
		/// Parses method member declaration.
		/// </summary>
        internal virtual void Parse_MethodMemberDeclaration(int class_id, ModifierList ml, ModifierList owner_ml, ClassKind ck)
		{
			DECLARE_SWITCH = true;
			if (IsCurrText("Sub"))
				Parse_SubDeclaration(class_id, ml, owner_ml, ck);
			else if (IsCurrText("Function"))
				Parse_FunctionDeclaration(class_id, ml, owner_ml, ck);
			else if (IsCurrText("Declare"))
				Parse_ExternalMethodDeclaration(class_id, ml, owner_ml);
			else
				Match("Sub");
		}

		/// <summary>
		/// Parses method modifiers.
		/// </summary>
        internal virtual void CheckMethodModifiers(int id, int class_id, ModifierList ml, ModifierList owner_ml)
		{
			CheckModifiers(ml, method_modifiers);

			if (ml.HasModifier(Modifier.Abstract) && (!owner_ml.HasModifier(Modifier.Abstract)))
			{
				string member_name = GetName(id);
				string class_name = GetName(class_id);
				// 'function' is abstract but it is contained in nonabstract class 'class'
				RaiseErrorEx(false, Errors.CS0513,	member_name, class_name);
			}

			if (ml.HasModifier(Modifier.Static))
			{
				if (ml.HasModifier(Modifier.Abstract) ||
					ml.HasModifier(Modifier.Virtual) ||
					ml.HasModifier(Modifier.Override))
					// A static member 'function' cannot be marked as override, virtual or abstract
					RaiseErrorEx(false, Errors.CS0112, GetName(id));
			}

			if (ml.HasModifier(Modifier.Override))
			{
				if (ml.HasModifier(Modifier.Virtual) || ml.HasModifier(Modifier.New))
					// A member 'function' marked as override cannot be marked as new or virtual
					RaiseErrorEx(false, Errors.CS0113, GetName(id));
			}

			if (ml.HasModifier(Modifier.Extern))
			{
				if (ml.HasModifier(Modifier.Abstract))
					// 'member' cannot be both extern and abstract
					RaiseErrorEx(false, Errors.CS0180, GetName(id));
			}

			if (ml.HasModifier(Modifier.Sealed))
			{
				if (!ml.HasModifier(Modifier.Override))
					// 'member' cannot be sealed because it is not an override
					RaiseErrorEx(false, Errors.CS0238, GetName(id));
			}

			if (ml.HasModifier(Modifier.Abstract))
			{
				if (ml.HasModifier(Modifier.Virtual))
					// The abstract method 'method' cannot be marked virtual
					RaiseErrorEx(false, Errors.CS0503, GetName(id));
			}

			if (ml.HasModifier(Modifier.Virtual))
			{
				if (owner_ml.HasModifier(Modifier.Sealed))
					// 'function' is a new virtual member in sealed class 'class'
					RaiseErrorEx(false, Errors.CS0549, GetName(id));
			}
		}

		/// <summary>
		/// Parses formal parameter list.
		/// </summary>
        internal virtual int Parse_ParameterList(int sub_id, bool isIndexer)
		{
			param_ids.Clear();
			param_type_ids.Clear();
			param_mods.Clear();

			bool is_params = false;
			bool has_optional = false;

			int result = 0;
			for (;;)
			{
				if (is_params)
					// A params parameter must be the last parameter in a formal parameter list.
					RaiseError(false, Errors.CS0231);

				result ++;

				Parse_Attributes();

				ParamMod mod = ParamMod.None;

				if (IsCurrText("Optional"))
				{
					Match("Optional");
					has_optional = true;
				}
				else if (has_optional)
					Match("Optional"); // error

				int type_id = ObjectClassId;
				if (IsCurrText("ByRef"))
				{
					if (isIndexer)
						// Indexers can't have ref or out parameters
						RaiseError(false, Errors.CS0631);
					Match("ByRef");
					mod = ParamMod.RetVal;
				}
				else if (IsCurrText("ParamArray"))
				{
					Match("ParamArray");
					if (IsCurrText("ByRef"))
						// The params parameter cannot be declared as ref or out
						RaiseError(false, Errors.CS1611);
					is_params = true;
				}
				else
					Match("ByVal");

				int param_id = Parse_Ident();

				string ps = "";
				if (IsCurrText('('))
				{
					is_params = true;
					ps = Parse_TypeNameModifier();
				}

				if (IsCurrText("As"))
				{
					Match("As");
					type_id = Parse_Type();

					string s;
					if (IsCurrText('('))
					{
						s = Parse_TypeNameModifier();
						string type_name = GetName(type_id);
						type_name += s;
						type_id = NewVar();
						SetName(type_id, type_name);
						Gen(code.OP_EVAL_TYPE, 0, 0, type_id);
					}

					if (is_params)
					{
						string type_name = GetName(type_id);
						type_name += ps;
						type_id = NewVar();
						SetName(type_id, type_name);
						Gen(code.OP_EVAL_TYPE, 0, 0, type_id);

						type_name = GetName(type_id);
						if (PaxSystem.GetRank(type_name) != 1)
							// The params parameter must be a single dimensional array
							RaiseError(false, Errors.CS0225);
					}
				}
				else
				{
					if (OPTION_STRICT)
						// Option Strict On requires all variable declarations to have an 'As' clause.
						RaiseError(false, Errors.VB00006);
				}

				Gen(code.OP_ASSIGN_TYPE, param_id, type_id, 0);

				if (!isIndexer)
				{
					if (is_params)
						Gen(code.OP_ADD_PARAMS, sub_id, param_id, 0);
					else
						Gen(code.OP_ADD_PARAM, sub_id, param_id, (int) mod);
				}

				if (has_optional)
				{
					Match('=');
					int default_value_id = Parse_Expression();
					Gen(code.OP_ADD_DEFAULT_VALUE, sub_id, param_id, default_value_id);
				}

				foreach (int temp_id in param_ids)
					if (GetName(temp_id) == GetName(param_id))
						// The parameter name is a duplicate
						RaiseErrorEx(false, Errors.CS0100, GetName(param_id));

				param_ids.Add(param_id);
				param_type_ids.Add(type_id);
				param_mods.Add((int) mod);

				if (!CondMatch(',')) break;
			}
			return result;
		}

		/// <summary>
		/// Parses argument list.
		/// </summary>
        internal virtual int Parse_ArgumentList(char CloseBracket, int sub_id, int object_id)
		{
			Gen(code.OP_BEGIN_CALL, sub_id, 0, 0);
			if (IsCurrText(CloseBracket))
			{
				Gen(code.OP_PUSH, object_id, 0, 0);
				return 0;
			}

			int result = 0;
			for (;;)
			{
				result ++;

				ParamMod mod = ParamMod.None;
				int actual_id = Parse_Expression();
				Gen(code.OP_PUSH, actual_id, (int) mod, sub_id);
				if (!CondMatch(',')) break;
			}
			Gen(code.OP_PUSH, object_id, 0, 0);
			return result;
		}

		/// <summary>
		/// Parses Sub declaration.
		/// </summary>
        internal virtual void Parse_SubDeclaration(int class_id, ModifierList ml,
								  ModifierList owner_ml, ClassKind ck)
		{
			Match("Sub");
			PushExitKind(ExitKind.Sub);

			int sub_id;
			int type_id = (int) StandardType.Void;
			valid_this_context = true;

			if (IsCurrText("New"))
			{
				bool IsStatic = ml.HasModifier(Modifier.Static);
				CheckModifiers(ml, constructor_modifiers);
				if ((IsStatic) && HasAccessModifier(ml))
					// 'function' : access modifiers are not allowed on static constructors
					RaiseErrorEx(false, Errors.CS0515, GetName(class_id));

				sub_id = NewVar();
				Call_SCANNER();

				BeginMethod(sub_id, MemberKind.Constructor, ml, (int)StandardType.Void);

				if (IsCurrText('('))
				{
					Match('(');
					if (!IsCurrText(')'))
					{
						if (IsStatic)
							// 'constructor' : a static constructor must be parameterless
							RaiseErrorEx(false, Errors.CS0132, GetName(class_id));
						Parse_ParameterList(sub_id, false);
					}

					Match(')');
				}
				InitMethod(sub_id);

				IntegerList l;
				if (IsStatic)
					l = static_variable_initializers;
				else
					l = variable_initializers;

				for (int i = 0; i < l.Count; i++)
				{
					int init_id = l[i];
					Gen(code.OP_BEGIN_CALL, init_id, 0, 0);
					Gen(code.OP_PUSH, CurrThisID, 0, 0);
					Gen(code.OP_CALL, init_id, 0, 0);
				}

				DECLARE_SWITCH = false;
				MatchLineTerminator();
				Parse_Block();
				EndMethod(sub_id);
				valid_this_context = false;

				if (IsStatic)
					static_variable_initializers.Clear(); // already processed
				else
					has_constructor = true;

				DECLARE_SWITCH = true;
				Match("End");
				Match("Sub");
				PopExitKind();
				MatchLineTerminator();
				return;
			} // constructor
			else
				sub_id = Parse_Ident();

			CheckMethodModifiers(sub_id, class_id, ml, owner_ml);

			BeginMethod(sub_id, MemberKind.Method, ml, type_id);
			if (explicit_intf_id > 0)
				Gen(code.OP_ADD_EXPLICIT_INTERFACE, sub_id, explicit_intf_id, 0);

			if (IsCurrText('('))
			{
				Match('(');
				if (!IsCurrText(')'))
					Parse_ParameterList(sub_id, false);
				Match(')');
			}

			if (ml.HasModifier(Modifier.Extern))
			{
				// 'member' cannot be extern and declare a body
				RaiseErrorEx(false, Errors.CS0179, GetName(sub_id));
			}

			if (ck != ClassKind.Interface)
			{
				InitMethod(sub_id);

				if (ml.HasModifier(Modifier.Abstract))
				{
					string method_name = GetName(sub_id);
					// 'class member' cannot declare a body because it is marked abstract
					RaiseErrorEx(false, Errors.CS0500, method_name);
				}

				if (GetName(sub_id) == "Main")
					Gen(code.OP_CHECKED, TRUE_id, 0, 0);

				if (IsCurrText("Handles"))
					Parse_HandlesClause();
				else if (IsCurrText("Implements"))
					Parse_ImplementsClause(sub_id);

				DECLARE_SWITCH = false;
				MatchLineTerminator();
				Parse_Block();

				if (GetName(sub_id) == "Main")
					Gen(code.OP_RESTORE_CHECKED_STATE, 0, 0, 0);

				Match("End");
				Match("Sub");
			}
			EndMethod(sub_id);
			valid_this_context = false;
			DECLARE_SWITCH = true;

			PopExitKind();
			MatchLineTerminator();
		}

		/// <summary>
		/// Parses Function declaration.
		/// </summary>
        internal virtual void Parse_FunctionDeclaration(int class_id, ModifierList ml, ModifierList owner_ml,
									ClassKind ck)
		{
			Match("Function");
			PushExitKind(ExitKind.Function);

			int sub_id = Parse_Ident();

			CheckMethodModifiers(sub_id, class_id, ml, owner_ml);

			int type_id = (int) StandardType.Object;

			valid_this_context = true;
			BeginMethod(sub_id, MemberKind.Method, ml, type_id);
			if (explicit_intf_id > 0)
				Gen(code.OP_ADD_EXPLICIT_INTERFACE, sub_id, explicit_intf_id, 0);

			if (IsCurrText('('))
			{
				Match('(');
				if (!IsCurrText(')'))
					Parse_ParameterList(sub_id, false);
				Match(')');
			}

			if (IsCurrText("As"))
			{
				Match("As");
				Parse_Attributes();
				type_id = Parse_Type();

				string s;
				if (IsCurrText('('))
				{
					s = Parse_TypeNameModifier();
					string type_name = GetName(type_id);
					type_name += s;
					type_id = NewVar();
					SetName(type_id, type_name);
					Gen(code.OP_EVAL_TYPE, 0, 0, type_id);
				}

				DiscardInstruction(code.OP_ASSIGN_TYPE, sub_id, -1, -1);
				DiscardInstruction(code.OP_ASSIGN_TYPE, CurrResultId, -1, -1);
				Gen(code.OP_ASSIGN_TYPE, sub_id, type_id, 0);
				Gen(code.OP_ASSIGN_TYPE, CurrResultId, type_id, 0);
			}

			if (ml.HasModifier(Modifier.Extern))
			{
				// 'member' cannot be extern and declare a body
				RaiseErrorEx(false, Errors.CS0179, GetName(sub_id));
			}

			if (ck != ClassKind.Interface)
			{
				InitMethod(sub_id);

				if (ml.HasModifier(Modifier.Abstract))
				{
					string method_name = GetName(sub_id);
					// 'class member' cannot declare a body because it is marked abstract
					RaiseErrorEx(false, Errors.CS0500, method_name);
				}

				if (GetName(sub_id) == "Main")
					Gen(code.OP_CHECKED, TRUE_id, 0, 0);

				if (IsCurrText("Handles"))
					Parse_HandlesClause();
				else if (IsCurrText("Implements"))
					Parse_ImplementsClause(sub_id);

				DECLARE_SWITCH = false;
				MatchLineTerminator();
				Parse_Block();

				if (GetName(sub_id) == "Main")
					Gen(code.OP_RESTORE_CHECKED_STATE, 0, 0, 0);

				Match("End");
				Match("Function");
			}
			EndMethod(sub_id);
			valid_this_context = false;
			DECLARE_SWITCH = true;

			PopExitKind();
			MatchLineTerminator();
		}

		/// <summary>
		/// Parses External method declaration.
		/// </summary>
        internal virtual void Parse_ExternalMethodDeclaration(int class_id, ModifierList ml, ModifierList owner_ml)
		{
			Match("Declare");
		}

		///////////////// EVENT HANDLING ///////////////////////////////////

		/// <summary>
		/// Parses Handles clause.
		/// </summary>
        internal virtual void Parse_HandlesClause()
		{
			if (IsCurrText("Handles"))
			{
				DECLARE_SWITCH = false;
				Match("Handles");
				Parse_EventHandlerList();
				DECLARE_SWITCH = true;
			}
		}

		/// <summary>
		/// Parses event handler list.
		/// </summary>
        internal virtual void Parse_EventHandlerList()
		{
			for (;;)
			{
				Parse_EventMemberSpecifier();
				if (!CondMatch(',')) break;
			}
		}

		/// <summary>
		/// Parses event member specifier.
		/// </summary>
        internal virtual void Parse_EventMemberSpecifier()
		{
			int object_id;
			if (IsCurrText("MyBase"))
			{
				Match("MyBase");
				object_id = CurrThisID;
			}
			else
			{
				object_id = Parse_Ident();
			}
/*
			REF_SWITCH = true;
			Match('.');
			int event_id = Parse_Ident();
			Gen(code.OP_CREATE_REFERENCE, object_id, 0, event_id);
			REF_SWITCH = false;
*/
			DECLARE_SWITCH = true;
			Match('.');
			int event_id = Parse_Ident();

			Gen(code.OP_ADD_HANDLES, CurrSubId, object_id, event_id);
		}

		///////////////// ARRAYS ///////////////////////////////////////////

		/// <summary>
		/// Parses array type modifier.
		/// </summary>
        internal virtual string Parse_ArrayTypeModifier()
		{
			string result = "";

			Match('(');
			result += "[";

			for (;;)
			{
				if (!CondMatch(',')) break;
				result += ",";
			}

			Match(')');
			result += "]";

			return "";

		}

		/// <summary>
		/// Parses array name modifier.
		/// </summary>
        internal virtual string Parse_ArrayNameModifier(IntegerList bounds)
		{
			string result = "";

			bool temp = DECLARE_SWITCH;
	                DECLARE_SWITCH = false;

			Match('(');
			result += "[";

			if (!IsCurrText(')'))
			for (;;)
			{
				if (IsCurrText(','))
				{
				}
				else
				{
					int id = Parse_Expression();
					bounds.Add(id);
				}
				if (!CondMatch(',')) break;
				result += ",";
			}

			DECLARE_SWITCH = temp;
			Match(')');
			result += "]";

			return result;
		}

		/// <summary>
		/// Parses type name modifier.
		/// </summary>
        internal virtual string Parse_TypeNameModifier()
		{
			string result = "";

			Match('(');
			result += "[";

			if (!IsCurrText(')'))
			for (;;)
			{
				if (IsCurrText(','))
				{
				}
				else
				{
					Parse_Expression();
				}
				if (!CondMatch(',')) break;
				result += ",";
			}

			Match(')');
			result += "]";

			return result;
		}

		///////////////// DELEGATES ////////////////////////////////////////

		/// <summary>
		/// Parses Delegate declaration.
		/// </summary>
        internal virtual void Parse_DelegateDeclaration(ModifierList ml)
		{
			Match("Delegate");
			CheckModifiers(ml, delegate_modifiers);

			bool is_function = false;
			if (IsCurrText("Sub"))
				Match("Sub");
			else if (IsCurrText("Function"))
			{
				Match("Function");
				is_function = true;
			}
			else
				Match("Sub");

			int return_type_id = (int) StandardType.Void;

			int delegate_type_id = Parse_Ident();
			BeginDelegate(delegate_type_id, ml);
			int sub_id = NewVar();
			BeginMethod(sub_id, MemberKind.Method, ml, return_type_id);
			Gen(code.OP_ADD_PATTERN, delegate_type_id, sub_id, 0);

			if (IsCurrText('('))
			{
				Match('(');
				if (!IsCurrText(')'))
					Parse_ParameterList(sub_id, false);
				Match(')');
			}

			if (IsCurrText("As") && is_function)
			{
				Match("As");
				Parse_Attributes();
				return_type_id = Parse_Type();

				DiscardInstruction(code.OP_ASSIGN_TYPE, sub_id, -1, -1);
				DiscardInstruction(code.OP_ASSIGN_TYPE, CurrResultId, -1, -1);
				Gen(code.OP_ASSIGN_TYPE, sub_id, return_type_id, 0);
				Gen(code.OP_ASSIGN_TYPE, CurrResultId, return_type_id, 0);
			}

			InitMethod(sub_id);

			int code_id = NewVar();
			int data_id = NewVar();
			int res_id = GetResultId(sub_id);
			int this_id = GetThisId(sub_id);

			int break_label = NewLabel();
			int continue_label = NewLabel();

			Gen(code.OP_FIND_FIRST_DELEGATE, this_id, code_id, data_id);
			Gen(code.OP_GO_NULL, break_label, code_id, 0);
			for (int i = 0; i < param_ids.Count; i++)
				Gen(code.OP_PUSH, param_ids[i], param_mods[i], code_id);
			Gen(code.OP_PUSH, data_id, 0, 0);
			Gen(code.OP_CALL_SIMPLE, code_id, param_ids.Count, res_id);

			SetLabelHere(continue_label);

			Gen(code.OP_FIND_NEXT_DELEGATE, this_id, code_id, data_id);
			Gen(code.OP_GO_NULL, break_label, code_id, 0);

			for (int i = 0; i < param_ids.Count; i++)
				Gen(code.OP_PUSH, param_ids[i], param_mods[i], code_id);
			Gen(code.OP_PUSH, data_id, 0, 0);
			Gen(code.OP_CALL_SIMPLE, code_id, param_ids.Count, res_id);

			Gen(code.OP_GO, continue_label, 0, 0);
			SetLabelHere(break_label);

			EndMethod(sub_id);

			EndDelegate(delegate_type_id);
			DECLARE_SWITCH = true;

			MatchLineTerminator();
		}

		/// <summary>
		/// Parses Block statement.
		/// </summary>
        internal virtual void Parse_Block()
		{
			BeginBlock();
			DECLARE_SWITCH = false;
			Parse_Statements();
			EndBlock();
		}

		/// <summary>
		/// Parses statement list.
		/// </summary>
        internal virtual void Parse_Statements()
		{
			for (;;)
			{
				if (IsCurrText("End"))
					break;
				if (IsCurrText("Else"))
					break;
				if (IsCurrText("ElseIf"))
					break;
				if (IsCurrText("Case"))
					break;
				if (IsCurrText("Loop"))
					break;
				if (IsCurrText("Next"))
					break;
				if (IsCurrText("Catch"))
					break;
				if (IsCurrText("Finally"))
					break;
				if (IsEOF())
					break;

				Parse_Statement();
			}
		}

		/// <summary>
		/// Parses VB.NET statement.
		/// </summary>
        internal virtual void Parse_Statement()
		{
			int k = ReadToken();
			if (IsCurrText(':'))
			{
				Backup_SCANNER(k);
				int l = Parse_NewLabel();
				Gen(code.OP_DECLARE_LOCAL_VARIABLE, l, CurrSubId, 0);
				SetLabelHere(l);
				Match(':');
				if (IsLineTerminator())
				{
					MatchLineTerminator();
					return;
				}
			}
			else
				Backup_SCANNER(k);

			if (IsCurrText("print"))
				Parse_PrintStatement();
			else if (IsCurrText("println"))
				Parse_PrintlnStatement();
			else if (IsCurrText("Static"))
				Parse_LocalDeclarationStatement(LocalModifier.Static);
			else if (IsCurrText("Dim"))
				Parse_LocalDeclarationStatement(LocalModifier.Dim);
			else if (IsCurrText("Const"))
				Parse_LocalDeclarationStatement(LocalModifier.Const);
			else if (IsCurrText("With"))
				Parse_WithStatement();
			else if (IsCurrText("SyncLock"))
				Parse_SyncLockStatement();
			else if (IsCurrText("RaiseEvent"))
				Parse_RaiseEventStatement();
			else if (IsCurrText("AddHandler"))
				Parse_AddHandlerStatement();
			else if (IsCurrText("RemoveHandler"))
				Parse_RemoveHandlerStatement();
			else if (IsCurrText("Call"))
				Parse_InvocationStatement();
			else if (IsCurrText("If"))
				Parse_IfStatement();
			else if (IsCurrText("Select"))
				Parse_SelectStatement();
			else if (IsCurrText("While"))
				Parse_WhileStatement();
			else if (IsCurrText("Do"))
				Parse_DoLoopStatement();
			else if (IsCurrText("For"))
				Parse_ForNextStatement();
			else if (IsCurrText("Try"))
				Parse_TryStatement();
			else if (IsCurrText("Throw"))
				Parse_ThrowStatement();
			else if (IsCurrText("Error"))
				Parse_ErrorStatement();
			else if (IsCurrText("Resume"))
				Parse_ResumeStatement();
			else if (IsCurrText("On"))
				Parse_OnErrorStatement();
			else if (IsCurrText("GoTo"))
				Parse_GoToStatement();
			else if (IsCurrText("Exit"))
				Parse_ExitStatement();
			else if (IsCurrText("Stop"))
				Parse_StopStatement();
			else if (IsCurrText("End"))
				Parse_EndStatement();
			else if (IsCurrText("ReDim"))
				Parse_ReDimStatement();
			else if (IsCurrText("Erase"))
				Parse_EraseStatement();
			else if (IsCurrText("Return"))
				Parse_ReturnStatement();
			else
				Parse_AssignmentStatement();
		}

		/// <summary>
		/// Parses Local Declaration statement
		/// </summary>
        internal virtual void Parse_LocalDeclarationStatement(LocalModifier m)
		{
			local_variables.Clear();

			DECLARE_SWITCH = true;
			DECLARATION_CHECK_SWITCH = true;

			PaxArrayList bound_list = new PaxArrayList();
			PaxArrayList name_modifier_list = new PaxArrayList();

			Call_SCANNER();

			for (;;)
			{
				// parse local declarator
				int type_id = (int) StandardType.Object;
				for (;;) // parse local identifiers
				{
					// parse local identifier
					int id = Parse_Ident();
					Gen(code.OP_DECLARE_LOCAL_VARIABLE, id, CurrSubId, 0);
					local_variables.Add(id);

					if (m == LocalModifier.Static)
						SetStaticLocalVar(id);

					if (IsCurrText('('))
					{
                        DECLARE_SWITCH = false;
						IntegerList bounds = new IntegerList(true);
						string s = Parse_ArrayNameModifier(bounds);
						bound_list.Add(bounds);
						name_modifier_list.Add(s);
					}
					else
					{
						bound_list.Add(null);
						name_modifier_list.Add(null);
					}

					if(!CondMatch(',')) break;
				}

				DECLARE_SWITCH = false;
				if (IsCurrText("As"))
				{
					Match("As");

					bool is_date = curr_token.id == DATETIME_CLASS_id;
					if (IsCurrText("New") || is_date)
					{
						if (IsCurrText("New"))
						{
							Match("New");
							is_date = curr_token.id == DATETIME_CLASS_id;
						}

						type_id = Parse_Type();

						if (local_variables.Count == 1)
						{
							if (name_modifier_list[0] != null)
							{
								// Arrays cannot be declared with 'New'.
								RaiseError(true, Errors.VB00003);
							}

							int id = local_variables[0];
							Gen(code.OP_CREATE_OBJECT, type_id, 0, id);
							if (IsCurrText('('))
							{
								Match('(');
								Gen(code.OP_CALL, type_id, Parse_ArgumentList(')', type_id, id), 0);
								Match(')');
							}
							else
							{
								Gen(code.OP_BEGIN_CALL, type_id, 0, 0);

								if (is_date)
								{
									Gen(code.OP_PUSH, NewConst(0), 0, type_id);
								}

								Gen(code.OP_PUSH, id, 0, 0);
								Gen(code.OP_CALL, type_id, 0, 0);
							}
						}
						else
						{
							// Explicit initialization is not permitted with multiple variables declared with a single type specifier.
							RaiseError(true, Errors.VB00002);
						}
					}
					else
						type_id = Parse_TypeEx();
				}
				else
				{
                    if (OPTION_STRICT)
                    {
                        if (IsCurrText("$"))
                        {
                            type_id = (int)StandardType.String;
                            Call_SCANNER();
                        }
                        else if (IsCurrText("%"))
                        {
                            type_id = (int)StandardType.Int;
                            Call_SCANNER();
                        }
                        else if (IsCurrText("&"))
                        {
                            type_id = (int)StandardType.Long;
                            Call_SCANNER();
                        }
                        else if (IsCurrText("@"))
                        {
                            type_id = (int)StandardType.Decimal;
                            Call_SCANNER();
                        }
                        else if (IsCurrText("!"))
                        {
                            type_id = (int)StandardType.Float;
                            Call_SCANNER();
                        }
                        else if (IsCurrText("#"))
                        {
                            type_id = (int)StandardType.Double;
                            Call_SCANNER();
                        }
                        else
                        // Option Strict On requires all variable declarations to have an 'As' clause.
                            RaiseError(false, Errors.VB00006);
                    }
				}

				for (int i = 0; i < local_variables.Count; i++)
				{
					int id = local_variables[i];

					if (name_modifier_list[i] != null)
					{
						string s = (string) name_modifier_list[i];
						int element_type_id = type_id;
						int array_type_id = NewVar();
						SetName(array_type_id, GetName(element_type_id) + s);
						Gen(code.OP_EVAL_TYPE, element_type_id, 0, array_type_id);
						Gen(code.OP_ASSIGN_TYPE, id, array_type_id, 0);

						IntegerList bounds = (IntegerList) bound_list[i];
						Gen(code.OP_CREATE_OBJECT, array_type_id, 0, id);
						int index_count = bounds.Count;
						if (index_count > 0)
						{
							Gen(code.OP_BEGIN_CALL, array_type_id, 0, 0);
							for (int j = 0; j < index_count; j++)
							{
								Gen(code.OP_INC, bounds[j], 0, bounds[j]);
								Gen(code.OP_PUSH, bounds[j], 0, array_type_id);
							}
							Gen(code.OP_PUSH, id, 0, 0);
							Gen(code.OP_CALL, array_type_id, index_count, 0);
						}

						type_id = array_type_id;
					}
					else
						Gen(code.OP_ASSIGN_TYPE, id, type_id, 0);
				}

				if (IsCurrText('='))
				{
					DECLARE_SWITCH = false;
					Match('=');

					if (local_variables.Count == 1)
					{
						int id = local_variables[0];
						// parse local variable initializer
						if (IsCurrText('{'))
							Gen(code.OP_ASSIGN, id, Parse_ArrayInitializer(type_id), id);
						else
							Gen(code.OP_ASSIGN, id, Parse_Expression(), id);
						Gen(code.OP_INIT_STATIC_VAR, id, 0, 0);
					}
					else
					{
						// Explicit initialization is not permitted with multiple variables declared with a single type specifier.
						RaiseError(true, Errors.VB00002);
					}

					DECLARE_SWITCH = true;
				}
				else
				{
					for (int i = 0; i < local_variables.Count; i++)
					{
						int id = local_variables[i];
						Gen(code.OP_CHECK_STRUCT_CONSTRUCTOR, type_id, 0, id);
					}
				}

				DECLARE_SWITCH = true;
				if(!CondMatch(',')) break;
			}

			DECLARE_SWITCH = false;
			DECLARATION_CHECK_SWITCH = false;

			MatchStatementTerminator();
		}

		/// <summary>
		/// Parses With statemenr.
		/// </summary>
        internal virtual void Parse_WithStatement()
		{
			Match("With");
			int id = Parse_Expression();
			with_stack.Push(id);
			MatchStatementTerminator();
			if (!IsCurrText("End"))
				Parse_Block();
			with_stack.Pop();
			Match("End");
			Match("With");
			MatchStatementTerminator();
		}

		/// <summary>
		/// Parses SyncLock statemenr.
		/// </summary>
        internal virtual void Parse_SyncLockStatement()
		{
			Match("SyncLock");
			int id = Parse_Expression();
			MatchStatementTerminator();
			if (!IsCurrText("End"))
			{
				Gen(code.OP_LOCK, id, 0, 0);

				// try-block

				int l_try = NewLabel();
				Gen(code.OP_TRY_ON, l_try, 0, 0);

				Parse_Block();

				Gen(code.OP_FINALLY, 0, 0, 0);

				Gen(code.OP_UNLOCK, id, 0, 0);

				Gen(code.OP_EXIT_ON_ERROR, 0, 0, 0);
				Gen(code.OP_GOTO_CONTINUE, 0, 0, 0);

				SetLabelHere(l_try);
				Gen(code.OP_TRY_OFF, 0, 0, 0);
			}
			Match("End");
			Match("SyncLock");
			MatchStatementTerminator();
		}

		/// <summary>
		/// Parses RaiseEvent statement
		/// </summary>
        internal virtual void Parse_RaiseEventStatement()
	   {
			Match("RaiseEvent");
			int id = Parse_SimpleNameExpression();
			Gen(code.OP_RAISE_EVENT, id, 0, 0);
			if (IsCurrText('('))
			{
				Match('(');
				int result = NewVar();
				Gen(code.OP_CALL, id, Parse_ArgumentList(')', id, id), result);
				Match(')');
			}
			else
			{
				int result = NewVar();
				Gen(code.OP_BEGIN_CALL, id, 0, 0);
				Gen(code.OP_PUSH, id, 0, 0);
				Gen(code.OP_CALL, id, 0, result);
			}

			MatchStatementTerminator();
	   }

		/// <summary>
		/// Parses AddHandler statement
		/// </summary>
        internal virtual void Parse_AddHandlerStatement()
		{
			Match("AddHandler");
			int id1 = Parse_Expression();
			Match(',');
			int id2 = Parse_Expression();
			int result = NewVar();
			Gen(code.OP_PLUS, id1, id2, result);
			Gen(code.OP_ASSIGN, id1, result, id1);
			MatchStatementTerminator();
		}

		/// <summary>
		/// Parses RemoveHandler statement
		/// </summary>
        internal virtual void Parse_RemoveHandlerStatement()
		{
			Match("RemoveHandler");
			int id1 = Parse_Expression();
			Match(',');
			int id2 = Parse_Expression();
			int result = NewVar();
			Gen(code.OP_MINUS, id1, id2, result);
			Gen(code.OP_ASSIGN, id1, result, id1);
			MatchStatementTerminator();
		}

		/// <summary>
		/// Parses assignment statement
		/// </summary>
        internal virtual void Parse_AssignmentStatement()
		{
			int result = Parse_SimpleExpression();

			if (GetName(result) == GetName(CurrSubId))
			{
				result = CurrResultId;
			}
			else if (curr_prop_id > 0 && GetName(result) == GetName(curr_prop_id))
			{
				DiscardInstruction(code.OP_EVAL, -1, -1, result);
				result = CurrResultId;
			}

			if (IsCurrText('='))
			{
				Call_SCANNER();

                if (IsCurrText("Call"))
                    Call_SCANNER();

				Gen(code.OP_ASSIGN, result, Parse_Expression(), result);
				MatchStatementTerminator();
			}
			else if (IsCurrText('^'))
			{
				Call_SCANNER();
				Match('=');
				int temp = NewVar();
				Gen(code.OP_EXPONENT, result, Parse_Expression(), temp);
				Gen(code.OP_ASSIGN, result, temp, result);
				MatchStatementTerminator();
			}
			else if (IsCurrText('*'))
			{
				Call_SCANNER();
				Match('=');
				int temp = NewVar();
				Gen(code.OP_MULT, result, Parse_Expression(), temp);
				Gen(code.OP_ASSIGN, result, temp, result);
				MatchStatementTerminator();
			}
			else if (IsCurrText('/'))
			{
				Call_SCANNER();
				Match('=');
				int temp = NewVar();
				Gen(code.OP_DIV, result, Parse_Expression(), temp);
				Gen(code.OP_ASSIGN, result, temp, result);
				MatchStatementTerminator();
			}
			else if (IsCurrText('\\'))
			{
				Call_SCANNER();
				Match('=');
				int temp = NewVar();
				Gen(code.OP_DIV, result, Parse_Expression(), temp);
				Gen(code.OP_ASSIGN, result, temp, result);
				MatchStatementTerminator();
			}
			else if (IsCurrText('+'))
			{
				Call_SCANNER();
				Match('=');
				int temp = NewVar();
				Gen(code.OP_PLUS, result, Parse_Expression(), temp);
				Gen(code.OP_ASSIGN, result, temp, result);
				MatchStatementTerminator();
			}
			else if (IsCurrText('-'))
			{
				Call_SCANNER();
				Match('=');
				int temp = NewVar();
				Gen(code.OP_MINUS, result, Parse_Expression(), temp);
				Gen(code.OP_ASSIGN, result, temp, result);
				MatchStatementTerminator();
			}
			else if (IsCurrText('&'))
			{
				Call_SCANNER();
				Match('=');
				int temp = NewVar();
				Gen(code.OP_PLUS, result, Parse_Expression(), temp);
				Gen(code.OP_ASSIGN, result, temp, result);
				MatchStatementTerminator();
			}
			else if (IsCurrText("<<"))
			{
				Call_SCANNER();
				Match('=');
				int temp = NewVar();
				Gen(code.OP_LEFT_SHIFT, result, Parse_Expression(), temp);
				Gen(code.OP_ASSIGN, result, temp, result);
				MatchStatementTerminator();
			}
			else if (IsCurrText(">>"))
			{
				Call_SCANNER();
				Match('=');
				int temp = NewVar();
				Gen(code.OP_RIGHT_SHIFT, result, Parse_Expression(), temp);
				Gen(code.OP_ASSIGN, result, temp, result);
				MatchStatementTerminator();
			}
			else
			{
                if (IsLineTerminator())
                    MatchLineTerminator();
                else if (IsCurrText(':'))
                {
                    Call_SCANNER();
                    Parse_Statement();
                }
                else
                    Match('=');
			}
		}

		/// <summary>
		/// Parses 'println statement'.
		/// </summary>
        internal virtual void Parse_InvocationStatement()
		{
			if (IsCurrText("Call"))
				Match("Call");
			int sub_id = Parse_SimpleExpression();
			int result = NewVar();

			if (IsCurrText('('))
			{
				Match('(');
				Gen(code.OP_CALL, sub_id, Parse_ArgumentList(')', sub_id, CurrThisID), result);
				Match(')');
			}
			else
			{
				Gen(code.OP_BEGIN_CALL, sub_id, 0, 0);
				Gen(code.OP_PUSH, CurrThisID, 0, 0);
				Gen(code.OP_CALL, sub_id, 0, result);
			}
		}

		/// <summary>
		/// Parses If statement.
		/// </summary>
        internal virtual void Parse_IfStatement()
		{
			Match("If");

			int l = NewLabel();
			int lf = NewLabel();
			int expr_id = Parse_Expression();
			Gen(code.OP_GO_FALSE, lf, expr_id, 0);

			if (IsCurrText("Then"))
				Match("Then");
			if (IsStatementTerminator()) // block if statement
			{
				MatchStatementTerminator();

				Parse_Block();

				Gen(code.OP_GO, l, 0, 0);
				SetLabelHere(lf);

				while (IsCurrText("ElseIf"))
				{
					int l1 = NewLabel();
					Match("ElseIf");
					int id = Parse_Expression();
					Gen(code.OP_GO_FALSE, l1, id, 0);

					if (IsCurrText("Then"))
						Match("Then");
					MatchStatementTerminator();
					Parse_Block();
					Gen(code.OP_GO, l, 0, 0);
					SetLabelHere(l1);
				}

				if (IsCurrText("Else"))
				{
					Match("Else");
					MatchStatementTerminator();
					Parse_Block();
				}

				SetLabelHere(l);

				Match("End");
				Match("If");
				MatchStatementTerminator();
			}
			else // line if statement
			{
				SKIP_STATEMENT_TERMINATOR = true;

				Parse_Statement();
				Gen(code.OP_GO, l, 0, 0);
				SetLabelHere(lf);

				if (IsCurrText("Else"))
				{
					Match("Else");
					Parse_Statement();
				}

				SetLabelHere(l);
                if (IsStatementTerminator())
				    MatchStatementTerminator();

				SKIP_STATEMENT_TERMINATOR = false;
			}
		}

		/// <summary>
		/// Parses Select statement.
		/// </summary>
        internal virtual void Parse_SelectStatement()
		{
			Match("Select");
			if (IsCurrText("Case"))
				Match("Case");

			PushExitKind(ExitKind.Select);
			int l = NewLabel();
			BreakStack.Push(l, ExitKind.Select);

			int expr_id = Parse_Expression();
			MatchStatementTerminator();

			while(IsCurrText("Case"))
			{
				int lf = NewLabel();

				Match("Case");
				if (!IsCurrText("Else")) // parse case statement
				{
					int result_id = NewVar(true);
					Gen(code.OP_ASSIGN, result_id, TRUE_id, result_id);

					for (;;) // parse case clauses
					{
						// parse case clause
						if (IsCurrText("Is"))
							Match("Is");

						int op = 0;
						if (IsCurrText('='))
						{
							op = code.OP_EQ;
							Call_SCANNER();
						}
						else if (IsCurrText("<>"))
						{
							op = code.OP_NE;
							Call_SCANNER();
						}
						else if (IsCurrText('>'))
						{
							op = code.OP_GT;
							Call_SCANNER();
						}
						else if (IsCurrText(">="))
						{
							op = code.OP_GE;
							Call_SCANNER();
						}
						else if (IsCurrText('<'))
						{
							op = code.OP_LT;
							Call_SCANNER();
						}
						else if (IsCurrText("<="))
						{
							op = code.OP_LE;
							Call_SCANNER();
						}

						if (op != 0)
						{
							int id = Parse_Expression();
							int temp = NewVar(true);
							Gen(op, expr_id, id, temp);
							Gen(code.OP_BITWISE_AND, result_id, temp, result_id);
						}
						else
						{
							int id1 = Parse_Expression();
							if (IsCurrText("To"))
							{
								Match("To");
								int id2 = Parse_Expression();
								int temp1 = NewVar(true);
								int temp2 = NewVar(true);
								Gen(code.OP_GE, expr_id, id1, temp1);
								Gen(code.OP_LE, expr_id, id2, temp2);
								Gen(code.OP_BITWISE_AND, result_id, temp1, result_id);
								Gen(code.OP_BITWISE_AND, result_id, temp2, result_id);
							}
							else
							{
								int temp = NewVar(true);
								Gen(code.OP_EQ, expr_id, id1, temp);
								Gen(code.OP_BITWISE_AND, result_id, temp, result_id);
							}
						}

						if(!CondMatch(',')) break;
					}
					Gen(code.OP_GO_FALSE, lf, result_id, 0);

					MatchStatementTerminator();
					Parse_Block();

					Gen(code.OP_GO, l, 0, 0);
					SetLabelHere(lf);
				}
				else // parse case else statement
				{
					Match("Else");
					MatchStatementTerminator();
					Parse_Block();
					SetLabelHere(lf);
				}
			}

			SetLabelHere(l);
			BreakStack.Pop();
			PopExitKind();

			Match("End");
			Match("Select");
			MatchStatementTerminator();
		}

		/// <summary>
		/// Parses While statement.
		/// </summary>
        internal virtual void Parse_WhileStatement()
		{
			Match("While");
			PushExitKind(ExitKind.While);

			int lf = NewLabel();
			int lg = NewLabel();
			SetLabelHere(lg);
			Gen(code.OP_GO_FALSE, lf, Parse_Expression(), 0);
			MatchStatementTerminator();
			BreakStack.Push(lf, ExitKind.While);
			ContinueStack.Push(lg);
			Parse_Block();
			BreakStack.Pop();
			ContinueStack.Pop();
			Gen(code.OP_GO, lg, 0, 0);
			SetLabelHere(lf);
			Match("End");
			Match("While");
			PopExitKind();
			MatchStatementTerminator();
		}

		/// <summary>
		/// Parses DoLoop statement.
		/// </summary>
        internal virtual void Parse_DoLoopStatement()
		{
			Match("Do");
			PushExitKind(ExitKind.Do);

			int lg = NewLabel();
			int lf = NewLabel();
			SetLabelHere(lg);
			if (IsCurrText("While"))
			{
				Match("While");
				Gen(code.OP_GO_FALSE, lf, Parse_Expression(), 0);
			}
			else if (IsCurrText("Until"))
			{
				Match("Until");
				Gen(code.OP_GO_TRUE, lf, Parse_Expression(), 0);
			}
			MatchStatementTerminator();

			BreakStack.Push(lf, ExitKind.Do);
			ContinueStack.Push(lg);
			Parse_Block();
			BreakStack.Pop();
			ContinueStack.Pop();

			Match("Loop");
			if (IsCurrText("While"))
			{
				Match("While");
				Gen(code.OP_GO_TRUE, lg, Parse_Expression(), 0);
			}
			else if (IsCurrText("Until"))
			{
				Match("Until");
				Gen(code.OP_GO_FALSE, lg, Parse_Expression(), 0);
			}
			else
			{
				Gen(code.OP_GO, lg, 0, 0);
			}
			SetLabelHere(lf);
			MatchStatementTerminator();

			PopExitKind();
		}

		/// <summary>
		/// Parses ForNext statement.
		/// </summary>
        internal virtual void Parse_ForNextStatement()
		{
			Match("For");
			if (IsCurrText("Each"))
			{
				Parse_ForEachStatement();
				return;
			}
			PushExitKind(ExitKind.For);

			BeginBlock();

			int lg = NewLabel();
			int lf = NewLabel();

			int id = Parse_Ident();
			if (IsCurrText("As"))
			{
				Match("As");
				DiscardInstruction(code.OP_EVAL, -1, -1, id);
				Gen(code.OP_DECLARE_LOCAL_VARIABLE, id, CurrSubId, 0);
				Gen(code.OP_ASSIGN_TYPE, id, Parse_Type(), 0);
			}
			else
				Gen(code.OP_ASSIGN_TYPE, id, (int) StandardType.Int, 0);

			Match("=");
			int id1 = Parse_Expression();
			Gen(code.OP_ASSIGN, id, id1, id);
			SetLabelHere(lg);

			Match("To");
			int id2 = Parse_Expression();

			int temp = NewVar(true);
			Gen(code.OP_LE, id, id2, temp);
			Gen(code.OP_GO_FALSE, lf, temp, 0);

			int step_id;
			if (IsCurrText("Step"))
			{
				Match("Step");
				step_id = Parse_Expression();
			}
			else
			{
				step_id = NewVar(0);
				Gen(code.OP_ASSIGN, step_id, NewVar(1), step_id);
			}

			MatchStatementTerminator();

			for_loop_stack.Push(id, step_id, lg, lf);

			BreakStack.Push(lf, ExitKind.For);
			ContinueStack.Push(lg);

			BeginBlock();
			for (;;)
			{
				if (IsCurrText("Next"))
					break;
				if (IsEOF())
					break;
				if (for_loop_stack.Count == 0)
					break;

				Parse_Statement();
			}
			EndBlock();
			EndBlock();

			BreakStack.Pop();
			ContinueStack.Pop();

			if (for_loop_stack.Count == 0)
			{
				SetLabelHere(lf);
				return;
			}

			Match("Next");
			if (!IsStatementTerminator())
			{
				for (;;)
				{
					int next_id = Parse_Expression();
					ForLoopRec r = for_loop_stack.Top;
					if (r.id != next_id)
						// Next control variable does not match For loop control variable '<variablename>'
						RaiseErrorEx(true, Errors.VB00001, GetName(r.id));

					Gen(code.OP_PLUS, r.id, r.step_id, r.id);
					Gen(code.OP_GO, r.lg, 0, 0);
					SetLabelHere(r.lf);

					for_loop_stack.Pop();

					if(!CondMatch(',')) break;
				}
			}
			else
			{
				Gen(code.OP_PLUS, id, step_id, id);
				Gen(code.OP_GO, lg, 0, 0);
				SetLabelHere(lf);

				for_loop_stack.Pop();
			}

			PopExitKind();

			MatchStatementTerminator();
		}


		/// <summary>
		/// Parses ForEach statement.
		/// </summary>
        internal virtual void Parse_ForEachStatement()
		{
			Match("Each");
			BeginBlock();
			PushExitKind(ExitKind.For);
			int element_id = Parse_Ident();
			if (IsCurrText("As"))
			{
				Match("As");
				DiscardInstruction(code.OP_EVAL, -1, -1, element_id);
				Gen(code.OP_DECLARE_LOCAL_VARIABLE, element_id, CurrSubId, 0);
				Gen(code.OP_ASSIGN_TYPE, element_id, Parse_TypeEx(), 0);
			}
//			else
//				Gen(code.OP_ASSIGN_TYPE, element_id, (int) StandardType.Object, 0);

			Match("In");
			int collection_id = Parse_Expression();
			MatchStatementTerminator();

			// Enumerator enumerator = collection.GetEnumerator();
			int enumerator_id = NewVar();
			int get_enumerator_id = NewRef("GetEnumerator");
			Gen(code.OP_CREATE_REFERENCE, collection_id, 0, get_enumerator_id);
			Gen(code.OP_BEGIN_CALL, get_enumerator_id, 0, 0);
			Gen(code.OP_PUSH, collection_id, 0, 0);
			Gen(code.OP_CALL, get_enumerator_id, 0, enumerator_id);

			// while (enumerator.MoveNext()) {
			int lf = NewLabel();
			int lg = NewLabel();
			SetLabelHere(lg);

			int bool_var_id = NewVar();

			int move_next_id = NewRef("MoveNext");
			Gen(code.OP_CREATE_REFERENCE, enumerator_id, 0, move_next_id);
			Gen(code.OP_BEGIN_CALL, move_next_id, 0, 0);
			Gen(code.OP_PUSH, enumerator_id, 0, 0);
			Gen(code.OP_CALL, move_next_id, 0, bool_var_id);

			Gen(code.OP_GO_FALSE, lf, bool_var_id, 0);

			BreakStack.Push(lf, ExitKind.For);
			ContinueStack.Push(lg);

			// element = enumerator.Current;
			int enumerator_current_id = NewRef("get_Current");
			Gen(code.OP_CREATE_REFERENCE, enumerator_id, 0, enumerator_current_id);
			Gen(code.OP_BEGIN_CALL, enumerator_current_id, 0, 0);
			Gen(code.OP_PUSH, enumerator_id, 0, 0);
			Gen(code.OP_CALL, enumerator_current_id, 0, element_id);

			Parse_Block();

			BreakStack.Pop();
			ContinueStack.Pop();
			Gen(code.OP_GO, lg, 0, 0);
			SetLabelHere(lf);

			Match("Next");
			if (!IsStatementTerminator())
			{
				int next_id = Parse_Expression();
				if (next_id != element_id)
					// Next control variable does not match For loop control variable '<variablename>'
					RaiseErrorEx(true, Errors.VB00001, GetName(next_id));
			}
			else
			{
			}
			PopExitKind();
			BeginBlock();

			MatchStatementTerminator();
		}

		/// <summary>
		/// Parses Try statement.
		/// </summary>
        internal virtual void Parse_TryStatement()
		{
			Match("Try");
			PushExitKind(ExitKind.Try);

			MatchStatementTerminator();

			int l_try = NewLabel();
			BreakStack.Push(l_try, ExitKind.Try);

			Gen(code.OP_TRY_ON, l_try, 0, 0);

			Parse_Block();

			int l = NewLabel();
			Gen(code.OP_GO, l, 0, 0);

			while (IsCurrText("Catch"))
			{
				DECLARE_SWITCH = true;
				Match("Catch");
				int id;

				int l2 = NewLabel();

				if (IsCurrText("When"))
				{
					DECLARE_SWITCH = false;
					Match("When");
					int expr_id = Parse_BooleanExpression();
					Gen(code.OP_CATCH, 0, 0, 0);
					Gen(code.OP_GO_FALSE, l2, expr_id, 0);
				}
				else
				{
					id = Parse_Ident();
					DECLARE_SWITCH = false;
					Match("As");
					Gen(code.OP_DECLARE_LOCAL_SIMPLE, id, CurrSubId, 0);
					Gen(code.OP_ASSIGN_TYPE, id, Parse_Type(), 0);

					Gen(code.OP_CATCH, id, 0, 0);
					if (IsCurrText("When"))
					{
						Match("When");
						int expr_id = Parse_BooleanExpression();
						Gen(code.OP_GO_FALSE, l2, expr_id, 0);
					}
				}
				MatchStatementTerminator();

				Parse_Block();
				Gen(code.OP_DISCARD_ERROR, 0, 0, 0);

				SetLabelHere(l2);

				SetLabelHere(l);
			}
			if (IsCurrText("Finally"))
			{
				Match("Finally");
				MatchStatementTerminator();

				SetLabelHere(l);
				Gen(code.OP_FINALLY, 0, 0, 0);
				Parse_Block();
				Gen(code.OP_EXIT_ON_ERROR, 0, 0, 0);
				Gen(code.OP_GOTO_CONTINUE, 0, 0, 0);
			}

			SetLabelHere(l_try);
			Gen(code.OP_TRY_OFF, 0, 0, 0);

			Match("End");
			Match("Try");

			BreakStack.Pop();
			PopExitKind();

			MatchStatementTerminator();
		}

		/// <summary>
		/// Parses Throw statement.
		/// </summary>
        internal virtual void Parse_ThrowStatement()
		{
			Match("Throw");
			if (!IsStatementTerminator())
				Gen(code.OP_THROW, Parse_Expression(), 0, 0);
			else
				Gen(code.OP_THROW, 0, 0, 0);
			MatchStatementTerminator();
		}

		/// <summary>
		/// Parses Error statement.
		/// </summary>
        internal virtual void Parse_ErrorStatement()
		{
			Match("Error");
			Gen(code.OP_THROW, Parse_Expression(), 0, 0);
			MatchStatementTerminator();
		}

		/// <summary>
		/// Parses Resume statement.
		/// </summary>
        internal virtual void Parse_ResumeStatement()
		{
			Match("Resume");
			if (IsCurrText("Next"))
			{
				Match("Next");
				Gen(code.OP_RESUME_NEXT, 0, 0, 0);
			}
			else if (curr_token.tokenClass == TokenClass.Identifier)
			{
				int l = Parse_Ident();
				PutKind(l, MemberKind.Label);
				Gen(code.OP_GOTO_START, l, 0, 0);
			}
			else
				Gen(code.OP_RESUME, 0, 0, 0);

			MatchStatementTerminator();
		}

		/// <summary>
		/// Parses On Error statement.
		/// </summary>
        internal virtual void Parse_OnErrorStatement()
		{
			int lg = NewLabel();
			Gen(code.OP_GO, lg, 0, 0);
			Match("On");
			Match("Error");
			if (IsCurrText("GoTo"))
			{
				Match("GoTo");
				if (IsCurrText('-'))
				{
					Match('-');
					Match('1');
				}
				else if (IsCurrText('0'))
				{
					Match('0');
				}
				else
				{
					Gen(code.OP_ONERROR, 0, 0, 0);
					Gen(code.OP_DISCARD_ERROR, 0, 0, 0);
					int l = Parse_Ident();
					PutKind(l, MemberKind.Label);
					Gen(code.OP_GOTO_START, l, 0, 0);
				}
			}
			else
			{
				Gen(code.OP_ONERROR, 0, 0, 0);
				Gen(code.OP_DISCARD_ERROR, 0, 0, 0);
				Gen(code.OP_RESUME_NEXT, 0, 0, 0);
				Match("Resume");
				Match("Next");
			}
			SetLabelHere(lg);
			MatchStatementTerminator();
		}

		/// <summary>
		/// Parses GoTo statement.
		/// </summary>
        internal virtual void Parse_GoToStatement()
		{
			Match("GoTo");
			int l = Parse_Ident();
			PutKind(l, MemberKind.Label);
			Gen(code.OP_GOTO_START, l, 0, 0);
			MatchStatementTerminator();
		}

		/// <summary>
		/// Parses Exit statement.
		/// </summary>
        internal virtual void Parse_ExitStatement()
		{
			Match("Exit");
			ExitKind k = CurrExitKind;

			if (IsCurrText("Do"))
			{
				Match("Do");
				Gen(code.OP_GOTO_START, BreakStack.TopLabel(ExitKind.Do), 0, 0);
			}
			else if (IsCurrText("For"))
			{
				Match("For");
				Gen(code.OP_GOTO_START, BreakStack.TopLabel(ExitKind.For), 0, 0);
			}
			else if (IsCurrText("While"))
			{
				Match("While");
				Gen(code.OP_GOTO_START, BreakStack.TopLabel(ExitKind.While), 0, 0);
			}
			else if (IsCurrText("Select"))
			{
				Match("Select");
				Gen(code.OP_GOTO_START, BreakStack.TopLabel(ExitKind.Select), 0, 0);
			}
			else if (IsCurrText("Sub"))
			{
				Match("Sub");
				Gen(code.OP_EXIT_SUB, 0, 0, 0);
			}
			else if (IsCurrText("Function"))
			{
				Match("Function");
				Gen(code.OP_EXIT_SUB, 0, 0, 0);
			}
			else if (IsCurrText("Property"))
			{
				Match("Property");
				Gen(code.OP_EXIT_SUB, 0, 0, 0);
			}
			else
			{
				if (BreakStack.Count == 0)
					// No enclosing loop out of which to break or continue
					RaiseError(false, Errors.CS0139);
			}

			MatchStatementTerminator();
		}

		/// <summary>
		/// Parses Stop statement.
		/// </summary>
        internal virtual void Parse_StopStatement()
		{
			Match("Stop");
			Gen(code.OP_HALT, 0, 0, 0);
			MatchStatementTerminator();
		}

		/// <summary>
		/// Parses End statement.
		/// </summary>
        internal virtual void Parse_EndStatement()
		{
			Match("End");
			Gen(code.OP_HALT, 0, 0, 0);
			MatchStatementTerminator();
		}

		/// <summary>
		/// Parses ReDim statement.
		/// </summary>
        internal virtual void Parse_ReDimStatement()
		{
			Match("ReDim");
			if (IsCurrText("Preserve"))
				Match("Preserve");

            IntegerList bounds = new IntegerList(true);
            for (; ; )
			{
                bounds.Clear();
				int id = Parse_Ident();
                Parse_ArrayNameModifier(bounds);
                for (int i = 0; i < bounds.Count; i++)
                    Gen(code.OP_PUSH, bounds[i], 0, id);
                Gen(code.OP_REDIM, id, bounds.Count, 0);

				if(!CondMatch(',')) break;
			}
			MatchStatementTerminator();
		}

		/// <summary>
		/// Parses Erase statement.
		/// </summary>
        internal virtual void Parse_EraseStatement()
		{
			Match("Erase");
			for (;;)
			{
				int id = Parse_Expression();
				if(!CondMatch(',')) break;
			}
			MatchStatementTerminator();
		}

		/// <summary>
		/// Parses Println statement.
		/// </summary>
        internal virtual void Parse_PrintlnStatement()
		{
			Parse_PrintStatement();
			Gen(code.OP_PRINT, BR_id, 0, 0);
		}

		/// <summary>
		/// Parses Print statement.
		/// </summary>
        internal virtual void Parse_PrintStatement()
		{
			Call_SCANNER();
			for (;;)
			{
				Gen(code.OP_PRINT, Parse_Expression(), 0, 0);
				if(!CondMatch(',')) break;
			}
			MatchLineTerminator();
		}

        internal virtual void Parse_ReturnStatement()
		{
			Match("Return");
			if (!IsStatementTerminator())
			{
				int sub_id = CurrLevel;
				int res_id = GetResultId(sub_id);
				Gen(code.OP_ASSIGN, res_id, Parse_Expression(), res_id);
			}
			Gen(code.OP_EXIT_SUB, 0, 0, 0);
			MatchStatementTerminator();
		}

        internal virtual int Parse_NamespaceOrTypeName()
		{
			int id = Parse_Ident();
			Gen(code.OP_EVAL_TYPE, 0, 0, id);
			for (;;)
			{
				REF_SWITCH = true;
				if (!CondMatch('.')) break;
				int base_id = id;
				id = Parse_Ident();
				Gen(code.OP_CREATE_TYPE_REFERENCE, base_id, 0, id);
			}
			REF_SWITCH = false;
			return id;
		}

        internal virtual int Parse_ConstantExpression()
		{
			return Parse_Expression();
		}

        internal virtual int Parse_BooleanExpression()
		{
			int result = NewVar(true);
			Gen(code.OP_ASSIGN, result, Parse_Expression(), result);
			return result;
		}

		public override int Parse_Expression()
		{
			return Parse_LogicalXORExpression();
		}

        internal virtual int Parse_LogicalXORExpression()
		{
			int result = Parse_LogicalORExpression();
			while (IsCurrText("Xor"))
			{
				Call_SCANNER();
				result = BinOp(code.OP_BITWISE_XOR, result, Parse_LogicalORExpression());
			}
			return result;
		}

        internal virtual int Parse_LogicalORExpression()
		{
			int result = Parse_LogicalANDExpression();
			while (IsCurrText("Or") || IsCurrText("OrElse"))
			{
				if (IsCurrText("Or"))
				{
					Call_SCANNER();
					result = BinOp(code.OP_BITWISE_OR, result, Parse_LogicalANDExpression());
				}
				else
				{
					int id = result;
					int lf = NewLabel();
					result = NewVar();
					Gen(code.OP_ASSIGN, result, id, result);
					Gen(code.OP_GO_TRUE, lf, result, 0);
					Call_SCANNER();
					Gen(code.OP_ASSIGN, result, Parse_LogicalANDExpression(), result);
					SetLabelHere(lf);
				}
			}
			return result;
		}

        internal virtual int Parse_LogicalANDExpression()
		{
			int result = Parse_LogicalNOTExpression();
			while (IsCurrText("And") || IsCurrText("AndAlso"))
			{
				if (IsCurrText("And"))
				{
					Call_SCANNER();
					result = BinOp(code.OP_BITWISE_AND, result, Parse_LogicalNOTExpression());
				}
				else
				{
					int id = result;
					int lf = NewLabel();
					result = NewVar();
					Gen(code.OP_ASSIGN, result, id, result);
					Gen(code.OP_GO_FALSE, lf, result, 0);
					Call_SCANNER();
					Gen(code.OP_ASSIGN, result, Parse_LogicalNOTExpression(), result);
					SetLabelHere(lf);
				}
			}
			return result;
		}

        internal virtual int Parse_LogicalNOTExpression()
		{
			int result;
			if (IsCurrText("Not"))
			{
				Match("Not");
				result = UnaryOp(code.OP_NOT, Parse_Expression());
			}
            else
				result = Parse_RelationalExpression();
			return result;
		}

        internal virtual int Parse_RelationalExpression()
		{
			int result = Parse_ShiftExpression();
			while (IsCurrText('=') || IsCurrText("<>") ||
				   IsCurrText('>') || IsCurrText(">=") ||
				   IsCurrText('<') || IsCurrText("<=") ||
				   (IsCurrText("Is") && !typeof_expression) ||
				   IsCurrText("Like") ||
				   IsCurrText("IsNot")
				   )
			{
				if (IsCurrText('='))
				{
					Call_SCANNER();
					result = BinOp(code.OP_EQ, result, Parse_ShiftExpression());
				}
				else if (IsCurrText("<>"))
				{
					Call_SCANNER();
					result = BinOp(code.OP_NE, result, Parse_ShiftExpression());
				}
                else if (IsCurrText("IsNot"))
                {
                    Call_SCANNER();
                    result = BinOp(code.OP_NE, result, Parse_ShiftExpression());
                }
                else if (IsCurrText('>'))
				{
					Call_SCANNER();
					result = BinOp(code.OP_GT, result, Parse_ShiftExpression());
				}
				else if (IsCurrText(">="))
				{
					Call_SCANNER();
					result = BinOp(code.OP_GE, result, Parse_ShiftExpression());
				}
				else if (IsCurrText('<'))
				{
					Call_SCANNER();
					result = BinOp(code.OP_LT, result, Parse_ShiftExpression());
				}
				else if (IsCurrText("<="))
				{
					Call_SCANNER();
					result = BinOp(code.OP_LE, result, Parse_ShiftExpression());
				}
				else if (IsCurrText("Like"))
				{
					Call_SCANNER();
					result = BinOp(code.OP_EQ, result, Parse_ShiftExpression());
				}
				else if (IsCurrText("Is"))
				{
					Call_SCANNER();
					result = BinOp(code.OP_EQ, result, Parse_ShiftExpression());
				}
			}
			return result;
		}

        internal virtual int Parse_ShiftExpression()
		{
			int result = Parse_ConcatenationExpression();
			while (IsCurrText("<<") || IsCurrText(">>"))
			{
				if (IsCurrText("<<"))
				{
					Call_SCANNER();
					result = BinOp(code.OP_LEFT_SHIFT, result, Parse_ConcatenationExpression());
				}
				else
				{
					Call_SCANNER();
					result = BinOp(code.OP_RIGHT_SHIFT, result, Parse_ConcatenationExpression());
				}
			}
			return result;
		}

        internal virtual int Parse_ConcatenationExpression()
		{
			int result = Parse_AdditiveExpression();
			while (IsCurrText("&"))
			{
				Call_SCANNER();
				result = BinOp(code.OP_PLUS, result, Parse_AdditiveExpression());
			}
			return result;
		}

        internal virtual int Parse_AdditiveExpression()
		{
			int result = Parse_ModulusExpression();
			while (IsCurrText('+') || IsCurrText('-'))
			{
				if (IsCurrText('+'))
				{
					Call_SCANNER();
					result = BinOp(code.OP_PLUS, result, Parse_ModulusExpression());
				}
				else
				{
					Call_SCANNER();
					result = BinOp(code.OP_MINUS, result, Parse_ModulusExpression());
				}
			}
			return result;
		}

        internal virtual int Parse_ModulusExpression()
		{
			int result = Parse_IntegerDivisionExpression();
			while (IsCurrText("Mod"))
			{
				Call_SCANNER();
				result = BinOp(code.OP_MOD, result, Parse_IntegerDivisionExpression());
			}
			return result;
		}

        internal virtual int Parse_IntegerDivisionExpression()
		{
			int result = Parse_MultiplicativeExpression();
			while (IsCurrText('\\'))
			{
				Call_SCANNER();
				result = BinOp(code.OP_DIV, result, Parse_MultiplicativeExpression());
			}
			return result;
		}

        internal virtual int Parse_MultiplicativeExpression()
		{
			int result = Parse_UnaryNegationExpression();
			while (IsCurrText('*') || IsCurrText('/'))
			{
				if (IsCurrText('*'))
				{
					Call_SCANNER();
					result = BinOp(code.OP_MULT, result, Parse_UnaryNegationExpression());
				}
				else
				{
					Call_SCANNER();
					result = BinOp(code.OP_DIV, result, Parse_UnaryNegationExpression());
				}
			}
			return result;
		}

        internal virtual int Parse_UnaryNegationExpression()
		{
			int result;
			if (IsCurrText('+'))
			{
				Call_SCANNER();
				result = UnaryOp(code.OP_UNARY_PLUS, Parse_Expression());
			}
			else if (IsCurrText('-'))
			{
				Call_SCANNER();
				result = UnaryOp(code.OP_UNARY_MINUS, Parse_Expression());
			}
			else if (IsCurrText("Not"))
			{
				Call_SCANNER();
				result = UnaryOp(code.OP_NOT, Parse_Expression());
			}
			else
				result = Parse_ExponentiationExpression();
			return result;
		}

        internal virtual int Parse_ExponentiationExpression()
		{
			int result = Parse_SimpleExpression();
			while (IsCurrText('^'))
			{
				Call_SCANNER();
				result = BinOp(code.OP_EXPONENT, result, Parse_SimpleExpression());
			}
			return result;
		}

        internal virtual int Parse_SimpleExpression()
		{
			int result;

			if (IsCurrText('('))
			{
				//Parse parenthesized expression
				Match('(');
				result = Parse_Expression();
				Match(')');
			}
			else if (IsCurrText("CBool"))
			{
				Call_SCANNER();
				Match('(');
				result = NewVar();
				Gen(code.OP_TO_BOOLEAN, 0, Parse_Expression(), result);
				Match(')');
			}
			else if (IsCurrText("CByte"))
			{
				Call_SCANNER();
				Match('(');
				result = NewVar();
				Gen(code.OP_TO_BYTE, 0, Parse_Expression(), result);
				Match(')');
			}
			else if (IsCurrText("CChar"))
			{
				Call_SCANNER();
				Match('(');
				result = NewVar();
				Gen(code.OP_TO_CHAR, 0, Parse_Expression(), result);
				Match(')');
			}
			else if (IsCurrText("CDate"))
			{
				Call_SCANNER();
				Match('(');
				result = NewVar();
//				Gen(code.OP_TO_CHAR, 0, Parse_Expression(), result);
				Match(')');
			}
			else if (IsCurrText("CDec"))
			{
				Call_SCANNER();
				Match('(');
				result = NewVar();
				Gen(code.OP_TO_DECIMAL, 0, Parse_Expression(), result);
				Match(')');
			}
			else if (IsCurrText("CDbl"))
			{
				Call_SCANNER();
				Match('(');
				result = NewVar();
				Gen(code.OP_TO_DOUBLE, 0, Parse_Expression(), result);
				Match(')');
			}
			else if (IsCurrText("CInt"))
			{
				Call_SCANNER();
				Match('(');
				result = NewVar();
				Gen(code.OP_TO_INT, 0, Parse_Expression(), result);
				Match(')');
			}
			else if (IsCurrText("CType"))
			{
				Call_SCANNER();
				Match('(');
				result = NewVar();
				int expr_id = Parse_Expression();
				Match(',');
				int type_id = Parse_TypeEx();
				Gen(code.OP_CAST, type_id, expr_id, result);
				Match(')');
			}
			else if (IsCurrText("DirectCast"))
			{
				Call_SCANNER();
				Match('(');
				result = NewVar();
				int expr_id = Parse_Expression();
				Match(',');
				int type_id = Parse_TypeEx();
				Gen(code.OP_CAST, type_id, expr_id, result);
				Match(')');
			}
			else if (IsCurrText("CLng"))
			{
				Call_SCANNER();
				Match('(');
				result = NewVar();
				Gen(code.OP_TO_LONG, 0, Parse_Expression(), result);
				Match(')');
			}
			else if (IsCurrText("CObj"))
			{
				Call_SCANNER();
				Match('(');
				result = NewVar();
				Gen(code.OP_CAST, (int) StandardType.Object, Parse_Expression(), result);
				Match(')');
			}
			else if (IsCurrText("CShort"))
			{
				Call_SCANNER();
				Match('(');
				result = NewVar();
				Gen(code.OP_TO_SHORT, 0, Parse_Expression(), result);
				Match(')');
			}
			else if (IsCurrText("CSng"))
			{
				Call_SCANNER();
				Match('(');
				result = NewVar();
				Gen(code.OP_TO_FLOAT, 0, Parse_Expression(), result);
				Match(')');
			}
			else if (IsCurrText("CStr"))
			{
				Call_SCANNER();
				Match('(');
				result = NewVar();
				Gen(code.OP_TO_STRING, 0, Parse_Expression(), result);
				Match(')');
			}
			// Parse instance expressions
			else if (IsCurrText("Me"))
			{
				Match("Me");
				result = CurrThisID;
				if (GetName(result)!= "this")
					// keyword this is not valid
					RaiseError(false, Errors.CS0026);
				if (!valid_this_context)
					RaiseError(false, Errors.CS0027);
			}
			else if (IsCurrText("MyClass"))
			{
				Match("MyClass");
				result = CurrThisID;
				if (GetName(result)!= "this")
					// keyword this is not valid
					RaiseError(false, Errors.CS0026);
				if (!valid_this_context)
					RaiseError(false, Errors.CS0027);
			}
			// Parse type expression
			else if (IsCurrText("GetType"))
			{
				DiscardInstruction(code.OP_EVAL, -1, -1, curr_token.id);

				Match("GetType");
				result = NewVar();
				Match('(');
				Gen(code.OP_TYPEOF, Parse_TypeEx(), 0, result);
				Match(')');
			}
			// Parse TypeOf-Is expression
			else if (IsCurrText("TypeOf"))
			{
				typeof_expression = true;
				Match("TypeOf");
				int id = Parse_Expression();
				typeof_expression = false;
				Match("Is");
				result = NewVar();
				Gen(code.OP_IS, id, Parse_TypeEx(), result);
			}
			// Parse AddressOf expression
			else if (IsCurrText("AddressOf"))
			{
				Match("AddressOf");
				result = NewVar();
				Gen(code.OP_ADDRESS_OF, Parse_Expression(), 0, result);
			}
			else if (IsCurrText("MyBase"))
			{
				Match("MyBase");
				result = CurrThisID;
				if (GetName(result)!= "this")
					// Keyword base is not available in a static method
					RaiseError(false, Errors.CS1511);
				if (IsCurrText('.'))
				{
					REF_SWITCH = true;
					Match('.');

					int base_type_id = NewVar();
					Gen(code.OP_EVAL_BASE_TYPE, CurrClassID, 0, base_type_id);
					int object_id = NewVar();
					Gen(code.OP_CAST, base_type_id, CurrThisID, object_id);
					result = Parse_Ident();
					Gen(code.OP_CREATE_REFERENCE, object_id, 0, result);

					if (IsCurrText('(')) // invocation
					{
						int base_object_id = result;
						result = NewVar();
						Match('(');
						Gen(code.OP_CALL_BASE, base_object_id, Parse_ArgumentList(')', base_object_id, CurrThisID), result);
						Match(')');
					}
				}
				else
				{
					Match('[');

					int base_type_id = NewVar();
					Gen(code.OP_EVAL_BASE_TYPE, CurrClassID, 0, base_type_id);
					result = NewVar();
					Gen(code.OP_CAST, base_type_id, CurrThisID, result);

					int index_object_id = NewVar();
					Gen(code.OP_CREATE_INDEX_OBJECT, result, 0, index_object_id);
					for (;;)
					{
						Gen(code.OP_ADD_INDEX, index_object_id, Parse_Expression(), result);
						if (!CondMatch(',')) break;
					}
					Gen(code.OP_SETUP_INDEX_OBJECT, index_object_id, 0, 0);
					Match(']');
					result = index_object_id;
				}
			}
			else if (IsCurrText("New"))
			{
				Match("New");

				int type_id = Parse_Type();

				new_type_id = type_id;

				result = NewVar();
				Gen(code.OP_CREATE_OBJECT, type_id, 0, result);
				if (IsCurrText('('))
				{
					Match('(');
					Gen(code.OP_CALL, type_id, Parse_ArgumentList(')', type_id, result), 0);
					Match(')');
				}
				else
				{
					Gen(code.OP_BEGIN_CALL, type_id, 0, 0);
					Gen(code.OP_PUSH, result, 0, 0);
					Gen(code.OP_CALL, type_id, 0, 0);
				}

				if (IsCurrText('{'))
				{
					string s = GetName(new_type_id);
					s += "[]";
					int arr_type_id = NewVar();
					SetName(arr_type_id, s);
					Gen(code.OP_EVAL_TYPE, 0, 0, arr_type_id);
					int init_id = Parse_ArrayInitializer(arr_type_id);
					Gen(code.OP_ASSIGN_TYPE, result, arr_type_id, 0);
					Gen(code.OP_ASSIGN_TYPE, init_id, arr_type_id, 0);
					Gen(code.OP_ASSIGN, result, init_id, result);
				}

				return result;
			}
			else if (IsCurrText("true"))
				result = Parse_BooleanLiteral();
			else if (IsCurrText("false"))
				result = Parse_BooleanLiteral();
			else if (curr_token.tokenClass == TokenClass.StringConst)
				result = Parse_StringLiteral();
			else if (curr_token.tokenClass == TokenClass.CharacterConst)
				result = Parse_CharacterLiteral();
			else if (curr_token.tokenClass == TokenClass.IntegerConst)
				result = Parse_IntegerLiteral();
			else if (curr_token.tokenClass == TokenClass.RealConst)
				result = Parse_RealLiteral();
			else
				result = Parse_SimpleNameExpression();

			for (;;)
			{
				if (IsCurrText('('))
				{
					int sub_id = result;
					result = NewVar();
					Match('(');
					Gen(code.OP_CALL, sub_id, Parse_ArgumentList(')', sub_id, CurrThisID), result);
					Match(')');

					if (IsCurrText('{'))
					{
						string s = GetName(new_type_id);
						s += "[]";
						int arr_type_id = NewVar();
						SetName(arr_type_id, s);
						Gen(code.OP_EVAL_TYPE, 0, 0, arr_type_id);
						int init_id = Parse_ArrayInitializer(arr_type_id);
						Gen(code.OP_ASSIGN_TYPE, result, arr_type_id, 0);
						Gen(code.OP_ASSIGN_TYPE, init_id, arr_type_id, 0);
						Gen(code.OP_ASSIGN, result, init_id, result);
					}

				}
				else if (IsCurrText('[')) // element access
				{
					int index_object_id = NewVar();
					Gen(code.OP_CREATE_INDEX_OBJECT, result, 0, index_object_id);
					Match('[');
					for (;;)
					{
						Gen(code.OP_ADD_INDEX, index_object_id, Parse_Expression(), result);
						if (!CondMatch(',')) break;
					}
					Gen(code.OP_SETUP_INDEX_OBJECT, index_object_id, 0, 0);
					Match(']');
					result = index_object_id;
				}
				else if (IsCurrText('.')) // member access
				{
					REF_SWITCH = true;
					Match('.');
					int object_id = result;
					result = Parse_Ident();
					Gen(code.OP_CREATE_REFERENCE, object_id, 0, result);
				}
				else
					break;
			}

			return result;
		}

		/// <summary>
		/// Parses simple name expression
		/// </summary>
        internal virtual int Parse_SimpleNameExpression()
		{
			return Parse_IdentOrType();
		}

		/// <summary>
		/// Parses integral type.
		/// </summary>
        internal virtual int Parse_IntegralType()
		{

			int id = integral_types.GetTypeId(curr_token.Text);
			if (id == -1)
				RaiseError(false, Errors.CS1008);
			Call_SCANNER();
			return id;
		}

        internal virtual int Parse_IdentOrType()
		{
			if (IsCurrText("Boolean"))
			{
				Call_SCANNER();
				return (int) StandardType.Bool;
			}
			else if (IsCurrText("Date"))
			{
				Call_SCANNER();
				return DATETIME_CLASS_id;
			}
			else if (IsCurrText("Char"))
			{
				Call_SCANNER();
				return (int) StandardType.Char;
			}
			else if (IsCurrText("String"))
			{
				Call_SCANNER();
				return (int) StandardType.String;
			}
			else if (IsCurrText("Byte"))
			{
				Call_SCANNER();
				return (int) StandardType.Byte;
			}
			else if (IsCurrText("Short"))
			{
				Call_SCANNER();
				return (int) StandardType.Short;
			}
			else if (IsCurrText("Integer"))
			{
				Call_SCANNER();
				return (int) StandardType.Int;
			}
			else if (IsCurrText("Long"))
			{
				Call_SCANNER();
				return (int) StandardType.Long;
			}
			else if (IsCurrText("Single"))
			{
				Call_SCANNER();
				return (int) StandardType.Float;
			}
			else if (IsCurrText("Double"))
			{
				Call_SCANNER();
				return (int) StandardType.Double;
			}
			else if (IsCurrText("Decimal"))
			{
				Call_SCANNER();
				return (int) StandardType.Decimal;
			}
			else if (IsCurrText("Object"))
			{
				Call_SCANNER();
				return (int) StandardType.Object;
			}
			else if (IsCurrText("Nothing"))
			{
				Call_SCANNER();
				return NULL_id;
			}
			else
				return Parse_Ident();
		}

		/// <summary>
		/// Parses non-array type expression.
		/// </summary>
        internal virtual int Parse_NonArrayType()
		{
			int id;

			id = Parse_IdentOrType();
			Gen(code.OP_EVAL_TYPE, 0, 0, id);
			for (;;)
			{
				REF_SWITCH = true;
				if (!CondMatch('.')) break;
				int base_id = id;
				id = Parse_Ident();
				Gen(code.OP_CREATE_TYPE_REFERENCE, base_id, 0, id);
			}
			REF_SWITCH = false;

			return id;
		}

		/// <summary>
		/// Parses type expression.
		/// </summary>
        internal virtual int Parse_Type()
		{
			int result = Parse_NonArrayType();

			if (IsCurrText('['))
			{
				int element_type_id = result;
				int array_type_id = NewVar();

				string s = "";
				for (;;)
				{
					s += Parse_ArrayTypeModifier();
					if (!IsCurrText('(')) break;
				}

				SetName(array_type_id, GetName(result) + s);
				Gen(code.OP_EVAL_TYPE, element_type_id, 0, array_type_id);

				return array_type_id;
			}
			else
				return result;
		}

        internal virtual int Parse_TypeEx()
		{
			int type_id = Parse_Type();
			if (IsCurrText('('))
			{
				string s = Parse_TypeNameModifier();
				string type_name = GetName(type_id);
				type_name += s;
				type_id = NewVar();
				SetName(type_id, type_name);
				Gen(code.OP_EVAL_TYPE, 0, 0, type_id);
			}
			return type_id;
		}


        internal virtual void MatchLineTerminator()
		{
			if (IsEOF())
				return;

			if (!scanner.IsNewLine(scanner.LA(0)))
				RaiseError(true, "Line terminator expected");
            while (curr_token.tokenClass == TokenClass.Separator)
            {
                Gen(code.OP_SEPARATOR, curr_module, scanner.LineNumber, 0);
                Call_SCANNER();
            }  
		}

        internal virtual void MatchStatementTerminator()
		{
			if (IsEOF())
				return;

			if (!SKIP_STATEMENT_TERMINATOR)
			{
                if (IsCurrText(':'))
                {
                    Gen(code.OP_SEPARATOR, curr_module, scanner.LineNumber, 0);
                    Call_SCANNER();
                }
                else
                if (!scanner.IsNewLine(scanner.LA(0)))
                {
                    return;
//                  RaiseError(true, "Statement terminator expected");
                }
			}

            while (curr_token.tokenClass == TokenClass.Separator)
            {
                Gen(code.OP_SEPARATOR, curr_module, scanner.LineNumber, 0);
                Call_SCANNER();
            }
        }

		/// <summary>
		/// Returns 'true' if current token is line terminator.
		/// </summary>
        internal virtual bool IsLineTerminator()
		{
			return scanner.IsNewLine(scanner.LA(0)) || IsEOF();
		}

		/// <summary>
		/// Returns 'true' if current token is statement terminator.
		/// </summary>
        internal virtual bool IsStatementTerminator()
		{
			return IsLineTerminator() || IsCurrText(':') || IsEOF();
		}

		/// <summary>
		/// Not implemented yet.
		/// </summary>
        internal virtual void Parse_Attributes()
		{
		}

		/// <summary>
		/// Pushes kind of 'Exit' into exit kind stack.
		/// </summary>
        internal virtual void PushExitKind(ExitKind k)
		{
			exit_kind_stack.Push((int) k);
		}

		/// <summary>
		/// Pops kind of 'Exit' from exit kind stack.
		/// </summary>
        internal virtual void PopExitKind()
		{
			exit_kind_stack.Pop();
		}

		/// <summary>
		/// Returns current kind of 'Exit'
		/// </summary>
		ExitKind CurrExitKind
		{
			get
			{
				return (ExitKind) exit_kind_stack.Peek();
			}
		}

		/// <summary>
		/// Returns 'true' if modifier list ml contains an access modifier.
		/// </summary>
		internal virtual bool HasAccessModifier(ModifierList ml)
		{
			return ml.HasModifier(Modifier.Public) ||
				   ml.HasModifier(Modifier.Private) ||
				   ml.HasModifier(Modifier.Protected);
		}

		/// <summary>
		/// Parses integer literal.
		/// </summary>
		public override int Parse_IntegerLiteral()
		{
			int result = base.Parse_IntegerLiteral();
			if (IsCurrText('%'))
			{
				SetTypeId(result, (int) StandardType.Int);
                int v = Conversion.ToInt(GetVal(result));
                PutVal(result, v);
				Call_SCANNER();
			}
			else if (IsCurrText('&'))
			{
				SetTypeId(result, (int) StandardType.Long);
                long v = Conversion.ToLong(GetVal(result));
                PutVal(result, v);
				Call_SCANNER();
			}
			else if (IsCurrText('@'))
			{
				SetTypeId(result, (int) StandardType.Decimal);
                decimal v = Conversion.ToDecimal(GetVal(result));
                PutVal(result, v);
				Call_SCANNER();
			}
			else if (IsCurrText('!'))
			{
				SetTypeId(result, (int) StandardType.Float);
                float v = Conversion.ToFloat(GetVal(result));
                PutVal(result, v);
				Call_SCANNER();
			}
			else if (IsCurrText('#'))
			{
				SetTypeId(result, (int) StandardType.Double);
                double v = Conversion.ToDouble(GetVal(result));
                PutVal(result, v);
				Call_SCANNER();
			}
			else if (IsCurrText('$'))
			{
				SetTypeId(result, (int) StandardType.String);
                string v = Conversion.ToString(GetVal(result));
                PutVal(result, v);
                Call_SCANNER();
			}
			return result;
		}

        /// <summary>
        /// Parses real literal.
        /// </summary>
        public override int Parse_RealLiteral()
        {
            int result = base.Parse_IntegerLiteral();
            if (IsCurrText('%'))
            {
                SetTypeId(result, (int)StandardType.Int);
                int v = Conversion.ToInt(GetVal(result));
                PutVal(result, v);
                Call_SCANNER();
            }
            else if (IsCurrText('&'))
            {
                SetTypeId(result, (int)StandardType.Long);
                long v = Conversion.ToLong(GetVal(result));
                PutVal(result, v);
                Call_SCANNER();
            }
            else if (IsCurrText('@'))
            {
                SetTypeId(result, (int)StandardType.Decimal);
                decimal v = Conversion.ToDecimal(GetVal(result));
                PutVal(result, v);
                Call_SCANNER();
            }
            else if (IsCurrText('!'))
            {
                SetTypeId(result, (int)StandardType.Float);
                float v = Conversion.ToFloat(GetVal(result));
                PutVal(result, v);
                Call_SCANNER();
            }
            else if (IsCurrText('#'))
            {
                SetTypeId(result, (int)StandardType.Double);
                double v = Conversion.ToDouble(GetVal(result));
                PutVal(result, v);
                Call_SCANNER();
            }
            else if (IsCurrText('$'))
            {
                SetTypeId(result, (int)StandardType.String);
                string v = Conversion.ToString(GetVal(result));
                PutVal(result, v);
                Call_SCANNER();
            }
            return result;
        }

		/// <summary>
		/// Reads new token.
		/// </summary>
		public override void Call_SCANNER()
		{
			base.Call_SCANNER();

			if (IsCurrText("Date"))
			{
				curr_token.id = DATETIME_CLASS_id;
				curr_token.tokenClass = TokenClass.Identifier;
			}

			if (IsCurrText('_'))
			{
				Call_SCANNER();
				while (IsLineTerminator())
					MatchLineTerminator();
			}
		}

		/// <summary>
		/// Parses identifier.
		/// </summary>
		public override int Parse_Ident()
		{
			if (IsCurrText('.'))
			{
				if (with_stack.Count == 0)
				{
					// Identifier expected
					RaiseError(true, Errors.CS1001);
					return 0;
				}
				else
				{
					int object_id = with_stack.Peek();
					REF_SWITCH = true;
					Match('.');
					int ref_id = Parse_Ident();
					Gen(code.OP_CREATE_REFERENCE, object_id, 0, ref_id);
					REF_SWITCH = false;
					return ref_id;
				}
			}
			else
				return base.Parse_Ident();
		}
	}

	#endregion VB_Parser Class
}

