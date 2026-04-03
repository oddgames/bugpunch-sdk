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
	#region Pascal_Parser Class
	/// <summary>
	/// Parser of Pascal language.
	/// </summary>
	internal class Pascal_Parser: BaseParser
	{

		#region LocalModifier Enum
		/// <summary>
		/// Modifier of local variable.
		/// </summary>
		public enum LocalModifier
		{
			Var,
			Const,
		}
		#endregion LocalModifier Enum

		#region Directive Enum
		/// <summary>
		/// Represents a directive.
		/// </summary>
		public enum Directive
		{
			Overload,
			Forward,
		}
		#endregion Directive Enum

		IntegerList variable_initializers;
		IntegerList static_variable_initializers;
		IntegerList param_ids;
		IntegerList param_type_ids;
		IntegerList param_mods;
		IntegerList local_variables;

		StringList total_modifier_list;

		bool has_constructor = false;
		bool valid_this_context = false;

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

		bool prefix = false;

		/// <summary>
		/// Parser VB_Parser constructor
		/// </summary>
		public Pascal_Parser(): base()
		{
			language = "Pascal";
			scanner = new Pascal_Scanner(this);
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
			total_modifier_list.AddObject("Override", Modifier.Override);
			total_modifier_list.AddObject("Overloads", Modifier.Overloads);
			total_modifier_list.AddObject("ReadOnly", Modifier.ReadOnly);
			total_modifier_list.AddObject("Shadows", Modifier.Shadows);

			keywords.Add("and");
			keywords.Add("as");
			keywords.Add("begin");
			keywords.Add("break");
			keywords.Add("case");
			keywords.Add("char");
			keywords.Add("class");
			keywords.Add("const");
			keywords.Add("constructor");
			keywords.Add("continue");
			keywords.Add("decimal");
			keywords.Add("default");
			keywords.Add("delegate");
			keywords.Add("destructor");
			keywords.Add("do");
			keywords.Add("double");
			keywords.Add("downto");
			keywords.Add("each");
			keywords.Add("else");
			keywords.Add("end");
			keywords.Add("exit");
			keywords.Add("false");
			keywords.Add("finally");
			keywords.Add("for");
			keywords.Add("forward");
			keywords.Add("function");
			keywords.Add("goto");
			keywords.Add("if");
			keywords.Add("in");
			keywords.Add("integer");
			keywords.Add("interface");
			keywords.Add("implementation");
			keywords.Add("initialization");
			keywords.Add("finalization");
			keywords.Add("is");
			keywords.Add("mod");
			keywords.Add("namespace");
			keywords.Add("nil");
			keywords.Add("not");
			keywords.Add("object");
			keywords.Add("of");
			keywords.Add("on");
			keywords.Add("or");
			keywords.Add("else");
			keywords.Add("override");
			keywords.Add("private");
			keywords.Add("program");
			keywords.Add("procedure");
			keywords.Add("property");
			keywords.Add("protected");
			keywords.Add("public");
			keywords.Add("read");
			keywords.Add("record");
			keywords.Add("repeat");
			keywords.Add("set");
			keywords.Add("short");
			keywords.Add("single");
			keywords.Add("static");
			keywords.Add("string");
			keywords.Add("then");
			keywords.Add("to");
			keywords.Add("true");
			keywords.Add("try");
			keywords.Add("type");
			keywords.Add("uses");
			keywords.Add("unit");
			keywords.Add("until");
			keywords.Add("var");
			keywords.Add("variant");
			keywords.Add("while");
			keywords.Add("with");
			keywords.Add("write");
			keywords.Add("xor");

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
		}

		/// <summary>
		/// Parses VB.NET program.
		/// </summary>
		public override void Parse_Program()
		{
			DECLARE_SWITCH = false;

			Gen(code.OP_UPCASE_ON, 0, 0, 0);
			Gen(code.OP_EXPLICIT_ON, 0, 0, 0);
			Gen(code.OP_STRICT_OFF, 0, 0, 0);

			int base_id = NewVar();
			SetName(base_id, "System");
			int id = NewRef("Math");
			Gen(code.OP_EVAL_TYPE, 0, 0, base_id);
			Gen(code.OP_CREATE_TYPE_REFERENCE, base_id, 0, id);
			Gen(code.OP_BEGIN_USING, id, 0, 0);

			Parse_Start();
		}

        internal virtual void Parse_Start()
		{
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

        internal virtual void Parse_UsesStatement()
		{
			Match("Uses");

			for (;;)
			{
				Parse_UsesClause();
				if (!CondMatch(','))
					break;
			}

			DECLARE_SWITCH = false;

			Match(';');
		}

		/// <summary>
		/// Parses Uses clause.
		/// </summary>
        internal virtual void Parse_UsesClause()
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

				string s;
				if (IsCurrText("in"))
				{
					Call_SCANNER();
					int file_id = Parse_StringLiteral();
					s = (string) GetVal(file_id);
				}
				else
					s = GetName(id) + ".pas";
				AddModuleFromFile(s);

			}
		}

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
			Match(';');
		}

		/// <summary>
		/// Parses type declaration.
		/// </summary>
        internal virtual void Parse_TypeDeclaration()
		{
			ModifierList ml = new ModifierList();

			if (IsCurrText("Program"))
				Parse_ProgramDeclaration(ml);
			else if (IsCurrText("Unit"))
				Parse_UnitDeclaration(ml);
			else
				Parse_NonProgramDeclaration(ml);
		}

        internal virtual void Parse_ProgramDeclaration(ModifierList ml)
		{
			Match("program");

			DECLARE_SWITCH = true;
			has_constructor = false;

			CheckModifiers(ml, class_modifiers);
			int class_id = Parse_Ident();
			Match(";");

			while (IsCurrText("Uses"))
				Parse_UsesStatement();

			BeginClass(class_id, ml);
			Gen(code.OP_ADD_ANCESTOR, class_id, ObjectClassId, 0);

			Parse_ClassBody(class_id, ml, true);

			if (static_variable_initializers.Count > 0)
				CreateDefaultStaticConstructor(class_id);

			ml.Add(Modifier.Static);	

			int sub_id = NewVar();
			SetName(sub_id, "Main");
			BeginMethod(sub_id, MemberKind.Method, ml, (int) StandardType.Void);

			InitMethod(sub_id);
			MoveSeparator();

			Gen(code.OP_INSERT_STRUCT_CONSTRUCTORS, class_id, 0, 0);

			Parse_CompoundStatement();
			EndMethod(sub_id);

			EndClass(class_id);
			Match(".");
		}

        internal virtual void Parse_ProcedureHeading(int class_id, ModifierList ml,
										ModifierList owner_ml, ClassKind ck)
		{
			Match("procedure");

			int sub_id;
			int type_id = (int) StandardType.Void;
			sub_id = BeginMethod(Parse_Ident(), MemberKind.Method, ml, type_id);

			if (IsCurrText('('))
			{
				Match('(');
				if (!IsCurrText(')'))
					Parse_ParameterList(sub_id, false);
				Match(')');
			}
			Match(";");
			Parse_DirectiveList();
			SetForward(sub_id, true);
			EndMethod(sub_id);
			DECLARE_SWITCH = true;
		}

		/// <summary>
		/// Parses Function heading.
		/// </summary>
        internal virtual void Parse_FunctionHeading(int class_id, ModifierList ml, ModifierList owner_ml,
									ClassKind ck)
		{
			Match("function");

			int type_id = (int) StandardType.Object;
			int sub_id = BeginMethod(Parse_Ident(), MemberKind.Method, ml, type_id);

			if (IsCurrText('('))
			{
				Match('(');
				if (!IsCurrText(')'))
					Parse_ParameterList(sub_id, false);
				Match(')');
			}

			Match(":");
			Parse_Attributes();
			type_id = Parse_Type();
			Match(";");

			DiscardInstruction(code.OP_ASSIGN_TYPE, sub_id, -1, -1);
			DiscardInstruction(code.OP_ASSIGN_TYPE, CurrResultId, -1, -1);
			Gen(code.OP_ASSIGN_TYPE, sub_id, type_id, 0);
			Gen(code.OP_ASSIGN_TYPE, CurrResultId, type_id, 0);

			Parse_DirectiveList();
			SetForward(sub_id, true);
			EndMethod(sub_id);

			DECLARE_SWITCH = true;
		}

        internal virtual void Parse_UnitDeclaration(ModifierList ml)
		{
			DECLARE_SWITCH = true;
			Match("Unit");
			int namespace_id = Parse_Ident();
			Match(';');
			Parse_InterfaceSection(namespace_id);

			if (static_variable_initializers.Count > 0)
				CreateDefaultStaticConstructor(namespace_id);

//			Gen(OP_END_INTERFACE_SECTION, CurrModule.NameIndex, 0, 0);
			Parse_ImplementationSection(namespace_id);
			Parse_InitSection(namespace_id);

			EndClass(namespace_id);
			Match('.');
		}

        internal virtual void Parse_InterfaceSection(int namespace_id)
		{
			ModifierList ml = new ModifierList();
			ml.Add(Modifier.Static);
			ModifierList owner_modifiers = new ModifierList();

			bool ok;
			Match("interface");
			while (IsCurrText("Uses"))
				Parse_UsesStatement();

//			Gen(code.OP_END_IMPORT, 0, 0, 0);
//			BeginNamespace(namespace_id);

			BeginClass(namespace_id, ml);
			Gen(code.OP_ADD_ANCESTOR, namespace_id, ObjectClassId, 0);

			for(;;)
			{
				ok = false;
				if (IsCurrText("var"))
				{
					Parse_VariableMemberDeclaration(namespace_id, ml, owner_modifiers, true);
					ok = true;
				}
				else if (IsCurrText("const"))
				{
					Parse_ConstantMemberDeclaration(namespace_id, ml, owner_modifiers, true);
					ok = true;
				}
				else if (IsCurrText("procedure"))
				{
					Parse_ProcedureHeading(namespace_id, ml, owner_modifiers, ClassKind.Class);
					ok = true;
				}
				else if (IsCurrText("function"))
				{
					Parse_FunctionHeading(namespace_id, ml, owner_modifiers, ClassKind.Class);
					ok = true;
				}
				else if (IsCurrText("type"))
				{
					Parse_TypeDeclaration(namespace_id, owner_modifiers);
					ok = true;
				}
				if (!ok) break;
			}
		}

        internal virtual void Parse_ImplementationSection(int namespace_id)
		{
			Match("implementation");
			while (IsCurrText("Uses"))
				Parse_UsesStatement();

			ModifierList ml = new ModifierList();
			ml.Add(Modifier.Static);
			ModifierList owner_modifiers = new ModifierList();

			for(;;)
			{
				bool ok = false;
				if (IsCurrText("var"))
				{
					Parse_VariableMemberDeclaration(namespace_id, ml, owner_modifiers, true);
					ok = true;
				}
				else if (IsCurrText("const"))
				{
					Parse_ConstantMemberDeclaration(namespace_id, ml, owner_modifiers, true);
					ok = true;
				}
				else if (IsCurrText("procedure"))
				{
					Parse_ProcedureDeclaration(namespace_id, ml, owner_modifiers, ClassKind.Class);
					ok = true;
				}
				else if (IsCurrText("function"))
				{
					Parse_FunctionDeclaration(namespace_id, ml, owner_modifiers, ClassKind.Class);
					ok = true;
				}
				else if (IsCurrText("type"))
				{
					Parse_TypeDeclaration(namespace_id, owner_modifiers);
					ok = true;
				}
				if (!ok) break;
			}
		}

        internal virtual void Parse_InitSection(int namespace_id)
		{
			if (IsCurrText("initialization"))
			{
//				BeginInitialization();
				Call_SCANNER();
				Parse_Statements();
//				EndInitialization();
				if (IsCurrText("finalization"))
				{
//					BeginFinalization();
					Call_SCANNER();
					Parse_Statements();
//					EndFinalization();
				}
				Match("end");
			}
			else if (IsCurrText("begin"))
			{
//				BeginInitialization();
				Call_SCANNER();
				Parse_Statements();
//				EndInitialization();
				Match("end");
			}
			else if (IsCurrText("end"))
			{
				 Call_SCANNER();
			}
			else
			  Match("end");
		}

        internal virtual void Parse_LocalDeclarationPart()
		{
			bool ok;
			for (;;)
			{
				ok = false;

				if (IsCurrText("var"))
				{
					Parse_LocalDeclarationStatement(LocalModifier.Var);
					ok = true;
				}
				else if (IsCurrText("const"))
				{
					Parse_LocalDeclarationStatement(LocalModifier.Const);
					ok = true;
				}
				if (!ok)
					break;
			}
		}

        internal virtual void Parse_NonProgramDeclaration(ModifierList ml)
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
		}

        internal virtual void Parse_ClassBody(int class_id, ModifierList owner_modifiers, bool IsModule)
		{
			variable_initializers.Clear();
//			static_variable_initializers.Clear();
			for (;;)
			{
				if (IsCurrText("Begin"))
					break;
				if (IsCurrText("End"))
					break;
				if (IsEOF())
					Match("End");
				Parse_ClassMemberDeclaration(class_id, owner_modifiers, IsModule,
					ClassKind.Class);
			}
		}

        internal virtual void Parse_ClassMemberDeclaration(int class_id, ModifierList owner_modifiers,
											bool IsModule, ClassKind ck)
		{
			Parse_Attributes();
			ModifierList ml = new ModifierList();
			ml.Add(Modifier.Public);

			if (IsCurrText("private"))
				Call_SCANNER();
			else if (IsCurrText("protected"))
				Call_SCANNER();
			if (IsCurrText("public"))
				Call_SCANNER();

			if (owner_modifiers.HasModifier(Modifier.Public))
			{
				if (!ml.HasModifier(Modifier.Private))
					ml.Add(Modifier.Public);
			}

			if (IsCurrText("type"))
			{
				Parse_TypeDeclaration(class_id, owner_modifiers);
			}
			else if (IsCurrText("var"))
			{
				if (IsModule)
					ml.Add(Modifier.Static);
				Parse_VariableMemberDeclaration(class_id, ml, owner_modifiers, IsModule);
			}
			else if (IsCurrText("const"))
			{
				ml.Add(Modifier.Static);
				Parse_ConstantMemberDeclaration(class_id, ml, owner_modifiers, IsModule);
			}
			else if (IsCurrText("constructor"))
			{
				if (IsModule)
					ml.Add(Modifier.Static);
				Parse_MethodMemberDeclaration(class_id, ml, owner_modifiers, ck);
			}
			else if (IsCurrText("destructor"))
			{
				if (IsModule)
					ml.Add(Modifier.Static);
				Parse_MethodMemberDeclaration(class_id, ml, owner_modifiers, ck);
			}
			else if (IsCurrText("procedure"))
			{
				if (IsModule)
					ml.Add(Modifier.Static);
				Parse_MethodMemberDeclaration(class_id, ml, owner_modifiers, ck);
			}
			else if (IsCurrText("function"))
			{
				if (IsModule)
					ml.Add(Modifier.Static);
				Parse_MethodMemberDeclaration(class_id, ml, owner_modifiers, ck);
			}
			else if (IsCurrText("property"))
			{
				if (IsModule)
					ml.Add(Modifier.Static);
				Parse_PropertyMemberDeclaration(class_id, ml, owner_modifiers, ck);
			}
			else
			{
				if (IsModule)
					ml.Add(Modifier.Static);
				Parse_VariableMemberDeclaration(class_id, ml, owner_modifiers, IsModule);
			}
		}

		/// <summary>
		/// Parses type declaration.
		/// </summary>
        internal virtual void Parse_TypeDeclaration(int class_id, ModifierList owner_modifiers)
		{
			ModifierList ml = new ModifierList();
			ml.Add(Modifier.Public);

			DECLARE_SWITCH = true;
			Match("type");

			for (;;)
			{
				bool ok = false;
				int type_id = Parse_Ident();
				DECLARE_SWITCH = false;
				Match('=');

				if (IsCurrText('('))
				{
					DECLARE_SWITCH = true;
					Parse_EnumTypeDeclaration(type_id);
					DECLARE_SWITCH = true;
					Match(';');
					ok = true;
				}
				else if (curr_token.tokenClass == TokenClass.IntegerConst)
				{
					Parse_SubrangeTypeDeclaration(type_id, StandardType.Int);
					DECLARE_SWITCH = true;
					Match(';');
					ok = true;
				}
				else if (curr_token.tokenClass == TokenClass.CharacterConst)
				{
					Parse_SubrangeTypeDeclaration(type_id, StandardType.Char);
					DECLARE_SWITCH = true;
					Match(';');
					ok = true;
				}
				else if (curr_token.tokenClass == TokenClass.BooleanConst)
				{
					Parse_SubrangeTypeDeclaration(type_id, StandardType.Bool);
					DECLARE_SWITCH = true;
					Match(';');
					ok = true;
				}
				else if (IsCurrText("class"))
				{
					DECLARE_SWITCH = true;
					Parse_ClassTypeDeclaration(type_id, ml);
					DECLARE_SWITCH = true;
					Match(';');
					ok = true;
				}
				else if (IsCurrText("record"))
				{
					DECLARE_SWITCH = true;
					Parse_RecordTypeDeclaration(type_id, ml);
					DECLARE_SWITCH = true;
					Match(';');
					ok = true;
				}
				else if (IsCurrText("array"))
				{
					DECLARE_SWITCH = true;
					Parse_ArrayTypeDeclaration(type_id, ml);
					DECLARE_SWITCH = true;
					Match(';');
					ok = true;
				}

				if (!ok)
					break;

				if (curr_token.tokenClass == TokenClass.Keyword)
					break;
			}
		}

        internal virtual void Parse_EnumTypeDeclaration(int enum_id)
		{
			int owner_id = CurrLevel;
			int owner_field_id = NewVar();

			ModifierList ml = new ModifierList();
			ml.Add(Modifier.Public);
			ml.Add(Modifier.Static);

			DECLARE_SWITCH = true;
			int type_base = (int) StandardType.Int;
			BeginEnum(enum_id, ml, type_base);
			Gen(code.OP_ADD_UNDERLYING_TYPE, enum_id, type_base, 0);

			int k = -1;
			static_variable_initializers.Clear();

			Match("(");
			for (;;)
			{
				if (IsEOF())
					Match(")");

				// parse enum field

				int id = Parse_Ident();
				SetName(owner_field_id, GetName(id));

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

					Gen(code.OP_ASSIGN, owner_field_id, expr_id, owner_field_id);
				}
				else
				{
					k++;
					expr_id = NewConst(k);
					Gen(code.OP_ASSIGN, id, expr_id, id);
					SetTypeId(expr_id, type_base);

					Gen(code.OP_ASSIGN, owner_field_id, expr_id, owner_field_id);
				}
				EndMethod(sub_id);
				EndField(id);
				EndField(owner_field_id);

				DECLARE_SWITCH = true;

				if (NotMatch(","))
					break;
			}

			CreateDefaultStaticConstructor(enum_id);

			DECLARE_SWITCH = true;
			EndEnum(enum_id);
			Match(")");
		}

        internal virtual void Parse_ClassTypeDeclaration(int class_id, ModifierList ml)
		{
			DECLARE_SWITCH = true;
			has_constructor = false;

			CheckModifiers(ml, class_modifiers);
			if (ml.HasModifier(Modifier.Abstract) && (ml.HasModifier(Modifier.Sealed)))
			{
				// The class 'class' is abstract and sealed
				RaiseError(false, Errors.CS0502);
			}
			BeginClass(class_id, ml);

			Match("class");
			if (IsCurrText('('))
				Parse_ClassBase(class_id);
			else
				Gen(code.OP_ADD_ANCESTOR, class_id, ObjectClassId, 0);
			Parse_ClassBody(class_id, ml, false);
			if (static_variable_initializers.Count > 0)
				CreateDefaultStaticConstructor(class_id);
			if (!has_constructor)
				CreateDefaultConstructor(class_id, false);
			EndClass(class_id);
			Match("end");
		}

		/// <summary>
		/// Parses record declaration.
		/// </summary>
        internal virtual void Parse_RecordTypeDeclaration(int struct_id, ModifierList ml)
		{
			CheckModifiers(ml, structure_modifiers);
			BeginStruct(struct_id, ml);
			Match("record");
			Gen(code.OP_ADD_ANCESTOR, struct_id, ObjectClassId, 0);
			Parse_ClassBody(struct_id, ml, false);
			if (static_variable_initializers.Count > 0)
				CreateDefaultStaticConstructor(struct_id);
			CreateDefaultConstructor(struct_id, true);
			EndStruct(struct_id);
			Match("end");
		}

		/// <summary>
		/// Parses array declaration.
		/// </summary>
        internal virtual void Parse_ArrayTypeDeclaration(int array_id, ModifierList ml)
		{
			CheckModifiers(ml, structure_modifiers);
			BeginArray(array_id, ml);
			Match("array");
			IntegerList l = new IntegerList(false);
			l.Add(array_id);

			if (IsCurrText('['))
			{
				Match('[');
				for (;;)
				{
					Gen(code.OP_ADD_ARRAY_RANGE, array_id, Parse_OrdinalType(), 0);
					if (IsCurrText(','))
					{
						Match(',');
						array_id = NewVar();
						BeginArray(array_id, ml);
						l.Add(array_id);
					}
					else
						break;
				}
				Match(']');
			}
			else
				Gen(code.OP_ADD_ARRAY_RANGE, array_id, (int) StandardType.Int, 0);
    
			Match("of");
			l.Add(Parse_Type());

			for (int i = l.Count - 1; i > 0; i--)
			{
				Gen(code.OP_ADD_ARRAY_INDEX, l[i - 1], l[i], 0);
				EndArray(l[i - 1]);
			}
			CreateDefaultConstructor(l[0], false);
		}

		/// <summary>
		/// Parses subrange declaration.
		/// </summary>
        internal virtual void Parse_SubrangeTypeDeclaration(int type_id, StandardType type_base)
		{
			BeginSubrange(type_id, type_base);
			Gen(code.OP_ADD_MIN_VALUE, type_id, Parse_Expression(), 0);
			Match("..");
			Gen(code.OP_ADD_MAX_VALUE, type_id, Parse_Expression(), 0);
			EndSubrange(type_id);
		}

		/// <summary>
		/// Parses class base.
		/// </summary>
        internal virtual void Parse_ClassBase(int class_id)
		{
			Match('(');
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
			Match(')');
		}

		/// <summary>
		/// Parses variable member declaration.
		/// </summary>
        internal virtual void Parse_VariableMemberDeclaration(int class_id, ModifierList ml, ModifierList owner_modifiers, bool IsModule)
		{
			if (IsCurrText("var"))
				Match("var");
			for (;;)
			{
				Parse_VariableDeclarator(ml, IsModule);
				Match(";");
				if (curr_token.tokenClass != TokenClass.Identifier)
					break;
			}
		}

		/// <summary>
		/// Parses variable declaration.
		/// </summary>
        internal virtual void Parse_VariableDeclarator(ModifierList ml, bool IsModule)
		{
			DECLARE_SWITCH = false;
			
			PaxArrayList bound_list = new PaxArrayList();
			PaxArrayList name_modifier_list = new PaxArrayList();
			IntegerList l = Parse_VariableIdentifiers(bound_list, name_modifier_list);

			Match(":");
			int type_id = Parse_Type();

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
			if (IsCurrText('['))
				Gen(code.OP_ASSIGN, field_id, Parse_ArrayInitializer(), field_id);
			else
				Gen(code.OP_ASSIGN, field_id, Parse_Expression(), field_id);
			EndMethod(sub_id);
			DECLARE_SWITCH = true;
		}

		/// <summary>
		/// Parses array initializer.
		/// </summary>
        internal virtual int Parse_ArrayInitializer()
		{
			int array_type_id = ArrayOfObjectClassId; 

			string array_type_name = GetName(array_type_id);
			int bounds_count = PaxSystem.GetRank(array_type_name);
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
				if (IsCurrText('['))
				{
					Match('[');

					level ++;
					if (level == bounds_count - 1)
					{
						curr_index[level] = -1;
						for (;;)
						{
							if (IsCurrText(']'))
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
				else if (IsCurrText(']'))
				{
					Match(']');

					if (bound[level] != curr_index[level])
						RaiseError(true, Errors.CS0178);

					fixed_bound[level] = 1;
					curr_index[level] --;

					level --;
					if (level == -1)
						break;
				}
				else
					Match('['); // error
			}

			for (int i = 0; i < bounds_count; i++)
				PutVal(ids[i], bound[i] + 1);

			return result;
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
			if (IsCurrText('['))
				s = Parse_ArrayNameModifier(bounds);
			else
				s = "";
			return result;
		}

		/// <summary>
		/// Parses array name modifier.
		/// </summary>
        internal virtual string Parse_ArrayNameModifier(IntegerList bounds)
		{
			string result = "";

			Match('[');
			result += "[";

			if (!IsCurrText(']'))
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

			Match(']');
			result += "]";

			return result;
		}

        internal virtual void Parse_ConstantMemberDeclaration(int class_id, ModifierList ml, ModifierList owner_modifiers, bool IsModule)
		{
			DECLARE_SWITCH = true;
			Match("const");
			for (;;)
			{
				Parse_ConstantDeclarator(ml, IsModule);
				Match(";");
				if (curr_token.tokenClass == TokenClass.Keyword)
					break;
			}
		}

		/// <summary>
		/// Parses constant declaration.
		/// </summary>
        internal virtual void Parse_ConstantDeclarator(ModifierList ml, bool IsModule)
		{
			int id = Parse_Ident();

			int type_id = ObjectClassId;
			if (IsCurrText(":"))
			{
				Match(":");
				type_id = Parse_Type();
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

        internal virtual void Parse_MethodMemberDeclaration(int class_id, ModifierList ml, ModifierList owner_ml, ClassKind ck)
		{
			DECLARE_SWITCH = true;
			if (IsCurrText("procedure"))
				Parse_ProcedureDeclaration(class_id, ml, owner_ml, ck);
			else if (IsCurrText("function"))
				Parse_FunctionDeclaration(class_id, ml, owner_ml, ck);
			else if (IsCurrText("constructor"))
				Parse_ConstructorDeclaration(class_id, ml, owner_ml, ck);
			else if (IsCurrText("destructor"))
				Parse_DestructorDeclaration(class_id, ml, owner_ml, ck);
		}

        internal virtual int Parse_ParameterList(int sub_id, bool isIndexer)
		{
			param_ids.Clear();
			param_type_ids.Clear();
			param_mods.Clear();

			int result = 0;
			for (;;)
			{
				result ++;

				Parse_Attributes();

				ParamMod mod = ParamMod.None;

				int type_id = ObjectClassId;
				if (IsCurrText("var"))
				{
					if (isIndexer)
						// Indexers can't have ref or out parameters
						RaiseError(false, Errors.CS0631);
					Match("var");
					mod = ParamMod.RetVal;
				}

				for (;;)
				{
					param_ids.Add(Parse_Ident());
					if (NotMatch(","))
						break;
				}

				Match(":");
				type_id = Parse_Type();

				int default_value_id = 0;
				if (IsCurrText("="))
				{
					Match('=');
					default_value_id = Parse_Expression();
				}

				while (param_type_ids.Count < param_ids.Count)
				{
					int i = param_type_ids.Count;
					int param_id = param_ids[i];

					Gen(code.OP_ASSIGN_TYPE, param_id, type_id, 0);
					Gen(code.OP_ADD_PARAM, sub_id, param_id, (int) mod);

					if (default_value_id != 0)
						Gen(code.OP_ADD_DEFAULT_VALUE, sub_id, param_id, default_value_id);

					param_type_ids.Add(type_id);
					param_mods.Add((int) mod);
				}

				if (!CondMatch(';')) break;
			}
			return result;
		}

		/// <summary>
		/// Parses constructor declaration.
		/// </summary>
        internal virtual void Parse_ConstructorDeclaration(int class_id, ModifierList ml,
								  ModifierList owner_ml, ClassKind ck)
		{
			Match("constructor");

			valid_this_context = true;
			bool IsStatic = ml.HasModifier(Modifier.Static);

			int sub_id = BeginMethod(Parse_Ident(), MemberKind.Constructor, ml, (int)StandardType.Void);

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
			Match(";");
			if (Parse_ForwardMethodDeclaration(sub_id))
				return;

			InitMethod(sub_id);
			Parse_LocalDeclarationPart();

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
			Parse_CompoundStatement();
			Match(";");
			EndMethod(sub_id);
			valid_this_context = false;

			if (IsStatic)
				static_variable_initializers.Clear(); // already processed
			else
				has_constructor = true;

			DECLARE_SWITCH = true;
		}

        internal virtual void Parse_DestructorDeclaration(int class_id, ModifierList ml,
										ModifierList owner_ml, ClassKind ck)
		{
			Match("destructor");

			int type_id = (int) StandardType.Void;
			valid_this_context = true;

			int sub_id = BeginMethod(Parse_Ident(), MemberKind.Method, ml, type_id);
			Match(";");
			if (Parse_ForwardMethodDeclaration(sub_id))
				return;

			if (ck != ClassKind.Interface)
			{
				InitMethod(sub_id);
				Parse_LocalDeclarationPart();

				if (ml.HasModifier(Modifier.Abstract))
				{
					string method_name = GetName(sub_id);
					// 'class member' cannot declare a body because it is marked abstract
					RaiseErrorEx(false, Errors.CS0500, method_name);
				}

				DECLARE_SWITCH = false;
				Parse_CompoundStatement();
				Match(";");
			}

			EndMethod(sub_id);
			valid_this_context = false;
			DECLARE_SWITCH = true;
		}

        internal virtual void Parse_ProcedureDeclaration(int class_id, ModifierList ml,
										ModifierList owner_ml, ClassKind ck)
		{
			Match("procedure");

			int sub_id;
			int type_id = (int) StandardType.Void;
			valid_this_context = true;

			sub_id = BeginMethod(Parse_Ident(), MemberKind.Method, ml, type_id);

			if (IsCurrText('('))
			{
				Match('(');
				if (!IsCurrText(')'))
					Parse_ParameterList(sub_id, false);
				Match(')');
			}

			Match(";");

			if (Parse_ForwardMethodDeclaration(sub_id))
				return;

			if (ck != ClassKind.Interface)
			{
				InitMethod(sub_id);
				Parse_LocalDeclarationPart();

				if (ml.HasModifier(Modifier.Abstract))
				{
					string method_name = GetName(sub_id);
					// 'class member' cannot declare a body because it is marked abstract
					RaiseErrorEx(false, Errors.CS0500, method_name);
				}

				if (GetName(sub_id) == "Main")
					Gen(code.OP_CHECKED, TRUE_id, 0, 0);

				DECLARE_SWITCH = false;
				Parse_CompoundStatement();
				Match(";");

				if (GetName(sub_id) == "Main")
					Gen(code.OP_RESTORE_CHECKED_STATE, 0, 0, 0);
			}

			EndMethod(sub_id);
			valid_this_context = false;
			DECLARE_SWITCH = true;
		}

		/// <summary>
		/// Parses Function declaration.
		/// </summary>
        internal virtual void Parse_FunctionDeclaration(int class_id, ModifierList ml, ModifierList owner_ml,
									ClassKind ck)
		{
			Match("function");

			int type_id = (int) StandardType.Object;
			valid_this_context = true;
			int sub_id = BeginMethod(Parse_Ident(), MemberKind.Method, ml, type_id);

			if (IsCurrText('('))
			{
				Match('(');
				if (!IsCurrText(')'))
					Parse_ParameterList(sub_id, false);
				Match(')');
			}

			Match(":");
			Parse_Attributes();
			type_id = Parse_Type();
			Match(";");

			if (Parse_ForwardMethodDeclaration(sub_id))
				return;
				
			DiscardInstruction(code.OP_ASSIGN_TYPE, sub_id, -1, -1);
			DiscardInstruction(code.OP_ASSIGN_TYPE, CurrResultId, -1, -1);
			Gen(code.OP_ASSIGN_TYPE, sub_id, type_id, 0);
			Gen(code.OP_ASSIGN_TYPE, CurrResultId, type_id, 0);

			IntegerList DirectiveList = Parse_DirectiveList();
			if (DirectiveList.IndexOf((int)Directive.Forward) >= 0)
			{
				SetForward(sub_id, true);
				EndMethod(sub_id);
				return;
			}

			SetName(CurrResultId, "result");
			Gen(code.OP_DECLARE_LOCAL_VARIABLE, CurrResultId, CurrSubId, 0);

			if (ml.HasModifier(Modifier.Extern))
			{
				// 'member' cannot be extern and declare a body
				RaiseErrorEx(false, Errors.CS0179, GetName(sub_id));
			}

			if (ck != ClassKind.Interface)
			{
				InitMethod(sub_id);
				Parse_LocalDeclarationPart();

				if (ml.HasModifier(Modifier.Abstract))
				{
					string method_name = GetName(sub_id);
					// 'class member' cannot declare a body because it is marked abstract
					RaiseErrorEx(false, Errors.CS0500, method_name);
				}

				if (GetName(sub_id) == "Main")
					Gen(code.OP_CHECKED, TRUE_id, 0, 0);

				DECLARE_SWITCH = false;
				Parse_CompoundStatement();
				Match(";");

				if (GetName(sub_id) == "Main")
					Gen(code.OP_RESTORE_CHECKED_STATE, 0, 0, 0);

			}
			EndMethod(sub_id);
			valid_this_context = false;
			DECLARE_SWITCH = true;
		}

		/// <summary>
		/// Parses property member declaration.
		/// </summary>
        internal virtual void Parse_PropertyMemberDeclaration(int class_id, ModifierList ml, ModifierList owner_modifiers,
											ClassKind ck)
		{
			DECLARE_SWITCH = true;
			Match("property");

			int id = Parse_Ident();

			param_ids.Clear();
			param_type_ids.Clear();
			param_mods.Clear();

			IntegerList new_param_ids = new IntegerList(true);

			if (IsCurrText('['))
			{
				Match('[');
				if (!IsCurrText(']'))
					Parse_ParameterList(id, false);
				Match(']');
			}

			DECLARE_SWITCH = false;

			Match(":");
			int type_id = Parse_Type();

			DiscardInstruction(code.OP_ASSIGN_TYPE, id, -1, -1);
			Gen(code.OP_ASSIGN_TYPE, id, type_id, 0);

			int read_id = 0;
			int write_id = 0;

			if (IsCurrText("read"))
			{
				Call_SCANNER();
				read_id = Parse_Ident();
				string s = GetName(read_id);
				DiscardInstruction(code.OP_EVAL, 0, 0, read_id);
				SetName(read_id, "");
				read_id = LookupID(s);
				if (read_id == 0)
					RaiseErrorEx(true, Errors.UNDECLARED_IDENTIFIER, s);
			}

			if (IsCurrText("write"))
			{
				Call_SCANNER();
				write_id = Parse_Ident();
				string s = GetName(write_id);
				DiscardInstruction(code.OP_EVAL, 0, 0, write_id);
				SetName(write_id, "");
				write_id = LookupID(s);
				if (write_id == 0)
					RaiseErrorEx(true, Errors.UNDECLARED_IDENTIFIER, s);
			}

			BeginProperty(id, ml, type_id, 0);

			if (read_id > 0)
			{
				valid_this_context = true;
				int sub_id = NewVar();
				SetName(sub_id, "get_" + GetName(id));
				BeginMethod(sub_id, MemberKind.Method, ml, type_id);

				new_param_ids.Clear();
				for (int i = 0; i < param_ids.Count; i++)
				{
					DiscardInstruction(code.OP_ADD_PARAM, id, -1, -1);

					int param_id = NewVar();
					SetName(param_id, GetName(param_ids[i]));
					Gen(code.OP_ASSIGN_TYPE, param_id, param_type_ids[i], 0);
					Gen(code.OP_ADD_PARAM, sub_id, param_id, 0);

					new_param_ids.Add(param_id);
				}
				InitMethod(sub_id);

				if (GetKind(read_id) == MemberKind.Method)
				{
					int ref_id = NewRef(GetName(read_id));
					Gen(code.OP_CREATE_REFERENCE, CurrThisID, 0, ref_id);

					Gen(code.OP_BEGIN_CALL, ref_id, 0, 0);
					for (int i = 0; i < param_ids.Count; i++)
					{
						Gen(code.OP_PUSH, new_param_ids[i], 0, ref_id);
					}
					
					Gen(code.OP_PUSH, CurrThisID, 0, 0);
					Gen(code.OP_CALL, ref_id, param_ids.Count, CurrResultId);
				}
				else if (GetKind(read_id) == MemberKind.Field)
				{
					if (param_ids.Count > 0)
						//Incompatible types
						RaiseError(false, Errors.PAS0002);

					int ref_id = NewRef(GetName(read_id));
					Gen(code.OP_CREATE_REFERENCE, CurrThisID, 0, ref_id);
					Gen(code.OP_ASSIGN, CurrResultId, ref_id, CurrResultId);
				}
				else
					RaiseError(false, Errors.PAS0001);

				EndMethod(sub_id);
				Gen(code.OP_ADD_READ_ACCESSOR, id, sub_id, 0);
				valid_this_context = false;
			}

			if (write_id > 0)
			{
				valid_this_context = true;
				int sub_id = NewVar();
				SetName(sub_id, "set_" + GetName(id));
				BeginMethod(sub_id, MemberKind.Method, ml, type_id);

				new_param_ids.Clear();
				int param_id;
				for (int i = 0; i < param_ids.Count; i++)
				{
					DiscardInstruction(code.OP_ADD_PARAM, id, -1, -1);

					param_id = NewVar();
					SetName(param_id, GetName(param_ids[i]));
					Gen(code.OP_ASSIGN_TYPE, param_id, param_type_ids[i], 0);
					Gen(code.OP_ADD_PARAM, sub_id, param_id, 0);

					new_param_ids.Add(param_id);
				}

				param_id = NewVar();
				SetName(param_id, "value");
				Gen(code.OP_ADD_PARAM, sub_id, param_id, 0);
				Gen(code.OP_ASSIGN_TYPE, param_id, type_id, 0);
				new_param_ids.Add(param_id);

				InitMethod(sub_id);

				if (GetKind(write_id) == MemberKind.Method)
				{
					int ref_id = NewRef(GetName(write_id));
					Gen(code.OP_CREATE_REFERENCE, CurrThisID, 0, ref_id);

					Gen(code.OP_BEGIN_CALL, ref_id, 0, 0);
					for (int i = 0; i < new_param_ids.Count; i++)
					{
						Gen(code.OP_PUSH, new_param_ids[i], 0, ref_id);
					}

					Gen(code.OP_PUSH, CurrThisID, 0, 0);
					Gen(code.OP_CALL, ref_id, new_param_ids.Count, CurrResultId);
				}
				else if (GetKind(write_id) == MemberKind.Field)
				{
					if (param_ids.Count > 0)
						//Incompatible types
						RaiseError(false, Errors.PAS0002);

					int ref_id = NewRef(GetName(write_id));
					Gen(code.OP_CREATE_REFERENCE, CurrThisID, 0, ref_id);
					Gen(code.OP_ASSIGN, ref_id, param_id, ref_id);
				}
				else
					RaiseError(false, Errors.PAS0001);

				EndMethod(sub_id);
				Gen(code.OP_ADD_WRITE_ACCESSOR, id, sub_id, 0);
				valid_this_context = false;
			}

			Match(";");
			if (IsCurrText("default"))
			{
				Call_SCANNER();
				if (param_ids.Count == 0)
				{
					// Properties with no required parameters cannot be declared 'Default'.
					RaiseError(false, Errors.VB00004);
				}
				else
				{
					Gen(code.OP_SET_DEFAULT, id, 0, 0);
				}
				Match(";");
			}
			EndProperty(id);
			DECLARE_SWITCH = true;
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

        internal virtual void Parse_Attributes()
		{
		}

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

			if (!result.HasModifier(Modifier.Private))
				result.Add(Modifier.Public);

			return result;
		}

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

		// STATEMENTS //////////////////////////////////////////////////////////

		/// <summary>
		/// Parses statement list.
		/// </summary>
        internal virtual void Parse_Statements()
		{
			for (;;)
			{
				if (IsEOF())
					break;
				if (IsCurrText("End"))
					break;

				Parse_Statement();
				if (NotMatch(';'))
					break;
			}
		}

		/// <summary>
		/// Parses statement.
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
			}
			else
				Backup_SCANNER(k);

			if (IsCurrText("begin"))
				Parse_CompoundStatement();
			else if (IsCurrText("if"))
				Parse_IfStatement();
			else if (IsCurrText("case"))
				Parse_CaseStatement();
			else if (IsCurrText("goto"))
				Parse_GotoStatement();
			else if (IsCurrText("break"))
				Parse_BreakStatement();
			else if (IsCurrText("continue"))
				Parse_ContinueStatement();
			else if (IsCurrText("exit"))
				Parse_ExitStatement();
			else if (IsCurrText("while"))
				Parse_WhileStatement();
			else if (IsCurrText("repeat"))
				Parse_RepeatStatement();
			else if (IsCurrText("for"))
				Parse_ForStatement();
			else if (IsCurrText("print"))
				Parse_PrintStatement();
			else if (IsCurrText("println"))
				Parse_PrintlnStatement();
			else
				Parse_AssignmentStatement();
		}

		/// <summary>
		/// Parses Compound Statement.
		/// </summary>
        internal virtual void Parse_CompoundStatement()
		{
			DECLARE_SWITCH = false;
			Match("begin");
			Parse_Statements();
			Match("end");
		}

		/// <summary>
		/// Parses If Statement.
		/// </summary>
        internal virtual void Parse_IfStatement()
		{
			int lf, lg;
			Match("if");
			lf = NewLabel();
			Gen(code.OP_GO_FALSE, lf, Parse_Expression(), 0);
			Match("then");
			Parse_Statement();
			if (IsCurrText("else"))
			{
				lg = NewLabel();
				Gen(code.OP_GO, lg, 0, 0);
				SetLabelHere(lf);
				Match("else");
				Parse_Statement();
				SetLabelHere(lg);
			}
			else
				SetLabelHere(lf);
		}

        internal virtual void Parse_CaseStatement()
		{
			int lg, lf, lt, lc, id, expr1_id, cond_id;
			Match("case");
			lg = NewLabel();
			cond_id = NewTempVar();
			id = Parse_Expression();
			Match("of");
			for (;;)
			{
				// Parse case selector
				lt = NewLabel();
				lf = NewLabel();
				for (;;)
				{
					lc = NewLabel();
					expr1_id = Parse_ConstantExpression();

					if (IsCurrText(".."))
					{
						Gen(code.OP_GE, id, expr1_id, cond_id);
						Gen(code.OP_GO_FALSE, lc, cond_id, 0);
						Match("..");
						Gen(code.OP_LE, id, Parse_ConstantExpression(), cond_id);
						Gen(code.OP_GO_FALSE, lc, cond_id, 0);
					}
					else
						Gen(code.OP_EQ, id, expr1_id, cond_id);
					Gen(code.OP_GO_TRUE, lt, cond_id, 0);
					SetLabelHere(lc);

					if (NotMatch(','))
						break;
				}
				Match(':');
				Gen(code.OP_GO, lf, 0, 0);
				SetLabelHere(lt);
				Parse_Statement();
				Gen(code.OP_GO, lg, 0, 0);
				SetLabelHere(lf);
				// end of case selector
				if (NotMatch(';'))
					break;
				if (IsCurrText("else"))
					break;
				if (IsCurrText("end"))
					break;
		  }

		  if (IsCurrText("else"))
		  {
			Match("else");
			Parse_Statement();
		  }
		  if (IsCurrText(';'))
			Match(';');
		  Match("end");
		  SetLabelHere(lg);
		}

		/// <summary>
		/// Parses Goto Statement.
		/// </summary>
        internal virtual void Parse_GotoStatement()
		{
			Match("Goto");
			int l = Parse_Ident();
			PutKind(l, MemberKind.Label);
			Gen(code.OP_GOTO_START, l, 0, 0);
		}

		/// <summary>
		/// Parses Break Statement.
		/// </summary>
        internal virtual void Parse_BreakStatement()
		{
			if (BreakStack.Count == 0)
				// No enclosing loop out of which to break or continue
				RaiseError(false, Errors.CS0139);
			Gen(code.OP_GOTO_START, BreakStack.TopLabel(), 0, 0);
			Match("break");
		}

		/// <summary>
		/// Parses Continue Statement.
		/// </summary>
        internal virtual void Parse_ContinueStatement()
		{
			if (ContinueStack.Count == 0)
				// No enclosing loop out of which to break or continue
				RaiseError(false, Errors.CS0139);
			Gen(code.OP_GOTO_START, ContinueStack.TopLabel(), 0, 0);
			Match("continue");
		}

		/// <summary>
		/// Parses Exit Statement.
		/// </summary>
        internal virtual void Parse_ExitStatement()
		{
			Match("exit");
			Gen(code.OP_EXIT_SUB, 0, 0, 0);
		}

		/// <summary>
		/// Parses While Statement.
		/// </summary>
        internal virtual void Parse_WhileStatement()
		{
			int lf, lg;
			Match("while");
			lf = NewLabel();
			lg = NewLabel();
			SetLabelHere(lg);
			Gen(code.OP_GO_FALSE, lf, Parse_Expression(), 0);
			Match("do");
			BreakStack.Push(lf);
			ContinueStack.Push(lg);
			Parse_Statement();
			BreakStack.Pop();
			ContinueStack.Pop();
			Gen(code.OP_GO, lg, 0, 0);
			SetLabelHere(lf);
		}

		/// <summary>
		/// Parses Repeat Statement.
		/// </summary>
        internal virtual void Parse_RepeatStatement()
		{
			int lf, lg;
			Match("repeat");
			lf = NewLabel();
			lg = NewLabel();
			SetLabelHere(lf);
			for (;;)
			{
				if (IsCurrText("until"))
				  break;
				if (IsEOF())
				  break;

				BreakStack.Push(lg);
				ContinueStack.Push(lf);
				Parse_Statement();
				BreakStack.Pop();
				ContinueStack.Pop();
				if (NotMatch(';'))
					break;
			}

			Match("until");
			Gen(code.OP_GO_FALSE, lf, Parse_Expression(), 0);
			SetLabelHere(lg);
		}

		/// <summary>
		/// Parses For Statement.
		/// </summary>
        internal virtual void Parse_ForStatement()
		{
			int id, expr1_id, expr2_id, limit_cond_id1, limit_cond_id2;
			bool i;
			int lf, lg;
			Match("for");
			lf = NewLabel();
			lg = NewLabel();
			limit_cond_id1 = NewTempVar();
			limit_cond_id2 = NewTempVar();
			id = Parse_Ident();
			Match(":=");
			expr1_id = Parse_Expression();
			Gen(code.OP_ASSIGN, id, expr1_id, id);
			if (IsCurrText("downto"))
			{
				Match("downto");
				i = false;
			}
			else
			{
				Match("to");
				i = true;
			}
			expr2_id = Parse_Expression();
			Gen(code.OP_GT, expr1_id, expr2_id, limit_cond_id1);
			Gen(code.OP_GO_TRUE, lg, limit_cond_id1, 0);
			Match("do");
			SetLabelHere(lf);
			BreakStack.Push(lg);
			ContinueStack.Push(lf);
			Parse_Statement();
			BreakStack.Pop();
			ContinueStack.Pop();
			if (i)
				Gen(code.OP_PLUS, id, NewConst(1), id);
			else
				Gen(code.OP_MINUS, id, NewConst(1), id);
			Gen(code.OP_GT, id, expr2_id, limit_cond_id2);
			Gen(code.OP_GO_FALSE, lf, limit_cond_id2, 0);
			SetLabelHere(lg);
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
		}

		/// <summary>
		/// Parses Local Declaration statement
		/// </summary>
        internal virtual void Parse_LocalDeclarationStatement(LocalModifier m)
		{
			local_variables.Clear();

			DECLARE_SWITCH = true;
			DECLARATION_CHECK_SWITCH = true;

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

					if(!CondMatch(',')) break;
				}

				DECLARE_SWITCH = false;

				if (m == LocalModifier.Var)
				{
					Match(":");
					type_id = Parse_Type();
				}
				else
				{
					if (IsCurrText(":"))
					{
						Match(":");
						type_id = Parse_Type();
					}
				}

				for (int i = 0; i < local_variables.Count; i++)
				{
					int id = local_variables[i];
					Gen(code.OP_ASSIGN_TYPE, id, type_id, 0);
				}

				if (IsCurrText('='))
				{
					DECLARE_SWITCH = false;
					Match('=');

					if (local_variables.Count == 1)
					{
						int id = local_variables[0];
						Gen(code.OP_ASSIGN, id, Parse_Expression(), id);
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

			Match(";");
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

			if (IsCurrText(":="))
			{
				Call_SCANNER();
				Gen(code.OP_ASSIGN, result, Parse_Expression(), result);
			}
			else if (TopInstruction.op != code.OP_CALL)
			{
				Gen(code.OP_BEGIN_CALL, result, 0, 0);
				Gen(code.OP_PUSH, CurrThisID, 0, 0);
				Gen(code.OP_CALL, result, 0, 0);
			}
		}

		// EXPRESSIONS /////////////////////////////////////////////////////////

        internal virtual int Parse_ConstantExpression()
		{
			return Parse_Expression();
		}

		public override int Parse_Expression()
		{
			int result = Parse_SimpleExpression();
			while (IsCurrText('=') || IsCurrText("<>") ||
				   IsCurrText('>') || IsCurrText(">=") ||
				   IsCurrText('<') || IsCurrText("<=")
				   )
			{
				if (IsCurrText('='))
				{
					Call_SCANNER();
					result = BinOp(code.OP_EQ, result, Parse_SimpleExpression());
				}
				else if (IsCurrText("<>"))
				{
					Call_SCANNER();
					result = BinOp(code.OP_NE, result, Parse_SimpleExpression());
				}
				else if (IsCurrText('>'))
				{
					Call_SCANNER();
					result = BinOp(code.OP_GT, result, Parse_SimpleExpression());
				}
				else if (IsCurrText(">="))
				{
					Call_SCANNER();
					result = BinOp(code.OP_GE, result, Parse_SimpleExpression());
				}
				else if (IsCurrText('<'))
				{
					Call_SCANNER();
					result = BinOp(code.OP_LT, result, Parse_SimpleExpression());
				}
				else if (IsCurrText("<="))
				{
					Call_SCANNER();
					result = BinOp(code.OP_LE, result, Parse_SimpleExpression());
				}
			}
			return result;
		}

        internal virtual int Parse_SimpleExpression()
		{
			int result = Parse_Term();
			while (IsCurrText('+') || IsCurrText('-') ||
				   IsCurrText("or") || IsCurrText("xor")
				   )
			{
				if (IsCurrText('+'))
				{
					Call_SCANNER();
					result = BinOp(code.OP_PLUS, result, Parse_Term());
				}
				else if (IsCurrText('-'))
				{
					Call_SCANNER();
					result = BinOp(code.OP_MINUS, result, Parse_Term());
				}
				else if (IsCurrText("or"))
				{
					Call_SCANNER();
					result = BinOp(code.OP_BITWISE_OR, result, Parse_Term());
				}
				else if (IsCurrText("xor"))
				{
					Call_SCANNER();
					result = BinOp(code.OP_BITWISE_XOR, result, Parse_Term());
				}
			}
			return result;
		}

        internal virtual int Parse_Term()
		{
			int result = Parse_Factor();
			while (IsCurrText('*') || IsCurrText('/') ||
				   IsCurrText("div") || IsCurrText("mod") ||
				   IsCurrText("and") || IsCurrText("shl") || IsCurrText("shr")
				   )
			{
				if (IsCurrText('*'))
				{
					Call_SCANNER();
					result = BinOp(code.OP_MULT, result, Parse_Factor());
				}
				else if (IsCurrText('/'))
				{
					Call_SCANNER();
					result = BinOp(code.OP_DIV, result, Parse_Factor());
				}
				else if (IsCurrText("div"))
				{
					Call_SCANNER();
					result = BinOp(code.OP_DIV, result, Parse_Factor());
				}
				else if (IsCurrText("mod"))
				{
					Call_SCANNER();
					result = BinOp(code.OP_MOD, result, Parse_Factor());
				}
				else if (IsCurrText("and"))
				{
					Call_SCANNER();
					result = BinOp(code.OP_BITWISE_AND, result, Parse_Factor());
				}
				else if (IsCurrText("shl"))
				{
					Call_SCANNER();
					result = BinOp(code.OP_LEFT_SHIFT, result, Parse_Factor());
				}
				else if (IsCurrText("shr"))
				{
					Call_SCANNER();
					result = BinOp(code.OP_RIGHT_SHIFT, result, Parse_Factor());
				}
			}
			return result;
		}

        internal virtual int Parse_Factor()
		{
			int result;

			if (IsCurrText('('))
			{
				//Parse parenthesized expression
				Match('(');
				result = Parse_Expression();
				Match(')');
			}
			// Parse instance expressions
			else if (IsCurrText("Self"))
			{
				Match("Self");
				result = CurrThisID;
				if (GetName(result)!= "this")
					// keyword this is not valid
					RaiseError(false, Errors.CS0026);
				if (!valid_this_context)
					RaiseError(false, Errors.CS0027);
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
			else if (IsCurrText("["))
			{
				result = Parse_ArrayInitializer();
			}
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
				}
				else if (IsCurrText('[')) // element access
				{
					int sub_id = result;
					result = NewVar();
					Match('[');
					Gen(code.OP_CALL, sub_id, Parse_ArgumentList(']', sub_id, CurrThisID), result);
					Match(']');
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

        internal virtual int Parse_OrdinalType()
		{
			int result;
			if (IsCurrText('('))
			{
				result = NewVar();
				Parse_EnumTypeDeclaration(result);
			}
			else if (curr_token.tokenClass == TokenClass.IntegerConst)
			{
				result = NewVar();
				Parse_SubrangeTypeDeclaration(result, StandardType.Int);
			}
			else if (curr_token.tokenClass == TokenClass.CharacterConst)
			{
				result = NewVar();
				Parse_SubrangeTypeDeclaration(result, StandardType.Char);
			}
			else if (curr_token.tokenClass == TokenClass.BooleanConst)
			{
				result = NewVar();
				Parse_SubrangeTypeDeclaration(result, StandardType.Bool);
			}
			else
			{
				result = Parse_NonArrayType();
			}
			return result;
		}

        internal virtual int Parse_Type()
		{
			int result;

			ModifierList ml = new ModifierList();
			if (IsCurrText("packed"))
			{
				Call_SCANNER();
			}

			if (IsCurrText('('))
			{
				result = NewVar();
				Parse_EnumTypeDeclaration(result);
				return result;
			}
			else if (curr_token.tokenClass == TokenClass.IntegerConst)
			{
				result = NewVar();
				Parse_SubrangeTypeDeclaration(result, StandardType.Int);
				return result;
			}
			else if (curr_token.tokenClass == TokenClass.CharacterConst)
			{
				result = NewVar();
				Parse_SubrangeTypeDeclaration(result, StandardType.Char);
				return result;
			}
			else if (curr_token.tokenClass == TokenClass.BooleanConst)
			{
				result = NewVar();
				Parse_SubrangeTypeDeclaration(result, StandardType.Bool);
				return result;
			}
			else if (IsCurrText("record"))
			{
				result = NewTempVar();
				DECLARE_SWITCH = true;
				Parse_RecordTypeDeclaration(result, ml);
				DECLARE_SWITCH = false;
				return result;
			}
			else if (IsCurrText("array"))
			{
				result = NewTempVar();
				DECLARE_SWITCH = true;
				Parse_ArrayTypeDeclaration(result, ml);
				DECLARE_SWITCH = false;
				return result;
			}

			result = Parse_NonArrayType();
			if (IsCurrText('['))
			{
				string s = Parse_TypeNameModifier();
				string type_name = GetName(result);
				type_name += s;
				result = NewVar();
				SetName(result, type_name);
				Gen(code.OP_EVAL_TYPE, 0, 0, result);
			}
			return result;
		}

        internal virtual string Parse_TypeNameModifier()
		{
			string result = "";

			Match('[');
			result += "[";

			if (!IsCurrText(']'))
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

			Match(']');
			result += "]";

			return result;
		}

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

        internal virtual int Parse_IdentOrType()
		{
			if (IsCurrText("Boolean"))
			{
				Call_SCANNER();
				return (int) StandardType.Bool;
			}
			else if (IsCurrText("Date"))
			{
				RaiseError(true, Errors.CS0001);
				return 0;
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
			else if (IsCurrText("Nil"))
			{
				Call_SCANNER();
				return NULL_id;
			}
			else
				return Parse_Ident();
		}

		/// <summary>
		/// Reads new token.
		/// </summary>
		public override void Call_SCANNER()
		{
			base.Call_SCANNER();

			if (IsCurrText("true"))
			{
				curr_token.tokenClass = TokenClass.BooleanConst;
                curr_token.id = TRUE_id;
			}
			else if (IsCurrText("false"))
			{
				curr_token.tokenClass = TokenClass.BooleanConst;
				curr_token.id = FALSE_id;
			}
		}

        internal virtual IntegerList Parse_DirectiveList()
		{
			IntegerList result = new IntegerList(false);
			for (;;)
			{
				if (IsCurrText("overload"))
				{
					Call_SCANNER();
					result.Add((int)Directive.Overload);
					Match(';');
				}
				else if (IsCurrText("forward"))
				{
				  Call_SCANNER();
				  result.Add((int)Directive.Forward);
				  Match(';');
				}
				else
					break;
			}
			return result;
		}

        internal virtual bool NotMatch(string s)
		{
			if (!IsCurrText(s))
				return true;
			Call_SCANNER();
			return false;
		}

        internal virtual bool NotMatch(char s)
		{
			if (!IsCurrText(s))
				return true;
			Call_SCANNER();
			return false;
		}

        internal virtual int NewTempVar()
		{
			return NewVar();
		}

		/// <summary>
		/// Emits beginning of method declaration.
		/// </summary>
		public override int BeginMethod(int sub_id, MemberKind k, ModifierList ml, int res_type_id)
		{
			if (IsCurrText('.'))
			{
				int class_id = LookupTypeID(GetName(sub_id));
				if (class_id == 0)
					RaiseErrorEx(true, Errors.UNDECLARED_IDENTIFIER, GetName(sub_id));
				Gen(code.OP_BEGIN_USING, class_id, 0, 0);
				level_stack.Push(class_id);
				Match('.');
				sub_id = Parse_Ident();
				prefix = true;
			}

			base.BeginMethod(sub_id, k, ml, res_type_id);
			return sub_id;
		}

		/// <summary>
		/// Emits beginning of method's statement list.
		/// </summary>
		public override void InitMethod(int sub_id)
		{
			ReplaceForwardDeclaration(sub_id);
			base.InitMethod(sub_id);
		}

		/// <summary>
		/// Emits end of method declaration.
		/// </summary>
		public override void EndMethod(int sub_id)
		{
			base.EndMethod(sub_id);
			if (prefix)
			{
				Gen(code.OP_END_USING, level_stack.Peek(), 0, 0);
				level_stack.Pop();
			}
			prefix = false;
		}

        internal virtual bool Parse_ForwardMethodDeclaration(int sub_id)
		{
			if (!prefix)
			{
				IntegerList DirectiveList = Parse_DirectiveList();
				if (DirectiveList.IndexOf((int)Directive.Forward) >= 0)
				{
					SetForward(sub_id, true);
					EndMethod(sub_id);
					return true;
				}

				if (IsCurrText("end") ||
					IsCurrText("procedure") ||
					IsCurrText("function") ||
					IsCurrText("constructor") ||
					IsCurrText("property") ||
					IsCurrText("destructor"))
				{
					SetForward(sub_id, true);
					EndMethod(sub_id);
					return true;
				}
			}
			return false;
		}
	}
	#endregion Pascal_Parser Class
}

