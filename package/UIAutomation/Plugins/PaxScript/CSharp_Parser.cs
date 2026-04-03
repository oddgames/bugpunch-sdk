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
	#region CSharp_Parser Class
	/// <summary>
	/// Parser of C# language.
	/// </summary>
	internal class CSharp_Parser: BaseParser
	{
		/// <summary> Assignment operators. </summary>
		 PaxHashTable assign_operators;
		/// <summary> Relational operators. </summary>
		 PaxHashTable relational_operators;
		/// <summary> Shift operators. </summary>
		 PaxHashTable shift_operators;
		/// <summary> Additive operators. </summary>
		 PaxHashTable additive_operators;
		/// <summary> Multiplicative operators. </summary>
		 PaxHashTable multiplicative_operators;

		/// <summary> Class declaration modifiers. </summary>
		 ModifierList class_modifiers;
		/// <summary> Const declaration modifiers. </summary>
		 ModifierList constant_modifiers;
		/// <summary> Field declaration modifiers. </summary>
		 ModifierList field_modifiers;
		/// <summary> Method declaration modifiers. </summary>
		 ModifierList method_modifiers;
		/// <summary> Operator declaration modifiers. </summary>
		 ModifierList operator_modifiers;
		/// <summary> Constructor declaration modifiers. </summary>
		 ModifierList constructor_modifiers;
		/// <summary> Destructor declaration modifiers. </summary>
		 ModifierList destructor_modifiers;
		/// <summary> Struct declaration modifiers. </summary>
		 ModifierList structure_modifiers;
		/// <summary> Interface declaration modifiers. </summary>
		 ModifierList interface_modifiers;
		/// <summary> Enum declaration modifiers. </summary>
		 ModifierList enum_modifiers;
		/// <summary> Delegate declaration modifiers. </summary>
		 ModifierList delegate_modifiers;

		/// <summary> Overloadable unary operators. </summary>
		 StringList overloadable_unary_operators;
		/// <summary> Overloadable binary operators. </summary>
		 StringList overloadable_binary_operators;

		/// <summary> Standard types. </summary>
		 internal Types standard_types;
		/// <summary> Integral types. </summary>
		 Types integral_types;

		/// <summary> List of ids of instance field initializers. </summary>
		IntegerList variable_initializers;
		/// <summary> List of ids of static field initializers. </summary>
		IntegerList static_variable_initializers;
		/// <summary> List of ids of formal parameters. </summary>
		IntegerList param_ids;
		/// <summary> List of ids of formal parameter types. </summary>
		IntegerList param_type_ids;
		/// <summary> List of ids of formal parameter modifiers. </summary>
		IntegerList param_mods;
		/// <summary> List of ids of local variables. </summary>
		IntegerList local_variables; // returned by Parse_LocalVariableDeclaration

		/// <summary> Determines accessor body context. </summary>
		bool ACCESSOR_SWITCH = false;
		/// <summary> 'true', if class declaration contains constructor declaration. </summary>
		bool has_constructor = false;
		/// <summary> If 'true', parser will not emit p-code. </summary>
		bool no_gen = false;
		/// <summary> If 'true', this is valid context for 'this' keyword. </summary>
		bool valid_this_context = false;

		/// <summary>
		/// List of all possible modifiers.
		/// </summary>
		protected StringList total_modifier_list;

        string html;
        StringList html_out = new StringList(true);
        StringList html_temp = new StringList(true);
        int html_pos; 

		/// <summary>
		/// Constructor.
		/// </summary>
		public CSharp_Parser(): base()
		{
			language = "CSharp";
			scanner = new CSharp_Scanner(this);

			upcase = false;

			variable_initializers = new IntegerList(false);
			static_variable_initializers = new IntegerList(false);
			param_ids = new IntegerList(true);
			param_type_ids = new IntegerList(true);
			param_mods = new IntegerList(true);
			local_variables = new IntegerList(false);

			keywords.Add("abstract");
			keywords.Add("as");
			keywords.Add("base");
			keywords.Add("bool");
			keywords.Add("break");
			keywords.Add("byte");
			keywords.Add("case");
			keywords.Add("catch");
			keywords.Add("char");
			keywords.Add("checked");
			keywords.Add("class");
			keywords.Add("const");
			keywords.Add("continue");
			keywords.Add("decimal");
			keywords.Add("default");
			keywords.Add("delegate");
			keywords.Add("do");
			keywords.Add("double");
			keywords.Add("else");
			keywords.Add("enum");
			keywords.Add("event");
			keywords.Add("explicit");
			keywords.Add("extern");
			keywords.Add("false");
			keywords.Add("finally");
			keywords.Add("fixed");
			keywords.Add("float");
			keywords.Add("for");
			keywords.Add("foreach");
			keywords.Add("goto");
			keywords.Add("if");
			keywords.Add("implicit");
			keywords.Add("in");
			keywords.Add("int");
			keywords.Add("interface");
			keywords.Add("internal");
			keywords.Add("is");
			keywords.Add("lock");
			keywords.Add("long");
			keywords.Add("namespace");
			keywords.Add("new");
			keywords.Add("null");
			keywords.Add("object");
			keywords.Add("operator");
			keywords.Add("out");
			keywords.Add("override");
			keywords.Add("params");
			keywords.Add("private");
			keywords.Add("protected");
			keywords.Add("public");
			keywords.Add("readonly");
			keywords.Add("ref");
			keywords.Add("return");
			keywords.Add("sbyte");
			keywords.Add("sealed");
			keywords.Add("short");
			keywords.Add("sizeof");
			keywords.Add("stackalloc");
			keywords.Add("static");
			keywords.Add("string");
			keywords.Add("struct");
			keywords.Add("switch");
			keywords.Add("this");
			keywords.Add("throw");
			keywords.Add("true");
			keywords.Add("try");
			keywords.Add("typeof");
			keywords.Add("uint");
			keywords.Add("ulong");
			keywords.Add("unchecked");
			keywords.Add("unsafe");
			keywords.Add("ushort");
			keywords.Add("using");
			keywords.Add("virtual");
			keywords.Add("void");
			keywords.Add("while");

			keywords.Add("print");
			keywords.Add("println");
			keywords.Add("function");

			total_modifier_list = new StringList(false);
			total_modifier_list.AddObject("new", Modifier.New);
			total_modifier_list.AddObject("public", Modifier.Public);
			total_modifier_list.AddObject("protected", Modifier.Protected);
			total_modifier_list.AddObject("internal", Modifier.Internal);
			total_modifier_list.AddObject("private", Modifier.Private);
			total_modifier_list.AddObject("abstract", Modifier.Abstract);
			total_modifier_list.AddObject("sealed", Modifier.Sealed);
			total_modifier_list.AddObject("static", Modifier.Static);
			total_modifier_list.AddObject("readonly", Modifier.ReadOnly);
			total_modifier_list.AddObject("volatile", Modifier.Volatile);
			total_modifier_list.AddObject("override", Modifier.Override);
			total_modifier_list.AddObject("virtual", Modifier.Virtual);
			total_modifier_list.AddObject("extern", Modifier.Extern);

			class_modifiers = new ModifierList();
			class_modifiers.Add(Modifier.New);
			class_modifiers.Add(Modifier.Public);
			class_modifiers.Add(Modifier.Protected);
			class_modifiers.Add(Modifier.Internal);
			class_modifiers.Add(Modifier.Private);
			class_modifiers.Add(Modifier.Abstract);
			class_modifiers.Add(Modifier.Sealed);

			constant_modifiers = new ModifierList();
			constant_modifiers.Add(Modifier.New);
			constant_modifiers.Add(Modifier.Public);
			constant_modifiers.Add(Modifier.Protected);
			constant_modifiers.Add(Modifier.Internal);
			constant_modifiers.Add(Modifier.Private);

			field_modifiers = new ModifierList();
			field_modifiers.Add(Modifier.New);
			field_modifiers.Add(Modifier.Public);
			field_modifiers.Add(Modifier.Protected);
			field_modifiers.Add(Modifier.Internal);
			field_modifiers.Add(Modifier.Private);
			field_modifiers.Add(Modifier.Static);
			field_modifiers.Add(Modifier.ReadOnly);
			field_modifiers.Add(Modifier.Volatile);

			method_modifiers = new ModifierList();
			method_modifiers.Add(Modifier.New);
			method_modifiers.Add(Modifier.Public);
			method_modifiers.Add(Modifier.Protected);
			method_modifiers.Add(Modifier.Internal);
			method_modifiers.Add(Modifier.Private);
			method_modifiers.Add(Modifier.Static);
			method_modifiers.Add(Modifier.Virtual);
			method_modifiers.Add(Modifier.Sealed);
			method_modifiers.Add(Modifier.Override);
			method_modifiers.Add(Modifier.Abstract);
			method_modifiers.Add(Modifier.Extern);

			operator_modifiers = new ModifierList();
			operator_modifiers.Add(Modifier.Public);
			operator_modifiers.Add(Modifier.Static);
			operator_modifiers.Add(Modifier.Extern);

			constructor_modifiers = new ModifierList();
			constructor_modifiers.Add(Modifier.Public);
			constructor_modifiers.Add(Modifier.Protected);
			constructor_modifiers.Add(Modifier.Internal);
			constructor_modifiers.Add(Modifier.Private);
			constructor_modifiers.Add(Modifier.Extern); // static only
			constructor_modifiers.Add(Modifier.Static); // static only

			destructor_modifiers = new ModifierList();
			destructor_modifiers.Add(Modifier.Extern);

			structure_modifiers = new ModifierList();
			structure_modifiers.Add(Modifier.New);
			structure_modifiers.Add(Modifier.Public);
			structure_modifiers.Add(Modifier.Protected);
			structure_modifiers.Add(Modifier.Internal);
			structure_modifiers.Add(Modifier.Private);

			interface_modifiers = new ModifierList();
			interface_modifiers.Add(Modifier.New);
			interface_modifiers.Add(Modifier.Public);
			interface_modifiers.Add(Modifier.Protected);
			interface_modifiers.Add(Modifier.Internal);
			interface_modifiers.Add(Modifier.Private);

			enum_modifiers = new ModifierList();
			enum_modifiers.Add(Modifier.New);
			enum_modifiers.Add(Modifier.Public);
			enum_modifiers.Add(Modifier.Protected);
			enum_modifiers.Add(Modifier.Internal);
			enum_modifiers.Add(Modifier.Private);

			delegate_modifiers = new ModifierList();
			delegate_modifiers.Add(Modifier.New);
			delegate_modifiers.Add(Modifier.Public);
			delegate_modifiers.Add(Modifier.Protected);
			delegate_modifiers.Add(Modifier.Internal);
			delegate_modifiers.Add(Modifier.Private);

			standard_types = new Types();
			standard_types.Add("", 0);
			standard_types.Add("void", StandardType.Void);
			standard_types.Add("bool", StandardType.Bool);
			standard_types.Add("byte", StandardType.Byte);
			standard_types.Add("char", StandardType.Char);
			standard_types.Add("decimal", StandardType.Decimal);
			standard_types.Add("double", StandardType.Double);
			standard_types.Add("float", StandardType.Float);
			standard_types.Add("int", StandardType.Int);
			standard_types.Add("long", StandardType.Long);
			standard_types.Add("sbyte", StandardType.Sbyte);
			standard_types.Add("short", StandardType.Short);
			standard_types.Add("string", StandardType.String);
			standard_types.Add("uint", StandardType.Uint);
			standard_types.Add("ulong", StandardType.Ulong);
			standard_types.Add("ushort", StandardType.Ushort);
			standard_types.Add("object", StandardType.Object);

			integral_types = new Types();
			integral_types.Add("sbyte", StandardType.Sbyte);
			integral_types.Add("byte", StandardType.Byte);
			integral_types.Add("short", StandardType.Short);
			integral_types.Add("ushort", StandardType.Ushort);
			integral_types.Add("int", StandardType.Int);
			integral_types.Add("uint", StandardType.Uint);
			integral_types.Add("long", StandardType.Long);
			integral_types.Add("ulong", StandardType.Ulong);
			integral_types.Add("char", StandardType.Char);

			assign_operators = new PaxHashTable();
			relational_operators = new PaxHashTable();
			shift_operators = new PaxHashTable();
			additive_operators = new PaxHashTable();
			multiplicative_operators = new PaxHashTable();
			overloadable_unary_operators = new StringList(true);
			overloadable_binary_operators = new StringList(false);
		}

		internal override void Init(BaseScripter scripter, Module m)
		{
			base.Init(scripter, m);

			variable_initializers.Clear();
			static_variable_initializers.Clear();
			param_ids.Clear();
			param_type_ids.Clear();
			param_mods.Clear();
			local_variables.Clear();
			ACCESSOR_SWITCH = false;
			has_constructor = false;
			no_gen = false;
			valid_this_context = false;

			assign_operators.Clear();
			assign_operators.Add("=", code.OP_ASSIGN);
			assign_operators.Add("+=", code.OP_PLUS);
			assign_operators.Add("-=", code.OP_MINUS);
			assign_operators.Add("*=", code.OP_MULT);
			assign_operators.Add("/=", code.OP_DIV);
			assign_operators.Add("%=", code.OP_MOD);
			assign_operators.Add("&=", code.OP_BITWISE_AND);
			assign_operators.Add("|=", code.OP_BITWISE_OR);
			assign_operators.Add("^=", code.OP_BITWISE_XOR);
			assign_operators.Add("<<=",code.OP_LEFT_SHIFT);
			assign_operators.Add(">>=",code.OP_RIGHT_SHIFT);

			relational_operators.Clear();
			relational_operators.Add(">", code.OP_GT);
			relational_operators.Add("<", code.OP_LT);
			relational_operators.Add(">=", code.OP_GE);
			relational_operators.Add("<=", code.OP_LE);
			relational_operators.Add("is", code.OP_IS);
			relational_operators.Add("as", code.OP_AS);

			shift_operators.Clear();
			shift_operators.Add("<<", code.OP_LEFT_SHIFT);
			shift_operators.Add(">>", code.OP_RIGHT_SHIFT);

			additive_operators.Clear();
			additive_operators.Add("+", code.OP_PLUS);
			additive_operators.Add("-", code.OP_MINUS);

			multiplicative_operators.Clear();
			multiplicative_operators.Add("*", code.OP_MULT);
			multiplicative_operators.Add("/", code.OP_DIV);
			multiplicative_operators.Add("%", code.OP_MOD);

			overloadable_unary_operators.Clear();
			overloadable_unary_operators.AddObject("+", code.OP_UNARY_PLUS);
			overloadable_unary_operators.AddObject("-", code.OP_UNARY_MINUS);
			overloadable_unary_operators.AddObject("!", code.OP_NOT);
			overloadable_unary_operators.AddObject("~", code.OP_COMPLEMENT);
			overloadable_unary_operators.AddObject("++", code.OP_INC);
			overloadable_unary_operators.AddObject("--", code.OP_DEC);
			overloadable_unary_operators.AddObject("true", code.OP_TRUE);
			overloadable_unary_operators.AddObject("false", code.OP_TRUE);

			overloadable_binary_operators.Clear();
			overloadable_binary_operators.AddObject("+", code.OP_PLUS);
			overloadable_binary_operators.AddObject("-", code.OP_MINUS);
			overloadable_binary_operators.AddObject("*", code.OP_MULT);
			overloadable_binary_operators.AddObject("/", code.OP_DIV);
			overloadable_binary_operators.AddObject("%", code.OP_MOD);
			overloadable_binary_operators.AddObject("&", code.OP_BITWISE_AND);
			overloadable_binary_operators.AddObject("|", code.OP_BITWISE_OR);
			overloadable_binary_operators.AddObject("^", code.OP_BITWISE_XOR);
			overloadable_binary_operators.AddObject("<<", code.OP_LEFT_SHIFT);
			overloadable_binary_operators.AddObject(">>", code.OP_RIGHT_SHIFT);
			overloadable_binary_operators.AddObject("==", code.OP_EQ);
			overloadable_binary_operators.AddObject("!=", code.OP_NE);
			overloadable_binary_operators.AddObject(">", code.OP_GT);
			overloadable_binary_operators.AddObject("<", code.OP_LT);
			overloadable_binary_operators.AddObject(">=", code.OP_GE);
			overloadable_binary_operators.AddObject("<=", code.OP_LE);
		}

		/// <summary>
		/// Returns 'true', if s is a keyword.
		/// </summary>
		public override bool IsKeyword(string s)
		{
			if (base.IsKeyword(s))
				return true;
			else
			{
				if (ACCESSOR_SWITCH)
					return (s == "get") || (s == "set") ||
						   (s == "add") || (s == "remove");
				else
					return false;
			}
		}

		/// <summary>
		/// Matches string s with current token.
		/// If - success, reads new token. Otherwise - generates error.
		/// </summary>
		public override void Match(string s)
		{
			if (s != curr_token.Text)
			{
				if (s == ";")
					RaiseError(true, Errors.CS1002);
				else if (s == ")")
					RaiseError(true, Errors.CS1026);
				else if (s == "}")
					RaiseError(true, Errors.CS1513);
				else if (s == "{")
					RaiseError(true, Errors.CS1514);
				else if (s == "in")
					RaiseError(true, Errors.CS1515);
				else
					RaiseErrorEx(true, Errors.CS1003, s);
			}
			Call_SCANNER();
		}

		/// <summary>
		/// Matches char c with current token.
		/// If - success, reads new token. Otherwise - generates error.
		/// </summary>
		public override void Match(char c)
		{
			if (c != curr_token.Char)
				Match(c.ToString());
			Call_SCANNER();
		}

		/// <summary>
		/// Reads new token.
		/// </summary>
		public override void Call_SCANNER()
		{
			base.Call_SCANNER();
			if (curr_token.tokenClass == TokenClass.Keyword)
			{
				int i = standard_types.GetTypeId(curr_token.Text);
				if (i >= 0)
				{
					curr_token.id = i;
					curr_token.tokenClass = TokenClass.Identifier;
				}
			}
		}

		/// <summary>
		/// Emits new p-code instruction.
		/// </summary>
		public override void Gen(int op, int arg1, int arg2, int res)
		{
			if (no_gen)
			{
				if (op == code.OP_SEPARATOR)
				{
					base.Gen(op, arg1, arg2, res);
					SetUpcase(false);
				}
			}
			else
			{
				base.Gen(op, arg1, arg2, res);
				SetUpcase(false);
			}
		}

		/// <summary>
		/// Returns 'true', if modifier list contains an access modifiers.
		/// </summary>
        internal virtual bool HasAccessModifier(ModifierList ml)
		{
			return (ml.HasModifier(Modifier.Public));

			//		(ml.IndexOf((int).Modifier.Private) ||
			 //		(ml.IndexOf((int).Modifier.Protected));
		}

		/// <summary>
		/// Parses 'null' literal.
		/// </summary>
        internal virtual int Parse_NullLiteral()
		{
			Match("null");
			return NULL_id;
		}

		/// <summary>
		/// Parses non-array type expression.
		/// </summary>
		internal virtual int Parse_NonArrayType()
		{
			int id;
			id = Parse_Ident();
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

				string s = Parse_RankSpecifiers();
				SetName(array_type_id, GetName(result) + s);
				Gen(code.OP_EVAL_TYPE, element_type_id, 0, array_type_id);

				return array_type_id;
			}
			else
				return result;
		}

		/// <summary>
		/// Returns 'true', if s is name of standard type.
		/// </summary>
		public virtual bool IsStandardType(string s)
		{
			return standard_types.GetTypeId(s) != -1;
		}

		/// <summary>
		/// Parses integral type.
		/// </summary>
        internal virtual int Parse_IntegralType()
		{
			int id = integral_types.GetTypeId(curr_token.Text);
			if (id == -1)
				RaiseError(false, Errors.CS1008);
			Parse_Ident();
			return id;
		}

		/// <summary>
		/// Parses Multiplicative expression.
		/// </summary>
		internal virtual int Parse_MultiplicativeExpr(int result)
		{
			result = Parse_UnaryExpr(result);

			int op;
			object v;

			v = multiplicative_operators[curr_token.Text];
			while (v != null)
			{
				op = (int) v;
				Call_SCANNER();
				result = BinOp(op, result, Parse_UnaryExpr(0));
				v = multiplicative_operators[curr_token.Text];
			}
			return result;
		}

		/// <summary>
		/// Parses Additive expression.
		/// </summary>
		internal virtual int Parse_AdditiveExpr(int result)
		{
			result = Parse_MultiplicativeExpr(result);

			int op;
			object v;

			v = additive_operators[curr_token.Text];
			while (v != null)
			{
				op = (int) v;
				Call_SCANNER();
				result = BinOp(op, result, Parse_MultiplicativeExpr(0));
				v = additive_operators[curr_token.Text];
			}
			return result;
		}

		/// <summary>
		/// Parses Shift expression.
		/// </summary>
		internal virtual int Parse_ShiftExpr(int result)
		{
			result = Parse_AdditiveExpr(result);

			int op;
			object v;

			v = shift_operators[curr_token.Text];
			while (v != null)
			{
				op = (int) v;
				Call_SCANNER();
				result = BinOp(op, result, Parse_AdditiveExpr(0));
				v = shift_operators[curr_token.Text];
			}
			return result;
		}

		/// <summary>
		/// Parses Relational expression.
		/// </summary>
		internal virtual int Parse_RelationalExpr(int result)
		{
			result = Parse_ShiftExpr(result);

			int op;
			object v;

			v = relational_operators[curr_token.Text];
			while (v != null)
			{
				op = (int) v;
				Call_SCANNER();
				result = BinOp(op, result, Parse_ShiftExpr(0));
				v = relational_operators[curr_token.Text];
			}
			return result;
		}

		/// <summary>
		/// Parses Equality expression.
		/// </summary>
		internal virtual int Parse_EqualityExpr(int result)
		{
			result = Parse_RelationalExpr(result);

			while (IsCurrText("==")||IsCurrText("!="))
			{
				int op;
				if (IsCurrText("=="))
					op = code.OP_EQ;
				 else
					op = code.OP_NE;

				Call_SCANNER();
				result = BinOp(op, result, Parse_RelationalExpr(0));
			}
			return result;
		}

		/// <summary>
		/// Parses AND expression.
		/// </summary>
		internal virtual int Parse_ANDExpr(int result)
		{
			result = Parse_EqualityExpr(result);

			while (IsCurrText("&"))
			{
				Call_SCANNER();
				result = BinOp(code.OP_BITWISE_AND, result,
					Parse_EqualityExpr(0));
			}
			return result;
		}

		/// <summary>
		/// Parses Exclusive OR expression.
		/// </summary>
		internal virtual int Parse_ExclusiveORExpr(int result)
		{
			result = Parse_ANDExpr(result);

			while (IsCurrText("^"))
			{
				Call_SCANNER();
				result = BinOp(code.OP_BITWISE_XOR, result,
					Parse_ANDExpr(0));
			}
			return result;
		}

		/// <summary>
		/// Parses Inclusive OR expression.
		/// </summary>
		internal virtual int Parse_InclusiveORExpr(int result)
		{
			result = Parse_ExclusiveORExpr(result);

			while (IsCurrText("|"))
			{
				Call_SCANNER();
				result = BinOp(code.OP_BITWISE_OR, result,
					Parse_ExclusiveORExpr(0));
			}
			return result;
		}

		/// <summary>
		/// Parses Conditional AND expression.
		/// </summary>
		internal virtual int Parse_ConditionalANDExpr(int result)
		{
			result = Parse_InclusiveORExpr(result);

			while (IsCurrText("&&"))
			{
				int id = result;
				int lf = NewLabel();
				result = NewVar();

				Gen(code.OP_ASSIGN, result, id, result);
				Gen(code.OP_GO_FALSE, lf, result, 0);
				Call_SCANNER();
				Gen(code.OP_ASSIGN, result, Parse_InclusiveORExpr(0), result);
				SetLabelHere(lf);
			}
			return result;
		}

		/// <summary>
		/// Parses Conditional OR expression.
		/// </summary>
		internal virtual int Parse_ConditionalORExpr(int result)
		{
			result = Parse_ConditionalANDExpr(result);

			while (IsCurrText("||"))
			{
				int id = result;
				int lf = NewLabel();
				result = NewVar();

				Gen(code.OP_ASSIGN, result, id, result);
				Gen(code.OP_GO_TRUE, lf, result, 0);
				Call_SCANNER();
				Gen(code.OP_ASSIGN, result, Parse_InclusiveORExpr(0), result);
				SetLabelHere(lf);
			}
			return result;
		}

		/// <summary>
		/// Parses expression.
		/// </summary>
		public override int Parse_Expression()
		{
			int result = Parse_UnaryExpr(0);

			object v = assign_operators[curr_token.Text];
			if (v != null)
			{
				int op = (int) v;
				Call_SCANNER();
				if (op == code.OP_ASSIGN)
					Gen(op, result, Parse_Expression(), result);
				else
				{
					int temp = NewVar();
					Gen(op, result, Parse_Expression(), temp);
					Gen(code.OP_ASSIGN, result, temp, result);
				}
			}
			else
			{
				if (IsCurrText("?"))
				{
					Match("?");
					int lg = NewLabel();
					int lf = NewLabel();
					Gen(code.OP_GO_FALSE, lf, result, 0);
					result = NewVar();
					int id1 = Parse_Expression();
					Gen(code.OP_ASSIGN, result, id1, result);
					Match(':');
					Gen(code.OP_GO, lg, 0, 0);
					SetLabelHere(lf);
					int id2 = Parse_Expression();
					Gen(code.OP_ASSIGN_COND_TYPE, id1, id2, result);
					Gen(code.OP_ASSIGN, result, id2, result);
					SetLabelHere(lg);
				}
				else
					result = Parse_ConditionalORExpr(result);

			}
			return result;
		}

		/// <summary>
		/// Parses constant expression.
		/// </summary>
        internal virtual int Parse_ConstantExpression()
		{
			return Parse_Expression();
		}

		/// <summary>
		/// Parses Unary expression.
		/// </summary>
        internal virtual int Parse_UnaryExpr(int result)
		{
			if (result != 0)
				return result;

			if (IsCurrText('+'))
			{
				Call_SCANNER();
				result = UnaryOp(code.OP_UNARY_PLUS, Parse_UnaryExpr(0));
			}
			else if (IsCurrText('-'))
			{
				Call_SCANNER();
				result = UnaryOp(code.OP_UNARY_MINUS, Parse_UnaryExpr(0));
			}
			else if (IsCurrText("++"))
			{
				Call_SCANNER();
				result = Parse_UnaryExpr(0);
				int temp = NewVar();
				Gen(code.OP_INC, result, 0, temp);
				Gen(code.OP_ASSIGN, result, temp, result);
			}
			else if (IsCurrText("--"))
			{
				Call_SCANNER();
				result = Parse_UnaryExpr(0);
				int temp = NewVar();
				Gen(code.OP_DEC, result, 0, temp);
				Gen(code.OP_ASSIGN, result, temp, result);
			}
			else if (IsCurrText('!'))
			{
				Call_SCANNER();
				result = UnaryOp(code.OP_NOT, Parse_UnaryExpr(0));
			}
			else if (IsCurrText('~'))
			{
				Call_SCANNER();
				result = UnaryOp(code.OP_COMPLEMENT, Parse_UnaryExpr(0));
			}
			else if (IsCurrText('*'))
			{
				RaiseError(true, "Not implemented");
				Call_SCANNER();
				result = UnaryOp(0, Parse_UnaryExpr(0));
			}
			else
			{
				bool is_cast = IsCurrText('(');

				if (is_cast)
				{

					int k = ReadToken();
					bool b1 = IsIdentOrStandardType();
					k += ReadToken();
					for (;;)
					{
						if (!IsCurrText('.'))
							break;
						k += ReadToken(); // scrip '.'
						k += ReadToken(); // read next token
					}

					if (IsCurrText(')'))
						k += ReadToken();
					else
					{
						bool b2 = IsIdentOrStandardType() || IsCurrText('[');
						if (IsCurrText('['))
						{
							k += ReadToken();
							if (!(IsCurrText(']') || (IsCurrText(','))))
								b2 = false;
						}
						is_cast = b1 && b2;
						if (IsCurrText(')'))
							k += ReadToken();
						goto backup;
					}
					if (is_cast)
					{
						is_cast = IsCurrText('(') ||
								  IsIdentifier() ||
								  IsConstant();
					}
					backup:
					Backup_SCANNER(k);
				}
				else
					is_cast = false;

				if (is_cast)
				{
					Match('(');
					int type_id = Parse_Type();
					Match(')');
					result = NewVar();
					Gen(code.OP_CAST, type_id, Parse_UnaryExpr(0), result);
				}
				else
					result = Parse_PrimaryExpr();
			}
			return result;
		}

		/// <summary>
		/// Parses Primary expression.
		/// </summary>
		internal virtual int Parse_PrimaryExpr()
		{
			int result;

			if (IsCurrText('(')) // parenthesized expression
			{
				Match('(');
				result = Parse_Expression();
				Match(')');
			}
			else if (IsCurrText("checked"))
			{
				Match("checked");
				Match('(');
				Gen(code.OP_CHECKED, TRUE_id, 0, 0);
				result = Parse_Expression();
				Gen(code.OP_RESTORE_CHECKED_STATE, 0, 0, 0);
				Match(')');
			}
			else if (IsCurrText("unchecked"))
			{
				Match("unchecked");
				Match('(');
				Gen(code.OP_CHECKED, FALSE_id, 0, 0);
				result = Parse_Expression();
				Gen(code.OP_RESTORE_CHECKED_STATE, 0, 0, 0);
				Match(')');
			}
			else if (IsCurrText("new")) // object-creation-expression
			{
				Match("new");

				int class_id = Parse_NonArrayType();
				if (IsCurrText('['))
				{
					bool is_array_type = true;

					string s = "";
					for (;;)
					{
						// rank-specifier
						Match('[');

						if (!(IsCurrText(']') || IsCurrText(',')))
						{
							is_array_type = false;
							break;
						}

						s += "[";
						if (!IsCurrText(']'))
						{
							// dim-separators
							for (;;)
							{
								Match(',');
								s += ",";
								if (!IsCurrText(','))
									break;
							}
						}
						Match(']');
						s += "]";

						if (!IsCurrText('['))
							break;
					}

					if (is_array_type)
					{
						int array_class_id = NewVar();
						SetName(array_class_id, GetName(class_id) + s);
						Gen(code.OP_EVAL_TYPE, 0, 0, array_class_id);
						result = Parse_ArrayInitializer(array_class_id);
					}
					else
					{
						int array_class_id = NewVar();
						Gen(code.OP_EVAL_TYPE, 0, 0, array_class_id);

						result = NewVar();
						Gen(code.OP_CREATE_OBJECT, array_class_id, 0, result);
						Gen(code.OP_BEGIN_CALL, array_class_id, 0, 0);
						int index_count = 0;
						s = "[";

						for (;;)
						{
							index_count ++;
							Gen(code.OP_PUSH, Parse_Expression(), 0, array_class_id);
							if (!CondMatch(','))
								break;
							else
								s += ",";
						}
						Gen(code.OP_PUSH, result, 0, 0);
						Gen(code.OP_CALL, array_class_id, index_count, 0);
						Match(']');
						s += "]";

						if (IsCurrText('['))
							s += Parse_RankSpecifiers();

						SetName(array_class_id, GetName(class_id) + s);

						if (IsCurrText('{'))
							result = Parse_ArrayInitializer(array_class_id);
					}
				}
				else
				{
					if (!IsCurrText('('))
						// 'new' expression requires () or [] after type
						RaiseError(true, Errors.CS1526);

					result = NewVar();
					Gen(code.OP_CREATE_OBJECT, class_id, 0, result);
					Match('(');
					Gen(code.OP_CALL, class_id, Parse_ArgumentList(")", class_id, result), 0);
					Match(')');
				}
			}
			else if (IsCurrText("this")) // this access
			{
				Match("this");
				result = CurrThisID;
				if (GetName(result)!= "this")
					// keyword this is not valid
					RaiseError(false, Errors.CS0026);
				if (!valid_this_context)
					RaiseError(false, Errors.CS0027);
				if (IsCurrText('.'))
				{
					REF_SWITCH = true;
					Match('.');
					int object_id = result;
					result = Parse_Ident();
					Gen(code.OP_CREATE_REFERENCE, object_id, 0, result);
				}
			}
			else if (IsCurrText("base")) // base access
			{
				Match("base");
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
						Gen(code.OP_CALL_BASE, base_object_id, Parse_ArgumentList(")", base_object_id, base_object_id), result);
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
			else if (IsCurrText("null"))
				result = Parse_NullLiteral();
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
			else if (IsCurrText("typeof"))
			{
				Match("typeof");
				result = NewVar();
				Match('(');
				Gen(code.OP_TYPEOF, Parse_Type(), 0, result);
				Match(')');
			}
			else
				result = Parse_Ident();

			for (;;)
			{
				if (IsCurrText('(')) // invocation
				{
					int object_id = result;
					result = NewVar();

					Match('(');
					Gen(code.OP_CALL, object_id, Parse_ArgumentList(")", object_id, object_id), result);
					Match(')');
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

			if (IsCurrText("++")) // post increment expression
			{
				Match("++");
				int temp = NewVar();
				Gen(code.OP_ASSIGN, temp, result, temp);
				int r = NewVar();
				Gen(code.OP_INC, result, 0, r);
				Gen(code.OP_ASSIGN, result, r, result);
				result = temp;
			}
			else if (IsCurrText("--")) // post decrement expression
			{
				Match("--");
				int temp = NewVar();
				Gen(code.OP_ASSIGN, temp, result, temp);
				int r = NewVar();
				Gen(code.OP_DEC, result, 0, r);
				Gen(code.OP_ASSIGN, result, r, result);
				result = temp;
			}

			return result;
		}

		/// <summary>
		/// Parses argument list.
		/// </summary>
        internal virtual int Parse_ArgumentList(string CloseBracket, int sub_id, int object_id)
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

				if (IsCurrText("ref"))
				{
					Match("ref");
					mod = ParamMod.RetVal;
				}
				else if (IsCurrText("out"))
				{
					Match("out");
					mod = ParamMod.Out;
				}

				int actual_id = Parse_Expression();
				if ((mod == ParamMod.RetVal) || (mod == ParamMod.Out))
				{
					Gen(code.OP_SET_REF_TYPE, actual_id, 0, 0);
					Gen(code.OP_PUSH, actual_id, (int) mod, sub_id);
				}
				else
					Gen(code.OP_PUSH, actual_id, (int) mod, sub_id);
				if (!CondMatch(',')) break;
			}
			Gen(code.OP_PUSH, object_id, 0, 0);
			return result;
		}

		/// <summary>
		/// Parses formal parameter list.
		/// </summary>
        internal virtual int Parse_FormalParameterList(int sub_id, bool isIndexer)
		{
			param_ids.Clear();
			param_type_ids.Clear();
			param_mods.Clear();

			bool is_params = false;

			int result = 0;
			for (;;)
			{
				if (is_params)
					// A params parameter must be the last parameter in a formal parameter list.
					RaiseError(false, Errors.CS0231);

				result ++;

				if (IsCurrText('['))
					Parse_Attributes();

				ParamMod mod = ParamMod.None;

				int type_id;
				if (IsCurrText("ref"))
				{
					if (isIndexer)
						// Indexers can't have ref or out parameters
						RaiseError(false, Errors.CS0631);
					Match("ref");
					mod = ParamMod.RetVal;
					type_id = GenTypeRef(Parse_Type());
				}
				else if (IsCurrText("out"))
				{
					if (isIndexer)
						// Indexers can't have ref or out parameters
						RaiseError(false, Errors.CS0631);
					Match("out");
					mod = ParamMod.Out;
					type_id = GenTypeRef(Parse_Type());
				}
				else if (IsCurrText("params"))
				{
					Match("params");
					if ((IsCurrText("ref") || IsCurrText("out")))
						// The params parameter cannot be declared as ref or out
						RaiseError(false, Errors.CS1611);

					type_id = Parse_Type();
					string type_name = GetName(type_id);
					if (PaxSystem.GetRank(type_name) != 1)
						// The params parameter must be a single dimensional array
						RaiseError(false, Errors.CS0225);
					is_params = true;
				}
				else
					type_id = Parse_Type();

				int param_id = Parse_Ident();

				if (IsCurrText('='))
				{
					// Default parameter specifiers are not permitted
					RaiseError(true, Errors.CS0241);
				}
				else if (IsCurrText('['))
				{
					// Array type specifier, [], must appear before parameter name
					RaiseError(true, Errors.CS1552);
				}

				if (!isIndexer)
				{
					Gen(code.OP_ASSIGN_TYPE, param_id, type_id, 0);
					if (is_params)
						Gen(code.OP_ADD_PARAMS, sub_id, param_id, 0);
					else
						Gen(code.OP_ADD_PARAM, sub_id, param_id, (int) mod);
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
		/// Returns 'true', if current token is identifier or standard type.
		/// </summary>
        internal virtual bool IsIdentOrStandardType()
		{
			string s = curr_token.Text;
			if (curr_token.tokenClass == TokenClass.Identifier ||
				curr_token.tokenClass == TokenClass.Keyword)
			{
				if (IsKeyword(s))
				{
					if (IsStandardType(s))
						return true;
					else
						return false;
				}
				else
					return true;
			}
			return false;
		}

		/// <summary>
		/// Parses statement.
		/// </summary>
        internal virtual void Parse_Stmt()
		{
			if (IsCurrText("const"))
			{
				Parse_LocalConstantDeclaration();
				return;
			}

			bool b1 = IsIdentOrStandardType();

			int k = ReadToken();
			if (IsCurrText(':'))
			{
				Backup_SCANNER(k);
				int l = Parse_NewLabel();
				SetLabelHere(l);
				Match(':');
				Parse_Stmt();
				return;
			}

			for (;;)
			{
				if (!IsCurrText('.'))
					break;
				k += ReadToken(); // scrip '.'
				k += ReadToken(); // read next token
			}

			bool b2 = IsIdentOrStandardType() || IsCurrText('[');

			if (IsCurrText('['))
			{
				k += ReadToken();
				if (!(IsCurrText(']') || (IsCurrText(','))))
					b2 = false;
			}

			if (b1 && b2)
			{
				Backup_SCANNER(k);
				Parse_DeclarationStmt();
			}
			else
			{
				Backup_SCANNER(k);
				Parse_EmbeddedStmt();
			}
		}

		/// <summary>
		/// Parses Declaration statement.
		/// </summary>
        internal virtual void Parse_DeclarationStmt()
		{
			if (IsCurrText("const"))
				Parse_LocalConstantDeclaration();
			else
				Parse_LocalVariableDeclaration();
			Match(';');
		}

		/// <summary>
		/// Parses Local Constant Declaration statement.
		/// </summary>
        internal virtual void Parse_LocalConstantDeclaration()
		{
			Match("const");
			DECLARE_SWITCH = true;
			DECLARATION_CHECK_SWITCH = true;
			int type_id = Parse_Type();
			// parse constant declarators
			for (;;)
			{
				// parse constant declarator
				int id = Parse_Ident();
				if (!IsCurrText('='))
				{
					// A const field requires a value to be provided
					RaiseError(true, Errors.CS0145);
				}

				Gen(code.OP_DECLARE_LOCAL_VARIABLE, id, CurrSubId, 0);
				Gen(code.OP_ASSIGN_TYPE, id, type_id, 0);
				DECLARE_SWITCH = false;
				Match('=');
				// parse constant initializer
				if (IsCurrText('{'))
					Gen(code.OP_ASSIGN, id, Parse_ArrayInitializer(type_id), id);
				else
					Gen(code.OP_ASSIGN, id, Parse_Expression(), id);
				DECLARE_SWITCH = true;

				if (!CondMatch(',')) break;
			}
			DECLARE_SWITCH = false;
			DECLARATION_CHECK_SWITCH = false;
		}

		/// <summary>
		/// Parses Local Variable Declaration statement.
		/// </summary>
        internal virtual void Parse_LocalVariableDeclaration()
		{
			local_variables.Clear();

			DECLARE_SWITCH = true;
			DECLARATION_CHECK_SWITCH = true;
			int type_id = Parse_Type();
			// parse variable declarators
			for (;;)
			{
				// parse variable declarator
				int id = Parse_Ident();
				Gen(code.OP_DECLARE_LOCAL_VARIABLE, id, CurrSubId, 0);

				local_variables.Add(id);

				Gen(code.OP_ASSIGN_TYPE, id, type_id, 0);
				if (IsCurrText('='))
				{
					DECLARE_SWITCH = false;
					Match('=');
					// parse local variable initializer
					if (IsCurrText('{'))
						Gen(code.OP_ASSIGN, id, Parse_ArrayInitializer(type_id), id);
					else
						Gen(code.OP_ASSIGN, id, Parse_Expression(), id);
					DECLARE_SWITCH = true;
				}
				else
					Gen(code.OP_CHECK_STRUCT_CONSTRUCTOR, type_id, 0, id);

				if (!CondMatch(',')) break;
			}
			DECLARE_SWITCH = false;
			DECLARATION_CHECK_SWITCH = false;
		}

		/// <summary>
		/// Parses Embedded statement.
		/// </summary>
		internal virtual void Parse_EmbeddedStmt()
		{
			if (IsCurrText('{'))
				Parse_Block();
			else if (IsCurrText("if"))
				Parse_IfStmt();
			else if (IsCurrText("switch"))
				Parse_SwitchStmt();
			else if (IsCurrText("do"))
				Parse_DoStmt();
			else if (IsCurrText("while"))
				Parse_WhileStmt();
			else if (IsCurrText("for"))
				Parse_ForStmt();
			else if (IsCurrText("foreach"))
				Parse_ForeachStmt();
			else if (IsCurrText("goto"))
				Parse_GotoStmt();
			else if (IsCurrText("break"))
				Parse_BreakStmt();
			else if (IsCurrText("continue"))
				Parse_ContinueStmt();
			else if (IsCurrText("return"))
				Parse_ReturnStmt();
			else if (IsCurrText("throw"))
				Parse_ThrowStmt();
			else if (IsCurrText("try"))
				Parse_TryStmt();
			else if (IsCurrText("checked"))
				Parse_CheckedStmt();
			else if (IsCurrText("lock"))
				Parse_LockStmt();
			else if (IsCurrText("using"))
				Parse_UsingStmt();
			else if (IsCurrText("unchecked"))
				Parse_UncheckedStmt();
			else if (IsCurrText("print"))
				Parse_PrintStmt();
			else if (IsCurrText("println"))
				Parse_PrintlnStmt();
			else if (IsCurrText(';'))
				Parse_EmptyStmt();
			else
				Parse_ExpressionStmt();
		}

		/// <summary>
		/// Parses Block statement.
		/// </summary>
        internal virtual void Parse_Block()
		{
			BeginBlock();
			DECLARE_SWITCH = false;
			Match('{');
			if (!IsCurrText('}'))
				Parse_StatementList();
			EndBlock();
			Match('}');
		}

		/// <summary>
		/// Parses method body (block).
		/// </summary>
        internal virtual void Parse_MethodBlock()
		{
			Parse_Block();
			if (IsCurrText(';'))
				// Semicolon after method or accessor block is not valid
				RaiseError(true, Errors.CS1597);
		}

		/// <summary>
		/// Parses Checked statement.
		/// </summary>
        internal virtual void Parse_CheckedStmt()
		{
			Match("checked");
			Gen(code.OP_CHECKED, TRUE_id, 0, 0);
			Parse_Block();
			Gen(code.OP_RESTORE_CHECKED_STATE, 0, 0, 0);
		}

		/// <summary>
		/// Parses Unchecked statement.
		/// </summary>
        internal virtual void Parse_UncheckedStmt()
		{
			Match("unchecked");
			Gen(code.OP_CHECKED, FALSE_id, 0, 0);
			Parse_Block();
			Gen(code.OP_RESTORE_CHECKED_STATE, 0, 0, 0);
		}

		/// <summary>
		/// Parses Lock statement.
		/// </summary>
        internal virtual void Parse_LockStmt()
		{
			Match("lock");
			Match('(');
			int id = Parse_Expression();
			Match(')');

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

		/// <summary>
		/// Parses Resource Acquisition.
		/// </summary>
        internal virtual void Parse_ResourceAcquisition()
		{
			bool b1 = IsIdentOrStandardType();

			int k = ReadToken();
			for (;;)
			{
				if (!IsCurrText('.'))
					break;
				k += ReadToken(); // scrip '.'
				k += ReadToken(); // read next token
			}

			bool b2 = IsIdentOrStandardType() || IsCurrText('[');

			if (IsCurrText('['))
			{
				k += ReadToken();
				if (!(IsCurrText(']') || (IsCurrText(','))))
					b2 = false;
			}

			if (b1 && b2)
			{
				Backup_SCANNER(k);
				Parse_LocalVariableDeclaration();
			}
			else
			{
				Backup_SCANNER(k);
				int id = Parse_Expression();

				local_variables.Clear();
				local_variables.Add(id);
			}
		}

		/// <summary>
		/// Parses Using Statement.
		/// </summary>
        internal virtual void Parse_UsingStmt()
		{
			Match("using");
			BeginBlock();

			Match('(');
			Parse_ResourceAcquisition();
			Match(')');

			IntegerList resources = local_variables.Clone();

			// try-block

			int l_try = NewLabel();
			Gen(code.OP_TRY_ON, l_try, 0, 0);

			Parse_EmbeddedStmt();

			Gen(code.OP_FINALLY, 0, 0, 0);

			foreach (int temp_id in resources)
				Gen(code.OP_DISPOSE, temp_id, 0, 0);

			Gen(code.OP_EXIT_ON_ERROR, 0, 0, 0);
			Gen(code.OP_GOTO_CONTINUE, 0, 0, 0);

			SetLabelHere(l_try);
			Gen(code.OP_TRY_OFF, 0, 0, 0);

			EndBlock();
		}

		/// <summary>
		/// Parses Empty Statement.
		/// </summary>
        internal virtual void Parse_EmptyStmt()
		{
			Match(';');
		}

		/// <summary>
		/// Parses Throw Statement.
		/// </summary>
        internal virtual void Parse_ThrowStmt()
		{
			Match("throw");
			if (!IsCurrText(';'))
				Gen(code.OP_THROW, Parse_Expression(), 0, 0);
			else
				Gen(code.OP_THROW, 0, 0, 0);
			Match(';');
		}

		/// <summary>
		/// Parses Try Statement.
		/// </summary>
        internal virtual void Parse_TryStmt()
		{
			Match("try");

			int l_try = NewLabel();
			Gen(code.OP_TRY_ON, l_try, 0, 0);

			bool has_empty_block = false;

			Parse_Block();
			int l = NewLabel();
			Gen(code.OP_GO, l, 0, 0);
			if (IsCurrText("catch"))
			{
				while (IsCurrText("catch"))
				{
					int id, class_id;
					Match("catch");
					if (IsCurrText('('))
					{
						if (has_empty_block)
							// Try statement already has an empty catch block
							RaiseError(true, Errors.CS1017);

						DECLARE_SWITCH = true;
						// specific catch clause
						Match('(');
						class_id = Parse_Type();
						if (!IsCurrText(')'))
						{
							id = Parse_Ident();
						}
						else
						{
							id = NewVar();
						}
						Gen(code.OP_DECLARE_LOCAL_SIMPLE, id, CurrSubId, 0);
						Gen(code.OP_ASSIGN_TYPE, id, class_id, 0);
						DECLARE_SWITCH = false;
						Match(')');
					}
					else
					{
						has_empty_block = true;
						id = NewVar();
					}
					Gen(code.OP_CATCH, id, 0, 0);
					Parse_Block();
					Gen(code.OP_DISCARD_ERROR, 0, 0, 0);

					SetLabelHere(l);
					if (IsCurrText("finally"))
					{
						Match("finally");
						Gen(code.OP_FINALLY, 0, 0, 0);
						Parse_Block();
						Gen(code.OP_EXIT_ON_ERROR, 0, 0, 0);
						Gen(code.OP_GOTO_CONTINUE, 0, 0, 0);
					}
				}
			}
			else if (IsCurrText("finally"))
			{
				SetLabelHere(l);
				Match("finally");
				Gen(code.OP_FINALLY, 0, 0, 0);
				Parse_Block();
				Gen(code.OP_EXIT_ON_ERROR, 0, 0, 0);
				Gen(code.OP_GOTO_CONTINUE, 0, 0, 0);
			}
			else
				// Expected catch or finally
				RaiseError(true, Errors.CS1524);

			SetLabelHere(l_try);
			Gen(code.OP_TRY_OFF, 0, 0, 0);
		}

		/// <summary>
		/// Parses If Statement.
		/// </summary>
        internal virtual void Parse_IfStmt()
		{
			int lf = NewLabel();
			Match("if");
			Match('(');
			Gen(code.OP_GO_FALSE, lf, Parse_Expression(), 0);
			Match(')');
			Parse_EmbeddedStmt();

			if (IsCurrText("else"))
			{
				int lg = NewLabel();
				Gen(code.OP_GO, lg, 0, 0);
				SetLabelHere(lf);
				Match("else");
				Parse_EmbeddedStmt();
				SetLabelHere(lg);
			}
			else
			{
				SetLabelHere(lf);
			}
		}

		/// <summary>
		/// Parses Switch Statement.
		/// </summary>
        internal virtual void Parse_SwitchStmt()
		{
			int lg = NewLabel();
			int l_default = NewLabel();

			IntegerList case_expr_ids = new IntegerList(true);
			IntegerList goto_case_expr_ids = new IntegerList(true);

			IntegerStack lt = new IntegerStack();
			int bool_id = NewVar();
			Gen(code.OP_ASSIGN, bool_id, TRUE_id, bool_id);
			BreakStack.Push(lg);

			Match("switch");
			BeginBlock();
			Match('(');
			int expr_id = Parse_Expression();
			Match(')');

			Match('{'); // parse switch block
			for (;;) // parse switch sections
			{
				for (;;) // parse switch labels
				{
					if (IsCurrText("case"))
					{
						Match("case");
						lt.Push(NewLabel());
						int case_expr_id = Parse_Expression();
						case_expr_ids.AddObject(case_expr_id, lt.Peek());
						Gen(code.OP_EQ, expr_id, case_expr_id, bool_id);
						Gen(code.OP_GO_TRUE, lt.Peek(), bool_id, 0);
					}
					else if (IsCurrText("default"))
					{
						Match("default");
						SetLabelHere(l_default);
						Gen(code.OP_ASSIGN, bool_id, TRUE_id, bool_id);
					}
					else
						break; //switch labels
					Match(':');
				}

				while (lt.Count > 0)
				{
					SetLabelHere(lt.Peek());
					lt.Pop();
				}

				int lf = NewLabel();
				Gen(code.OP_GO_FALSE, lf, bool_id, 0);
				// parse statement list
				for (;;)
				{
					if (IsCurrText("case"))
					  break;
					if (IsCurrText("default"))
					  break;
					if (IsCurrText('}'))
					  break;

					if (IsCurrText("goto"))
					{
						Match("goto");
						if (IsCurrText("case"))
						{
							Match("case");
							int id = Parse_Expression();
							Gen(code.OP_GOTO_START, 0, 0, 0);
							goto_case_expr_ids.AddObject(id, CodeCard);
							Match(';');
						}
						else if (IsCurrText("default"))
						{
							Match("default");
							Gen(code.OP_GOTO_START, l_default, 0, 0);
							Match(';');
						}
						else
						{
							int l = Parse_Ident();
							Gen(code.OP_GOTO_START, l, 0, 0);
							Match(';');
						}
					}
					else
						Parse_Stmt();
				}
				SetLabelHere(lf);

				if (IsCurrText('}'))
					break;
			}
			BreakStack.Pop();
			SetLabelHere(lg);
			EndBlock();
			Match('}');

			for (int i = 0; i < goto_case_expr_ids.Count; i++)
			{
				int id = goto_case_expr_ids[i];
				int n = (int) goto_case_expr_ids.Objects[i];
				object value = GetVal(id);
				bool found = false;
				for (int j=0; j<case_expr_ids.Count; j++)
				{
					int case_expr_id = case_expr_ids[j];
					int case_expr_l = (int) case_expr_ids.Objects[j];
					object case_expr_value = GetVal(case_expr_id);

					if (value.GetType() == case_expr_value.GetType())
					{
						if (value == case_expr_value)
						{
							found = true;
							GenAt(n, code.OP_GOTO_START, case_expr_l, 0, 0);
							break;
						}
					}
				}
				if (!found)
				{
					CodeCard = n;
					// No such label 'label' within the scope of the goto statement
					RaiseErrorEx(true, Errors.CS0159, value.ToString());
				}
			}
		}

		/// <summary>
		/// Parses Do Statement.
		/// </summary>
        internal virtual void Parse_DoStmt()
		{
			int lt = NewLabel();
			int lg = NewLabel();
			SetLabelHere(lt);
			Match("do");
			BreakStack.Push(lg);
			ContinueStack.Push(lt);
			Parse_EmbeddedStmt();
			BreakStack.Pop();
			ContinueStack.Pop();
			Match("while");
			Match('(');
			Gen(code.OP_GO_TRUE, lt, Parse_Expression(), 0);
			SetLabelHere(lg);
			Match(')');
			Match(';');
		}

		/// <summary>
		/// Parses While Statement.
		/// </summary>
        internal virtual void Parse_WhileStmt()
		{
			int lf = NewLabel();
			int lg = NewLabel();
			SetLabelHere(lg);
			Match("while");
			Match('(');
			Gen(code.OP_GO_FALSE, lf, Parse_Expression(), 0);
			Match(')');
			BreakStack.Push(lf);
			ContinueStack.Push(lg);
			Parse_EmbeddedStmt();
			BreakStack.Pop();
			ContinueStack.Pop();
			Gen(code.OP_GO, lg, 0, 0);
			SetLabelHere(lf);
		}


		/// <summary>
		/// Parses Foreach Statement.
		/// </summary>
        internal virtual void Parse_ForeachStmt()
		{
			Match("foreach");
			BeginBlock();
			Match('(');
			DECLARE_SWITCH = true;
			DECLARATION_CHECK_SWITCH = true;
			int type_id = Parse_Type();

			if (curr_token.tokenClass != TokenClass.Identifier)
			{
				// Type and identifier are both required in a foreach statement
				RaiseError(true, Errors.CS0230);
			}

			int element_id = Parse_Ident();

			DECLARE_SWITCH = false;
			DECLARATION_CHECK_SWITCH = false;
			Gen(code.OP_ASSIGN_TYPE, element_id, type_id, 0);

			Match("in");
			int collection_id = Parse_Expression();
			Gen(code.OP_DECLARE_LOCAL_SIMPLE, element_id, collection_id, 0);
			Match(')');

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

			BreakStack.Push(lf);
			ContinueStack.Push(lg);

			// element = enumerator.Current;
			int enumerator_current_id = NewRef("get_Current");
			Gen(code.OP_CREATE_REFERENCE, enumerator_id, 0, enumerator_current_id);
			Gen(code.OP_BEGIN_CALL, enumerator_current_id, 0, 0);
			Gen(code.OP_PUSH, enumerator_id, 0, 0);
			Gen(code.OP_CALL, enumerator_current_id, 0, element_id);

			// statement
			Parse_EmbeddedStmt();

			BreakStack.Pop();
			ContinueStack.Pop();
			Gen(code.OP_GO, lg, 0, 0);
			SetLabelHere(lf);

			EndBlock();
		}

		/// <summary>
		/// Parses For Statement.
		/// </summary>
        internal virtual void Parse_ForStmt()
		{
			Match("for");
			BeginBlock();

			int lf = NewLabel();
			int l_iter = NewLabel();
			int l_cond = NewLabel();
			int l_stmt = NewLabel();

			Match('(');
			// parse for-initializer
			if (!IsCurrText(';'))
			{
				bool b1 = IsIdentOrStandardType();
				int k = ReadToken();
				for (;;)
				{
					if (!IsCurrText('.'))
						break;
					k += ReadToken(); // scrip '.'
					k += ReadToken(); // read next token
				}
				bool b2 = IsIdentOrStandardType() || IsCurrText('[');
				if (IsCurrText('['))
				{
					k += ReadToken();
					if (!(IsCurrText(']') || (IsCurrText(','))))
						b2 = false;
				}
				Backup_SCANNER(k);
				if (b1 && b2)
				{
					Parse_LocalVariableDeclaration();
				}
				else
				{
					for (;;)
					{
						Parse_Expression();
						if (!CondMatch(',')) break;
					}
				}
			}
			Match(';');
			// parse for-condition
			SetLabelHere(l_cond);
			if (!IsCurrText(';'))
				Gen(code.OP_GO_FALSE, lf, Parse_Expression(), 0);
			Gen(code.OP_GO, l_stmt, 0, 0);
			Match(';');
			// parse for-iterator
			SetLabelHere(l_iter);
			if (!IsCurrText(')'))
				for (;;)
				{
					Parse_Expression();
					if (!CondMatch(',')) break;
				}
			Gen(code.OP_GO, l_cond, 0, 0);
			Match(')');
			// parse embedded statement
			SetLabelHere(l_stmt);
			BreakStack.Push(lf);
			ContinueStack.Push(l_iter);
			Parse_EmbeddedStmt();
			BreakStack.Pop();
			ContinueStack.Pop();
			Gen(code.OP_GO, l_iter, 0, 0);
			SetLabelHere(lf);
			EndBlock();
		}

		/// <summary>
		/// Parses Goto Statement.
		/// </summary>
        internal virtual void Parse_GotoStmt()
		{
			Match("goto");
			int l = Parse_Ident();
			Gen(code.OP_GOTO_START, l, 0, 0);

			if (IsCurrText("case"))
			{
				// A goto case is only valid inside a switch statement
				RaiseError(true, Errors.CS0153);
			}

			Match(';');
		}

		/// <summary>
		/// Parses Break Statement.
		/// </summary>
        internal virtual void Parse_BreakStmt()
		{
			if (BreakStack.Count == 0)
				// No enclosing loop out of which to break or continue
				RaiseError(false, Errors.CS0139);
			Gen(code.OP_GOTO_START, BreakStack.TopLabel(), 0, 0);
			Match("break");
			Match(';');
		}

		/// <summary>
		/// Parses Continue Statement.
		/// </summary>
        internal virtual void Parse_ContinueStmt()
		{
			if (ContinueStack.Count == 0)
				// No enclosing loop out of which to break or continue
				RaiseError(false, Errors.CS0139);
			Gen(code.OP_GOTO_START, ContinueStack.TopLabel(), 0, 0);
			Match("continue");
			Match(';');
		}

		/// <summary>
		/// Parses Return Statement.
		/// </summary>
        internal virtual void Parse_ReturnStmt()
		{
			Match("return");
			if (!IsCurrText(';'))
			{
				int sub_id = CurrLevel;
				int res_id = GetResultId(sub_id);
				Gen(code.OP_ASSIGN, res_id, Parse_Expression(), res_id);
			}
			Gen(code.OP_EXIT_SUB, 0, 0, 0);
			Match(';');
		}

		/// <summary>
		/// Parses Expression Statement.
		/// </summary>
        internal virtual void Parse_ExpressionStmt()
		{
			Parse_StatementExpression();
			Match(';');
		}

		/// <summary>
		/// Parses Statement Expression.
		/// </summary>
        internal virtual void Parse_StatementExpression()
		{
			Parse_Assignment();
		}

		/// <summary>
		/// Parses Assignment.
		/// </summary>
        internal virtual void Parse_Assignment()
		{
			int result = Parse_UnaryExpr(0);
			if (IsCurrText(';'))
				return;

			object v = assign_operators[curr_token.Text];
			if (v != null)
			{
				int op = (int) v;
				Call_SCANNER();
				if (op == code.OP_ASSIGN)
				{
					Gen(op, result, Parse_Expression(), result);
				}
				else
				{
					int temp = NewVar();
					Gen(op, result, Parse_Expression(), temp);
					Gen(code.OP_ASSIGN, result, temp, result);
				}
			}
			else
				Match('=');
		}

		/// <summary>
		/// Parses statement list.
		/// </summary>
        internal virtual void Parse_StatementList()
		{
			for (;;)
			{
				if (IsCurrText('.'))
				  break;
				if (IsCurrText('}'))
				  break;
				if (IsEOF())
					break;

				Parse_Stmt();
			}
		}

		/// <summary>
		/// Parses compilation unit.
		/// </summary>
        internal virtual void Parse_CompilationUnit()
		{
			if (IsCurrText("using"))
				Parse_UsingDirectives();
			if (IsCurrText('['))
				Parse_GlobalAttributes();
			if (IsEOF())
				return;

			Gen(code.OP_BEGIN_USING, RootNamespaceId, 0, 0);
			Parse_NamespaceMemberDeclarations();
			Gen(code.OP_END_USING, RootNamespaceId, 0, 0);
		}

		///////////////// ATTRIBUTES ////////////////////////////////////////

		/// <summary>
		/// Parses global attributes.
		/// </summary>
        internal virtual void Parse_GlobalAttributes()
		{
			// parse global attribute sections
			no_gen = true;
			for (;;)
			{
				// parse global attribute section
				Match('[');
				Parse_GlobalAttributeTargetSpecifier();
				Parse_AttributeList();
				if (IsCurrText(','))
					Match(',');
				Match(']');

				if (!IsCurrText('['))
					break;
			}
			no_gen = false;
		}

		/// <summary>
		/// Parses global attribute target specifier.
		/// </summary>
        internal virtual void Parse_GlobalAttributeTargetSpecifier()
		{
			Match("assembly");
			Match(':');
		}

		/// <summary>
		/// Parses attribute list.
		/// </summary>
        internal virtual void Parse_AttributeList()
		{
			for(;;)
			{
				Parse_Attribute();
				if (!CondMatch(',')) break;
			}
		}

		/// <summary>
		/// Parses attribute.
		/// </summary>
        internal virtual void Parse_Attribute()
		{
			Parse_AttributeName();
			if (IsCurrText('('))
				Parse_AttributeArguments();
		}

		/// <summary>
		/// Parses attribute name.
		/// </summary>
        internal virtual void Parse_AttributeName()
		{
			Parse_Type();
		}

		/// <summary>
		/// Parses attribute arguments.
		/// </summary>
        internal virtual void Parse_AttributeArguments()
		{
			Match('(');

			// parse positional argument list
			for (;;)
			{
				int k = ReadToken();
				string next_text = curr_token.Text;
				Backup_SCANNER(k);

				if (next_text == "=")
					break;

				Parse_Expression();
				if (!CondMatch(',')) break;
			}

			if (!IsCurrText(')'))
			{
				// parse named argument list

				for (;;)
				{
					Parse_Ident();
					Match('=');
					Parse_Expression();
					if (!CondMatch(',')) break;
				}
			}

			Match(')');
		}

		/// <summary>
		/// Parses attributes.
		/// </summary>
        internal virtual void Parse_Attributes()
		{
			// parse attribute sections
			no_gen = true;
			for (;;)
			{
				// parse attribute section
				Match('[');

				// parse attribute target specifier (opt)

				if (IsCurrText("field"))
				{
					Match("field");
					Match(':');
				}
				else if (IsCurrText("event"))
				{
					Match("event");
					Match(':');
				}
				else if (IsCurrText("method"))
				{
					Match("method");
					Match(':');
				}
				else if (IsCurrText("param"))
				{
					Match("param");
					Match(':');
				}
				else if (IsCurrText("property"))
				{
					Match("property");
					Match(':');
				}
				else if (IsCurrText("return"))
				{
					Match("return");
					Match(':');
				}
				else if (IsCurrText("type"))
				{
					Match("type");
					Match(':');
				}

				Parse_AttributeList();
				if (IsCurrText(','))
					Match(',');

				Match(']');

				if (!IsCurrText('['))
					break;
			}
			no_gen = false;
		}

		///////////////// NAMESPACES ////////////////////////////////////////

		/// <summary>
		/// Parses using directives.
		/// </summary>
        internal virtual void Parse_UsingDirectives()
		{
			for (;;)
			{
				Parse_UsingDirective();
				if (!IsCurrText("using"))
					break;
			}
		}

		/// <summary>
		/// Parses using directive.
		/// </summary>
        internal virtual void Parse_UsingDirective()
		{
			DECLARE_SWITCH = true;
			Match("using");
			DECLARE_SWITCH = false;
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
			Match(';');
		}

		/// <summary>
		/// Parses namespace or type name.
		/// </summary>
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

		/// <summary>
		/// Parses namespace member declarations.
		/// </summary>
        internal virtual void Parse_NamespaceMemberDeclarations()
		{
			for (;;)
			{
				if (IsCurrText('}'))
					// Type or namespace definition, or end-of-file expected
					RaiseError(true, Errors.CS1022);
				else if (IsCurrText("using"))
					// A using clause must precede all other namespace elements
					RaiseError(true, Errors.CS1529);
				else if (IsCurrText("new"))
					// Keyword new not allowed on namespace elements
					RaiseError(true, Errors.CS1530);

				if (IsCurrText('['))
					Parse_Attributes();
				ModifierList ml = Parse_Modifiers();

				if (IsCurrText("namespace"))
					Parse_NamespaceDeclaration();
				else
				{
					Parse_TypeDeclaration(ml);
				}

				if (IsEOF())
					break;

				if (IsCurrText('}'))
					break;
			}
		}

		/// <summary>
		/// Parses type declaration.
		/// </summary>
        internal virtual void Parse_TypeDeclaration(ModifierList ml)
		{
			if (IsCurrText("class"))
				Parse_ClassDeclaration(ml);
			else if (IsCurrText("struct"))
				Parse_StructDeclaration(ml);
			else if (IsCurrText("interface"))
				Parse_InterfaceDeclaration(ml);
			else if (IsCurrText("enum"))
				Parse_EnumDeclaration(ml);
			else if (IsCurrText("delegate"))
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

					Parse_StatementList();
					EndMethod(sub_id);

					EndClass(class_id);
				}
				else
					Match("class");
			}
		}

		/// <summary>
		/// Parses namespace declaration.
		/// </summary>
        internal virtual void Parse_NamespaceDeclaration()
		{
			Match("namespace");

			IntegerList l = new IntegerList(false);

			int namespace_id;
			for (;;)
			{
				namespace_id = Parse_Ident();
				l.Add(namespace_id);
				BeginNamespace(namespace_id);
				if (!CondMatch('.')) break;
			}
			// parse namespace body
			Match('{');
			if (IsCurrText("using"))
				Parse_UsingDirectives();
			if (!IsCurrText('}'))
				Parse_NamespaceMemberDeclarations();

			for (int i=l.Count - 1; i >= 0; i--)
				EndNamespace(l[i]);
			Match('}');
			if (IsCurrText(';'))
				Match(';');
		}

		/// <summary>
		/// Parses qualified identifier.
		/// </summary>
        internal virtual int Parse_QualifiedIdent()
		{
			int result = Parse_Ident();
			for (;;)
			{
				REF_SWITCH = true;
				if (!CondMatch('.')) break;

				if (GetKind(result) != MemberKind.Type)
					Gen(code.OP_EVAL_TYPE, 0, 0, result);

				int object_id = result;
				result = Parse_Ident();
				Gen(code.OP_CREATE_TYPE_REFERENCE, object_id, 0, result);
			}
			REF_SWITCH = false;
			return result;
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
		/// Parses class declaration.
		/// </summary>
        internal virtual void Parse_ClassDeclaration(ModifierList ml)
		{
			DECLARE_SWITCH = true;
			has_constructor = false;

			CheckModifiers(ml, class_modifiers);
			Match("class");
			int class_id = Parse_Ident();

			if (ml.HasModifier(Modifier.Abstract) && (ml.HasModifier(Modifier.Sealed)))
			{
				// The class 'class' is abstract and sealed
				RaiseError(false, Errors.CS0502);
			}

			BeginClass(class_id, ml);
			if (IsCurrText(':'))
				Parse_ClassBase(class_id);
			else
				Gen(code.OP_ADD_ANCESTOR, class_id, ObjectClassId, 0);
			Parse_ClassBody(class_id, ml);
			if (static_variable_initializers.Count > 0)
				CreateDefaultStaticConstructor(class_id);
			if (!has_constructor)
				CreateDefaultConstructor(class_id, false);
			EndClass(class_id);
			if (IsCurrText(';'))
				Match(';');
		}

		/// <summary>
		/// Parses class base.
		/// </summary>
        internal virtual void Parse_ClassBase(int class_id)
		{
			Match(':');
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
			if (!IsCurrText('{'))
				// Invalid base type
				RaiseError(false, Errors.CS1521);
		}

		/// <summary>
		/// Parses class body.
		/// </summary>
        internal virtual void Parse_ClassBody(int class_id, ModifierList owner_modifiers)
		{
			variable_initializers.Clear();
			static_variable_initializers.Clear();
			Match('{');
			for (;;)
			{
				if (IsCurrText('}'))
					break;
				Parse_ClassMemberDeclaration(class_id, owner_modifiers);
			}
			Match('}');
		}

		/// <summary>
		/// Parses class member declaration.
		/// </summary>
        internal virtual void Parse_ClassMemberDeclaration(int class_id, ModifierList owner_modifiers)
		{
			Parse_MemberDeclaration(class_id, ClassKind.Class, owner_modifiers);
		}

		/// <summary>
		/// Parses class member declaration.
		/// </summary>
        internal virtual void Parse_MemberDeclaration(int class_id, ClassKind ck, ModifierList owner_ml)
		{
			string class_name = GetName(class_id);
			DECLARE_SWITCH = true;
			if (IsCurrText('['))
				Parse_Attributes();
			ModifierList ml = Parse_Modifiers();
			bool IsStatic = ml.HasModifier(Modifier.Static);

			int k = ReadToken();
			string next_text = curr_token.Text;
			k += ReadToken();
			string next_text2 = curr_token.Text;
			Backup_SCANNER(k);

			if (IsCurrText("enum")|
				IsCurrText("class") |
				IsCurrText("struct") |
				IsCurrText("interface") |
				IsCurrText("delegate")
				)
				Parse_TypeDeclaration(ml);
			else if (IsCurrText("const"))
			{
				Match("const");
				CheckModifiers(ml, constant_modifiers);
				ml.Add(Modifier.Static);

				int type_id = Parse_Type();
				// parse constant-declaratotors
				for (;;)
				{
					// parse constant-declarator
					int id = Parse_Ident();

					if (IsStatic)
					{
						// The constant 'variable' cannot be marked static
						RaiseErrorEx(false, Errors.CS0504, GetName(id));
					}

					if (!IsCurrText('='))
					{
						// A const field requires a value to be provided
						RaiseError(true, Errors.CS0145);
					}
					BeginField(id, ml, type_id);
					DECLARE_SWITCH = false;
					Parse_InstanceVariableInitializer(id, type_id, ml);
					EndField(id);
					DECLARE_SWITCH = true;

					if (!CondMatch(',')) break;
				}
				Match(';');
			}
			else if (IsCurrText("event"))
			{
				Match("event");
				CheckModifiers(ml, method_modifiers);

				int type_id = Parse_Type();
				int id = Parse_Ident();

				if (ml.HasModifier(Modifier.Abstract) && (!owner_ml.HasModifier(Modifier.Abstract)))
				{
					string member_name = GetName(id);
					// 'function' is abstract but it is contained in nonabstract class 'class'
					RaiseErrorEx(false, Errors.CS0513,
						member_name, class_name);
				}

				if (IsCurrText('{'))
				{
					BeginEvent(id, ml, type_id, 0);
					Gen(code.OP_ADD_MODIFIER, id, (int) Modifier.Public, 0);
					param_ids.Clear();
					param_type_ids.Clear();
					param_mods.Clear();
					Parse_EventAccessorDeclarations(id, type_id, ml);
					EndEvent(id);
				}
				else
				{
					for (;;)
					{
						// create a modifier list for private members
						ModifierList ml_private = ml.Clone();
						ml_private.Delete(Modifier.Public);

						// parse field declarator
						BeginField(id, ml_private, type_id);

						string event_name = GetName(id);
						SetName(id, "__" + event_name);

						if (IsCurrText('='))
						{
							Parse_InstanceVariableInitializer(id, type_id, ml);
						}
						EndField(id);

						int sub_id, param_id, field_id, temp_id;

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

						if (IsCurrText(','))
						{
							Call_SCANNER();
							id = Parse_Ident();
						}
						else
							break;
					}
					Match(';');
				}
			}
			else if (IsCurrText("implicit"))
			{
				CheckModifiers(ml, operator_modifiers);
				Match("implicit");
				Match("operator");

				if (!(ml.HasModifier(Modifier.Public) && ml.HasModifier(Modifier.Static)))
					// User-defined operator 'operator' must be declared static and public
					RaiseErrorEx(false, Errors.CS0558,	"op_Implicit");

				int type_id = Parse_Type();
				int sub_id = NewVar();
				SetName(sub_id, "op_Implicit");
				BeginMethod(sub_id, MemberKind.Method, ml, type_id);
				Match('(');
				int param_count = Parse_FormalParameterList(sub_id, false);
				if (param_count != 1)
				{
					RaiseErrorEx(false, Errors.CS1535, "implicit");
				}
				Match(')');
				if (IsCurrText(';'))
					Match(';');
				else
				{
					InitMethod(sub_id);
					Parse_MethodBlock();
				}
				EndMethod(sub_id);
			}
			else if (IsCurrText("explicit"))
			{
				CheckModifiers(ml, operator_modifiers);
				Match("explicit");
				Match("operator");

				if (!(ml.HasModifier(Modifier.Public) && ml.HasModifier(Modifier.Static)))
					// User-defined operator 'operator' must be declared static and public
					RaiseErrorEx(false, Errors.CS0558,	"op_Explicit");

				int type_id = Parse_Type();
				int sub_id = NewVar();
				SetName(sub_id, "op_Explicit");
				BeginMethod(sub_id, MemberKind.Method, ml, type_id);
				Match('(');
				int param_count = Parse_FormalParameterList(sub_id, false);
				if (param_count != 1)
				{
					RaiseErrorEx(false, Errors.CS1535, "explicit");
				}
				Match(')');

				string return_type_name = GetName(type_id);
				string param_type_name = GetName(param_type_ids[0]);

				if ((return_type_name == class_name) && (param_type_name == class_name))
					// User-defined conversion must convert to or from the enclosing type
					RaiseError(false, Errors.CS0556);

				if ((return_type_name != class_name) && (param_type_name != class_name))
					// User-defined conversion must convert to or from the enclosing type
					RaiseError(false, Errors.CS0556);

				if (IsCurrText(';'))
					Match(';');
				else
				{
					InitMethod(sub_id);
					Parse_MethodBlock();
				}
				EndMethod(sub_id);
			}
			else if (IsCurrText("~")) // destructor
			{
				if (ck == ClassKind.Struct)
					// Only class types can contain destructors
					RaiseError(false, Errors.CS0575);

				CheckModifiers(ml, destructor_modifiers);
				Match("~");
				int sub_id = Parse_Ident();

				if (GetName(class_id) != GetName(sub_id))
				{
					// Name of destructor must match name of class
					RaiseError(false, Errors.CS0574);
				}

				BeginMethod(sub_id, MemberKind.Method, ml, (int)StandardType.Void);
				Match('(');
				if (!IsCurrText(')'))
					Parse_FormalParameterList(sub_id, false);
				Match(')');
				if (IsCurrText(';'))
					Match(';');
				else
				{
					InitMethod(sub_id);
					Parse_MethodBlock();
				}
				EndMethod(sub_id);
			}
			else if ((GetName(class_id) == curr_token.Text) && (next_text == "(")) // constructor
			{
				CheckModifiers(ml, constructor_modifiers);
				if ((IsStatic) && HasAccessModifier(ml))
					// 'function' : access modifiers are not allowed on static constructors
					RaiseErrorEx(false, Errors.CS0515, GetName(class_id));

				int sub_id = Parse_Ident();
				valid_this_context = true;
				BeginMethod(sub_id, MemberKind.Constructor, ml, (int)StandardType.Void);
				Match('(');
				if (!IsCurrText(')'))
				{
					if (IsStatic)
						// 'constructor' : a static constructor must be parameterless
						RaiseErrorEx(false, Errors.CS0132, GetName(class_id));
					Parse_FormalParameterList(sub_id, false);
				}
				else if (ck == ClassKind.Struct)
					// Structs cannot contain explicit parameterless constructors
					RaiseError(false, Errors.CS0568);

				Match(')');
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

				if (IsCurrText(':')) // constructor-initializer
				{
					Match(':');
					if (IsStatic)
						// 'constructor' : static constructor cannot have an explicit this or base constructor call
						RaiseErrorEx(false, Errors.CS0514, GetName(class_id));

					if (IsCurrText("base"))
					{
						if (ck == ClassKind.Struct)
							// 'constructor' : structs cannot call base class constructors
							RaiseErrorEx(false, Errors.CS0522, GetName(class_id));
						Match("base");

						int base_sub_id = NewVar();
						int base_type_id = NewVar();
						Gen(code.OP_EVAL_BASE_TYPE, class_id, 0, base_type_id);
						Gen(code.OP_ASSIGN_NAME, base_sub_id, base_type_id, base_sub_id);
						int object_id = NewVar();
						Gen(code.OP_CAST, base_type_id, CurrThisID, object_id);
						Gen(code.OP_CREATE_REFERENCE, object_id, 0, base_sub_id);

						DECLARE_SWITCH = false;
						Match('(');
						Gen(code.OP_CALL_BASE, base_sub_id, Parse_ArgumentList(")", base_sub_id, object_id), 0);
						DECLARE_SWITCH = true;
						Match(')');
					}
					else if (IsCurrText("this"))
					{
						Match("this");
						DECLARE_SWITCH = false;
						Match('(');
						Gen(code.OP_CALL, sub_id, Parse_ArgumentList(")", sub_id, CurrThisID), 0);
						DECLARE_SWITCH = true;
						Match(')');
					}
					else
					{
						// Keyword this or base expected
						RaiseError(true, Errors.CS1018);
					}
				}
				else if (!IsStatic)
				{
					int base_sub_id = NewVar();
					int base_type_id = NewVar();
					Gen(code.OP_EVAL_BASE_TYPE, class_id, 0, base_type_id);
					Gen(code.OP_ASSIGN_NAME, base_sub_id, base_type_id, base_sub_id);
					int object_id = NewVar();
					Gen(code.OP_CAST, base_type_id, CurrThisID, object_id);
					Gen(code.OP_CREATE_REFERENCE, object_id, 0, base_sub_id);

					Gen(code.OP_BEGIN_CALL, base_sub_id, 0, 0);
					Gen(code.OP_PUSH, object_id, 0, 0);
					Gen(code.OP_CALL, base_sub_id, 0, 0);
				}

				if (IsCurrText(';'))
					Match(';');
				else
					Parse_Block();
				EndMethod(sub_id);
				valid_this_context = false;

				if (IsStatic)
					static_variable_initializers.Clear(); // already processed
				else
					has_constructor = true;
			}
			else
			{
				int type_id = Parse_Type();

				int explicit_intf_id = 0;
				if (next_text2 == ".")
				{
					if (ml.HasModifier(Modifier.Public))
						// The modifier 'modifier' is not valid for this item
						RaiseErrorEx(false, Errors.CS0106, "public");
					ml.Add(Modifier.Public);

					explicit_intf_id = Parse_Ident();
					Gen(code.OP_EVAL_TYPE, 0, 0, explicit_intf_id);
					for (;;)
					{
						REF_SWITCH = true;
						if (!CondMatch('.')) break;

						int kk = ReadToken();
						string s = curr_token.Text;
						Backup_SCANNER(kk);

						if (s != ".")
							break;

						int base_id = explicit_intf_id;
						explicit_intf_id = Parse_Ident();
						Gen(code.OP_CREATE_TYPE_REFERENCE, base_id, 0, explicit_intf_id);
					}
					REF_SWITCH = false;
				}

				if (IsCurrText("this")) // indexed property
				{
					CheckModifiers(ml, method_modifiers);
					Match("this");
					int prop_id = NewVar();
					SetName(prop_id, "Item");

					if (type_id == (int) StandardType.Void)
					{
						// Indexers can't have void type
						RaiseError(false, Errors.CS0620);
					}

					if (ml.HasModifier(Modifier.Abstract) && (!owner_ml.HasModifier(Modifier.Abstract)))
					{
						string member_name = GetName(prop_id);
						// 'function' is abstract but it is contained in nonabstract class 'class'
						RaiseErrorEx(false, Errors.CS0513, member_name, class_name);
					}

					Match('[');
					int param_count = Parse_FormalParameterList(prop_id, true);
					Match(']');
					valid_this_context = true;
					BeginProperty(prop_id, ml, type_id, param_count);
					Parse_PropertyAccessorDeclarations(prop_id, type_id, ml);
					EndProperty(prop_id);
					valid_this_context = false;
				}
				else if (IsCurrText("operator"))
				{
					CheckModifiers(ml, operator_modifiers);

					Match("operator");

					if (type_id == (int) StandardType.Void)
						RaiseError(false, Errors.CS0590);

					if (!(ml.HasModifier(Modifier.Public) && ml.HasModifier(Modifier.Static)))
						// User-defined operator 'operator' must be declared static and public
						RaiseErrorEx(false, Errors.CS0558, curr_token.Text);

					string operator_name = curr_token.Text;

					Call_SCANNER();

					int sub_id = NewVar();
					BeginMethod(sub_id, MemberKind.Method, ml, type_id);
					Match('(');
					int param_count = Parse_FormalParameterList(sub_id, false);
					string operator_method_name = "";
					if (param_count == 1)
					{
						int i = overloadable_unary_operators.IndexOf(operator_name);
						if (i == -1)
							// Overloadable unary operator expected
							RaiseError(true, Errors.CS1019);
						int op = (int) overloadable_unary_operators.Objects[i];
						operator_method_name = (string) code.overloadable_unary_operators_str[op];
					}
					else if (param_count == 2)
					{
						int i = overloadable_binary_operators.IndexOf(operator_name);
						if (i == -1)
							// Overloadable binary operator expected
							RaiseError(true, Errors.CS1020);
						int op = (int) overloadable_binary_operators.Objects[i];
						operator_method_name = (string) code.overloadable_binary_operators_str[op];
					}
					else
					{
						// Overloaded binary operator 'operator' only takes two parameters
						RaiseErrorEx(true, Errors.CS1534, operator_name);
					}
					SetName(sub_id, operator_method_name);
					Match(')');
					if (IsCurrText(';'))
						Match(';');
					else
					{
						InitMethod(sub_id);
						Parse_MethodBlock();
					}
					EndMethod(sub_id);
				}
				else
				{
					if (IsCurrText('('))
						// Class, struct, or interface method must have a return type
						RaiseError(true, Errors.CS1520);

					Again:
					int id = Parse_Ident();

					if (IsCurrText(';')) // field
					{
						if (type_id == (int) StandardType.Void)
							RaiseError(false, Errors.CS0670);

						CheckModifiers(ml, field_modifiers);
						BeginField(id, ml, type_id);
						Add_InstanceVariableInitializer(id, type_id, ml);
						EndField(id);
						Match(';');
					}
					else if (IsCurrText(',')) // field
					{
						CheckModifiers(ml, field_modifiers);
						BeginField(id, ml, type_id);
						EndField(id);
						Match(',');
						for (;;)
						{
							// parse variable declarator
							id = Parse_Ident();
							BeginField(id, ml, type_id);
							if (IsCurrText('='))
							{
								if ((!ml.HasModifier(Modifier.Static) && (ck == ClassKind.Struct)))
								{
									// 'field declaration' : cannot have instance field initializers in structs
									RaiseErrorEx(false, Errors.CS0573, GetName(id));
								}
								Parse_InstanceVariableInitializer(id, type_id, ml);
							}
							else
								Add_InstanceVariableInitializer(id, type_id, ml);
							EndField(id);
							if (!CondMatch(',')) break;
						}
						Match(';');
					}
					else if (IsCurrText('=')) // field
					{
						if (type_id == (int) StandardType.Void)
							RaiseError(false, Errors.CS0670);

						if ((!ml.HasModifier(Modifier.Static)) && (ck == ClassKind.Struct))
						{
							// 'field declaration' : cannot have instance field initializers in structs
							RaiseErrorEx(false, Errors.CS0573, GetName(id));
						}

						CheckModifiers(ml, field_modifiers);
						BeginField(id, ml, type_id);
						Parse_InstanceVariableInitializer(id, type_id, ml);
						EndField(id);

						if (IsCurrText(','))
						{
							Match(',');
							goto Again;
						}
						else
							Match(';');
					}
					else if (IsCurrText('(')) // method
					{
						CheckModifiers(ml, method_modifiers);

						if (ml.HasModifier(Modifier.Abstract) && (!owner_ml.HasModifier(Modifier.Abstract)))
						{
							string member_name = GetName(id);
							// 'function' is abstract but it is contained in nonabstract class 'class'
							RaiseErrorEx(false, Errors.CS0513,	member_name, class_name);
						}

						if (IsStatic)
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

						int sub_id = id;

						valid_this_context = true;
						BeginMethod(sub_id, MemberKind.Method, ml, type_id);
						if (explicit_intf_id > 0)
							Gen(code.OP_ADD_EXPLICIT_INTERFACE, sub_id, explicit_intf_id, 0);

						Match('(');
						if (!IsCurrText(')'))
							Parse_FormalParameterList(sub_id, false);
						Match(')');

						if (IsCurrText(';'))
						{
							if (!(ml.HasModifier(Modifier.Extern) || ml.HasModifier(Modifier.Abstract)))
							{
								// 'member function' must declare a body because it is not marked abstract or extern
								RaiseErrorEx(false, Errors.CS0501, GetName(sub_id));
							}
							Match(';');
						}
						else
						{
							if (ml.HasModifier(Modifier.Extern))
							{
								// 'member' cannot be extern and declare a body
								RaiseErrorEx(false, Errors.CS0179, GetName(sub_id));
							}

							InitMethod(sub_id);

							if (ml.HasModifier(Modifier.Abstract))
							{
								string method_name = GetName(sub_id);
								// 'class member' cannot declare a body because it is marked abstract
								RaiseErrorEx(false, Errors.CS0500, method_name);
							}

							if (GetName(id) == "Main")
								Gen(code.OP_CHECKED, TRUE_id, 0, 0);
							Parse_MethodBlock();
							if (GetName(id) == "Main")
								Gen(code.OP_RESTORE_CHECKED_STATE, 0, 0, 0);
						}

						EndMethod(sub_id);
						valid_this_context = false;
					}
					else if (IsCurrText('{')) // property
					{
						CheckModifiers(ml, method_modifiers);

						if (ml.HasModifier(Modifier.Abstract) && (!owner_ml.HasModifier(Modifier.Abstract)))
						{
							string member_name = GetName(id);
							// 'function' is abstract but it is contained in nonabstract class 'class'
							RaiseErrorEx(false, Errors.CS0513, member_name, class_name);
						}

						valid_this_context = true;
						BeginProperty(id, ml, type_id, 0);
						param_ids.Clear();
						param_type_ids.Clear();
						param_mods.Clear();
						Parse_PropertyAccessorDeclarations(id, type_id, ml);
						EndProperty(id);
						valid_this_context = false;
					}
					else
						Parse_TypeDeclaration(ml);
				}
			}
			DECLARE_SWITCH = false;
		}

		/// <summary>
		/// Parses instance variable initializer.
		/// </summary>
        internal virtual void Parse_InstanceVariableInitializer(int id, int type_id, ModifierList ml)
		{
			DECLARE_SWITCH = false;
			Match('=');
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
		/// Adds instance variable initializer.
		/// </summary>
        internal virtual void Add_InstanceVariableInitializer(int id, int type_id, ModifierList ml)
		{
			int val_id = 0;
			if (type_id == (int) StandardType.Bool)
				val_id = NewConst(false);
			else if (type_id == (int) StandardType.Byte)
				val_id = NewConst((byte)0);
			else if (type_id == (int) StandardType.Char)
				val_id = NewConst(' ');
			else if (type_id == (int) StandardType.Decimal)
				val_id = NewConst(0m);
			else if (type_id == (int) StandardType.Double)
				val_id = NewConst(0d);
			else if (type_id == (int) StandardType.Float)
				val_id = NewConst(0f);
			else if (type_id == (int) StandardType.Int)
				val_id = NewConst((int)0);
			else if (type_id == (int) StandardType.Long)
				val_id = NewConst(0L);
			else if (type_id == (int) StandardType.Sbyte)
				val_id = NewConst((sbyte)0);
			else if (type_id == (int) StandardType.Short)
				val_id = NewConst((short)0);
			else if (type_id == (int) StandardType.String)
				val_id = NewConst("");
			else if (type_id == (int) StandardType.Uint)
				val_id = NewConst((uint)0);
			else if (type_id == (int) StandardType.Ulong)
				val_id = NewConst((ulong)0);
			else if (type_id == (int) StandardType.Ushort)
				val_id = NewConst((ushort)0);

			if (val_id > 0)
			{
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

				Gen(code.OP_ASSIGN, field_id, val_id, field_id);
				EndMethod(sub_id);
			}
		}

		/// <summary>
		/// Parses property accessor declarations.
		/// </summary>
        internal virtual void Parse_PropertyAccessorDeclarations(int id, int type_id, ModifierList ml)
		{
			ACCESSOR_SWITCH = true;

			if (type_id == (int) StandardType.Void)
			{
				RaiseError(true, Errors.CS0547);
			}

			Match('{');
			int count_get = 0;
			int count_set = 0;
			for (;;)
			{
				if (IsCurrText('['))
					Parse_Attributes();
				if (IsCurrText("get"))
				{
					int sub_id = NewVar();
					SetName(sub_id, "get_" + GetName(id));
					BeginMethod(sub_id, MemberKind.Method, ml, type_id);
					count_get++;
					if (count_get > 1)
						// Property accessor already defined
						RaiseError(true, Errors.CS1008);
					Match("get");
					for (int i = 0; i < param_ids.Count; i++)
					{
						int param_id = NewVar();
						SetName(param_id, GetName(param_ids[i]));
						Gen(code.OP_ASSIGN_TYPE, param_id, param_type_ids[i], 0);
						Gen(code.OP_ADD_PARAM, sub_id, param_id, 0);
					}
					if (IsCurrText(';'))
					{
						if (!(ml.HasModifier(Modifier.Extern) || ml.HasModifier(Modifier.Abstract)))
						{
							// 'member function' must declare a body because it is not marked abstract or extern
							RaiseErrorEx(false, Errors.CS0501, GetName(sub_id));
						}
						Match(';');
					}
					else
					{
						if (ml.HasModifier(Modifier.Abstract))
						{
							string method_name = GetName(sub_id);
							// 'class member' cannot declare a body because it is marked abstract
							RaiseErrorEx(true, Errors.CS0500, method_name);
						}

						InitMethod(sub_id);
						Parse_MethodBlock();
					}
					EndMethod(sub_id);

					Gen(code.OP_ADD_READ_ACCESSOR, id, sub_id, 0);
				}
				else if (IsCurrText("set"))
				{
					int sub_id = NewVar();
					SetName(sub_id, "set_" + GetName(id));
					BeginMethod(sub_id, MemberKind.Method, ml, (int) StandardType.Void);
					count_set++;
					if (count_set > 1)
						// Property accessor already defined
						RaiseError(true, Errors.CS1008);
					Match("set");

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

					if (IsCurrText(';'))
					{
						if (!(ml.HasModifier(Modifier.Extern) || ml.HasModifier(Modifier.Abstract)))
						{
							// 'member function' must declare a body because it is not marked abstract or extern
							RaiseErrorEx(false, Errors.CS0501, GetName(sub_id));
						}

						Match(';');
					}
					else
					{
						if (ml.HasModifier(Modifier.Abstract))
						{
							string method_name = GetName(sub_id);
							// 'class member' cannot declare a body because it is marked abstract
							RaiseErrorEx(true, Errors.CS0500, method_name);
						}

						InitMethod(sub_id);
						Parse_MethodBlock();
					}
					EndMethod(sub_id);

					Gen(code.OP_ADD_WRITE_ACCESSOR, id, sub_id, 0);
				}
				else
					break;
			}
			if (count_get + count_set == 0)
			{
				// 'property' : property or indexer must have at least one accessor
				RaiseErrorEx(true, Errors.CS0548, GetName(id));
			}
			Match('}');

			ACCESSOR_SWITCH = true;
		}

		/// <summary>
		/// Parses event accessor declarations.
		/// </summary>
        internal virtual void Parse_EventAccessorDeclarations(int id, int type_id, ModifierList ml)
		{
			ACCESSOR_SWITCH = true;

			Match('{');
			int count_add = 0;
			int count_remove = 0;
			for (;;)
			{
				if (IsCurrText('['))
					Parse_Attributes();

				if (IsCurrText("add"))
				{
					int sub_id = NewVar();
					SetName(sub_id, "add_" + GetName(id));
					BeginMethod(sub_id, MemberKind.Method, ml, type_id);
					count_add++;
					if (count_add > 1)
						RaiseError(true, Errors.CS1007);
					Match("add");

					int param_id = NewVar();
					SetName(param_id, "value");
					Gen(code.OP_ADD_PARAM, sub_id, param_id, 0);
					Gen(code.OP_ASSIGN_TYPE, param_id, type_id, 0);

					if (IsCurrText(';'))
						Match(';');
					else
					{
						if (ml.HasModifier(Modifier.Abstract))
						{
							string method_name = GetName(sub_id);
							// 'class member' cannot declare a body because it is marked abstract
							RaiseErrorEx(true, Errors.CS0500, method_name);
						}

						InitMethod(sub_id);
						Parse_MethodBlock();
					}
					EndMethod(sub_id);

					Gen(code.OP_ADD_ADD_ACCESSOR, id, sub_id, 0);
				}
				else if (IsCurrText("remove"))
				{
					int sub_id = NewVar();
					SetName(sub_id, "remove_" + GetName(id));
					BeginMethod(sub_id, MemberKind.Method, ml, (int) StandardType.Void);
					count_remove++;
					if (count_remove > 1)
						RaiseError(true, Errors.CS1007);
					Match("remove");

					int param_id = NewVar();
					SetName(param_id, "value");
					Gen(code.OP_ADD_PARAM, sub_id, param_id, 0);
					Gen(code.OP_ASSIGN_TYPE, param_id, type_id, 0);

					if (IsCurrText(';'))
						Match(';');
					else
					{
						if (ml.HasModifier(Modifier.Abstract))
						{
							string method_name = GetName(sub_id);
							// 'class member' cannot declare a body because it is marked abstract
							RaiseErrorEx(true, Errors.CS0500, method_name);
						}

						InitMethod(sub_id);
						Parse_MethodBlock();
					}
					EndMethod(sub_id);

					Gen(code.OP_ADD_REMOVE_ACCESSOR, id, sub_id, 0);
				}
				else
					break;
			}

			if ((count_add == 0) && (count_remove == 0))
				// An add or remove accessor expected
				RaiseError(true, Errors.CS1055);

			if ((count_add == 0) || (count_remove == 0))
				//event property must have both add and remove accessors
				RaiseErrorEx(true, Errors.CS0065, GetName(id));
			Match('}');

			ACCESSOR_SWITCH = true;
		}

		///////////////// STRUCTS ////////////////////////////////////////

		/// <summary>
		/// Parses struct declaration.
		/// </summary>
        internal virtual void Parse_StructDeclaration(ModifierList ml)
		{
			CheckModifiers(ml, structure_modifiers);
			Match("struct");
			int struct_id = Parse_Ident();
			BeginStruct(struct_id, ml);
			if (IsCurrText(':'))
				Parse_ClassBase(struct_id);
			else
				Gen(code.OP_ADD_ANCESTOR, struct_id, ObjectClassId, 0);
			Parse_StructBody(struct_id, ml);
			if (static_variable_initializers.Count > 0)
				CreateDefaultStaticConstructor(struct_id);
			CreateDefaultConstructor(struct_id, true);
			EndStruct(struct_id);
			if (IsCurrText(';'))
				Match(';');
		}

		/// <summary>
		/// Parses struct body.
		/// </summary>
        internal virtual void Parse_StructBody(int struct_id, ModifierList owner_modifiers)
		{
			variable_initializers.Clear();
			static_variable_initializers.Clear();
			Match('{');
			for (;;)
			{
				if (IsCurrText('}'))
					break;
				Parse_StructMemberDeclaration(struct_id, owner_modifiers);
			}
			Match('}');
		}

		/// <summary>
		/// Parses struct member declaration.
		/// </summary>
        internal virtual void Parse_StructMemberDeclaration(int struct_id, ModifierList owner_modifiers)
		{
			Parse_MemberDeclaration(struct_id, ClassKind.Struct, owner_modifiers);
		}

		///////////////// ARRAYS ////////////////////////////////////////

		/// <summary>
		/// Parses array rank specifiers.
		/// </summary>
		internal virtual string Parse_RankSpecifiers()
		{
			string s = "";
			for (;;)
			{
				// rank-specifier
				Match('[');
				s += "[";
				if (!IsCurrText(']'))
				{
					// dim-separators
					for (;;)
					{
						Match(',');
						s += ",";
						if (!IsCurrText(','))
							break;
					}
				}
				Match(']');
				s += "]";

				if (!IsCurrText('['))
					break;
			}
			return s;
		}

		/// <summary>
		/// Parses array initializer.
		/// </summary>
		internal virtual int Parse_ArrayInitializer(int array_type_id)
		{
			return Parse_MultiArrayInitializer(array_type_id);
		}

		/// <summary>
		/// Parses array initializer.
		/// </summary>
        internal virtual int Parse_MultiArrayInitializer(int array_type_id)
		{
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
		/// Parses free array initializer.
		/// </summary>
		internal virtual int Parse_FreeArrayInitializer(int array_type_id, ref IntegerList bounds)
		{
			string array_type_name = GetName(array_type_id);
			int bounds_count = PaxSystem.GetRank(array_type_name);
			string element_type_name = PaxSystem.GetElementTypeName(array_type_name);
			IntegerList item_bounds = new IntegerList(true);
			int element_type_id = NewVar();

			int id;

			SetName(element_type_id, element_type_name);
			Gen(code.OP_EVAL_TYPE, 0, 0, element_type_id);

			int result = NewVar();
			Gen(code.OP_CREATE_OBJECT, array_type_id, 0, result);
			Gen(code.OP_BEGIN_CALL, array_type_id, 0, 0);

			IntegerList ids = new IntegerList(true);
			for (int i = 0; i < bounds_count; i++)
			{
				id = NewVar(0);
				ids.Add(id);
				Gen(code.OP_PUSH, id, 0, array_type_id);
			}
			Gen(code.OP_PUSH, result, 0, 0);
			Gen(code.OP_CALL, array_type_id, bounds_count, 0);

			int k = 0;
			Match('{');
			if (!IsCurrText('}'))
			{
				// variable-initializer-list
				k = 0;
				for (;;)
				{
					k++;
					int index_object_id = NewVar();
					int index = NewConst(k - 1);

					Gen(code.OP_CREATE_INDEX_OBJECT, result, 0, index_object_id);
					Gen(code.OP_ADD_INDEX, index_object_id, index, result);
					Gen(code.OP_SETUP_INDEX_OBJECT, index_object_id, 0, 0);

					id = NewVar();
					if (IsCurrText('{'))
						id = Parse_FreeArrayInitializer(element_type_id, ref item_bounds);
					else
						id = Parse_Expression();
					Gen(code.OP_ASSIGN, index_object_id, id, index_object_id);
					if (!CondMatch(',')) break;
				}
			}
			Match('}');

			// set up bounds

			id = ids[0];
			PutVal(id, k);
			bounds.Add(k);

			for (int i = 1; i < ids.Count; i++)
			{
				id = ids[i];
				k = item_bounds[i - 1];
				PutVal(id, k);
				bounds.Add(k);
			}

			return result;
		}

		///////////////// INTERFACES ////////////////////////////////////////

		/// <summary>
		/// Parses interface declaration.
		/// </summary>
		internal virtual void Parse_InterfaceDeclaration(ModifierList ml)
		{
			CheckModifiers(ml, interface_modifiers);

			DECLARE_SWITCH = true;
			Match("interface");
			int interface_id = Parse_Ident();
			BeginInterface(interface_id, ml);

			if (IsCurrText(':'))
				Parse_ClassBase(interface_id);

			Match('{');
			for (;;)
			{
				if (IsCurrText('}'))
					break;

				// interface member declaration
				if (IsCurrText('['))
					Parse_Attributes();
				ModifierList member_ml = new ModifierList();
				if (IsCurrText("new"))
				{
					Match("new");
					member_ml.Add(Modifier.New);
				}
				member_ml.Add(Modifier.Abstract);

				if (IsCurrText("event"))
				{
					// interface-event-declaration
					Match("event");
					int type_id = Parse_Type();
					int id = Parse_Ident();
					BeginEvent(id, member_ml, type_id, 0);
					EndEvent(id);
					if (IsCurrText('='))
						RaiseErrorEx(true, Errors.CS0068, GetName(id));
					if (IsCurrText('{'))
						RaiseErrorEx(true, Errors.CS0069, GetName(id));

					Match(';');
				}
				else if ((IsCurrText("class") || IsCurrText("struct") ||
					  IsCurrText("enum")) || IsCurrText("delegate"))
				{
					// 'type' : interfaces cannot declare types
					RaiseErrorEx(true, Errors.CS0524, GetName(interface_id));
				}
				else
				{
					int type_id = Parse_Type();

					if ((GetName(type_id) == GetName(interface_id)) && (IsCurrText('(')))
					{
						// Interfaces cannot contain constructors
						RaiseError(true, Errors.CS0526);
					}

					if (IsCurrText('('))
						// Class, struct, or interface method must have a return type
						RaiseError(true, Errors.CS1520);
					else if (IsCurrText("this"))
					{
						if (type_id == (int) StandardType.Void)
						{
							// Indexers can't have void type
							RaiseError(false, Errors.CS0620);
						}

						CheckModifiers(member_ml, method_modifiers);
						Match("this");
						int prop_id = NewVar();
						SetName(prop_id, "Item");
						Match('[');
						int param_count = Parse_FormalParameterList(prop_id, true);
						Match(']');
						valid_this_context = true;
						BeginProperty(prop_id, member_ml, type_id, param_count);
						Gen(code.OP_ADD_MODIFIER, prop_id, (int) Modifier.Public, 0);
						Parse_PropertyAccessorDeclarations(prop_id, type_id, member_ml);
						EndProperty(prop_id);
						valid_this_context = false;
					}
					else if (IsCurrText("operator"))
					{
						// Interfaces cannot contain operators
						RaiseError(true, Errors.CS0567);
					}
					else
					{
						int id = Parse_Ident();
						if (IsCurrText('('))
						{
							// interface-method-declaration
							int sub_id = id;
							BeginMethod(sub_id, MemberKind.Method, member_ml, type_id);
							Gen(code.OP_ADD_MODIFIER, sub_id, (int) Modifier.Public, 0);
							Match('(');
							if (!IsCurrText(')'))
								Parse_FormalParameterList(sub_id, false);
							Match(')');
							EndMethod(sub_id);

							if (IsCurrText('{'))
							{
								// 'member' : interface members cannot have a definition
								RaiseErrorEx(true, Errors.CS0531, GetName(sub_id));
							}

							Match(';');
						}
						else if (IsCurrText('{'))
						{
							// interface-property-declaration
							valid_this_context = true;
							BeginProperty(id, member_ml, type_id, 0);
							Gen(code.OP_ADD_MODIFIER, id, (int) Modifier.Public, 0);
							param_ids.Clear();
							param_type_ids.Clear();
							param_mods.Clear();
							Parse_PropertyAccessorDeclarations(id, type_id, member_ml);
							EndProperty(id);
							valid_this_context = false;
						}
						else if (IsCurrText('=') || IsCurrText(';'))
						{
							// Interfaces cannot contain fields
							RaiseError(true, Errors.CS0525);
						}
						else
						{
							Match('('); // error
						}
					}
				}
			}
			DECLARE_SWITCH = false;
			EndInterface(interface_id);
			Match('}');
			if (IsCurrText(';'))
				Match(';');
		}

		///////////////// ENUMS ////////////////////////////////////////

		/// <summary>
		/// Parses enum declaration.
		/// </summary>
		internal virtual void Parse_EnumDeclaration(ModifierList ml)
		{
			CheckModifiers(ml, enum_modifiers);
			ml.Add(Modifier.Static);

			DECLARE_SWITCH = true;
			Match("enum");
			int enum_id = Parse_Ident();
			int type_base = (int) StandardType.Int;
			if (IsCurrText(':')) // enum-base
			{
				Match(':');
				type_base = Parse_IntegralType();

				if (type_base == (int) StandardType.Char)
					// Type byte, sbyte, short, ushort, int, uint, long, or ulong expected
					RaiseError(true, Errors.CS1008);
			}
			BeginEnum(enum_id, ml, type_base);
			Gen(code.OP_ADD_UNDERLYING_TYPE, enum_id, type_base, 0);
			Match('{');
			if (!IsCurrText('}'))
			{
				int k = -1;
				static_variable_initializers.Clear();

				for (;;)
				{
					if (IsCurrText('}'))
						break;
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
					if (!CondMatch(',')) break;
				}

				CreateDefaultStaticConstructor(enum_id);
			}
			DECLARE_SWITCH = false;
			EndEnum(enum_id);
			Match('}');
			if (IsCurrText(';'))
				Match(';');
		}

		///////////////// DELEGATES ////////////////////////////////////////

		/// <summary>
		/// Parses delegate declaration.
		/// </summary>
		internal virtual void Parse_DelegateDeclaration(ModifierList ml)
		{
			CheckModifiers(ml, delegate_modifiers);
			DECLARE_SWITCH = true;
			Match("delegate");
			int return_type_id = Parse_Type();
			int delegate_type_id = Parse_Ident();
			BeginDelegate(delegate_type_id, ml);
			Match('(');
			int sub_id = NewVar();
			BeginMethod(sub_id, MemberKind.Method, ml, return_type_id);
			Gen(code.OP_ADD_PATTERN, delegate_type_id, sub_id, 0);

			if (!IsCurrText(')'))
				Parse_FormalParameterList(sub_id, false);
			Match(')');
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
			DECLARE_SWITCH = false;
			Match(';');
		}

		///////////////// COMPILATION UNIT /////////////////////////////////

		/// <summary>
		/// Parses compilation unit.
		/// </summary>
		public override void Parse_Program()
		{
			Gen(code.OP_UPCASE_OFF, 0, 0, 0);
			Gen(code.OP_EXPLICIT_ON, 0, 0, 0);
			Gen(code.OP_STRICT_ON, 0, 0, 0);
			Parse_CompilationUnit();
		}
        
        public override string ParseASPXPage(string s)
        {
            html = s + BaseScanner.CHAR_EOF + BaseScanner.CHAR_EOF + BaseScanner.CHAR_EOF;
            html_out.Clear();
            html_temp.Clear();
            html_pos = 0;

            string q;

            bool fin;
            q = Parse_LiteralControl(out fin);
            q = "Page.Controls.Add(new LiteralControl(@" + '\"' + q + '\"' + "));";
            html_out.Add(q);

            Parse_HtmlForm("Form1");

            q = Parse_LiteralControl(out fin);
            html_out.Add(q);
            return html_out.text;
        }

        internal virtual string Parse_LiteralControl(out bool fin)
        {
            fin = false;
            string result = "";
            for (; ; )
            {
                char c = html[html_pos];
                if (c == '<')
                {
                    if (html.Substring(html_pos, "<form ".Length) == "<form ")
                        break;
                    else if (html.Substring(html_pos, "</form>".Length) == "</form>")
                    {
                        fin = true;
                        break;
                    }
                    else if (html.Substring(html_pos, "<asp:".Length) == "<asp:")
                        break;
                    else
                    {
                        result += html[html_pos];
                        html_pos++;
                    }
                }
                else if (c == '\u000A')
                {
                    result += @"\n";
                    html_pos++;
                }
                else if (c == '\u000D')
                {
                    result += @"\r";
                    html_pos++;
                }
                else if (c == BaseScanner.CHAR_EOF)
                {
                    break;
                }
                else
                {
                    result += html[html_pos];
                    html_pos++;
                }
            }

            return result;
        }

        internal virtual void Parse_HtmlForm(string FormName)
        {
            string q;
            q = "HtmlForm " + FormName + " = new HtmlForm();";
            html_out.Add(q);
            q = "FormName" + ".ID = " + '\"' + FormName + '\"' + ";";
            html_out.Add(q);
            q = "FormName" + ".Method = " + '\"' + "post" + '\"' + ";";
            html_out.Add(q);

            for (; ; )
            {
                if (html[html_pos] == '>')
                    break;
                else
                    html_pos++;
            }
            html_pos++;

            // we are at literal control position here

            for (;;)
            {
                bool fin;
                q = Parse_LiteralControl(out fin);
                q = FormName + ".Controls.Add(new LiteralControl(@" + '\"' + q + '\"' + "));";
                html_out.Add(q);

                if (fin)
                    break;

                Parse_HtmlControl(FormName);
            }
            q = @"Controls.Add(" + FormName + ");";
            html_out.Add(q);
        }

        internal virtual void Parse_HtmlControl(string OwnerName)
        {
            // <asp:
            string q;
            char c;

            for (;;)
            {
                if (html[html_pos] == ':')
                    break;
                else
                    html_pos++;
            }
            html_pos++;

            // control's type starts here

            string type_name = "";

            for (; ; )
            {
                c = html[html_pos];
                if (BaseScanner.IsAlpha(c) || BaseScanner.IsDigit(c))
                {
                    type_name += c;
                    html_pos++;
                }
                else
                    break;
            }

            int count = 1;
            for (int i = 0; i < html_temp.Count; i++)
            {
                if (html_temp[i] == OwnerName && ((string)html_temp.Objects[i]) == type_name)
                    count++;
            }

            string name = type_name + count.ToString();

            html_temp.AddObject(OwnerName, type_name);

            q = type_name + " " + name + " = new " + type_name + "();";
            html_out.Add(q);

            q = OwnerName + ".Controls.Add(" + name + ");";
            html_out.Add(q);

            string Text = "";

            for (; ; )
            {
                c = html[html_pos];

                if (c == '>')
                {
                    break;
                }
                else if (c == '=')
                {
                    string prop_name = "";
                    string prop_value = "";

                    int i = html_pos - 1;
                    for (; ; )
                    {
                         c = html[i];
                         if (BaseScanner.IsAlpha(c) || BaseScanner.IsDigit(c))
                         {
                             prop_name = c + prop_name;
                             i--;
                         }
                         else
                             break;
                    }

                    html_pos++;

                    if (html[html_pos] == '\"')
                            html_pos++; // skip ""

                     for (; ; )
                     {
                         c = html[html_pos];
                         if (BaseScanner.IsAlpha(c) || BaseScanner.IsDigit(c))
                         {
                             prop_value += c;
                             html_pos++;
                         }
                         else
                             break;
                     }

                     if (html[html_pos] == '\"')
                         html_pos++; // skip ""

                     if (prop_name == "runat")
                         continue;

                     q = name + "." + prop_name + " = " + '\"' + prop_value + '\"' + ";";
                     html_out.Add(q);

                }
                else
                {
                    html_pos++;
                }
            }

            // here html[html_pos] == '>' 

            html_pos++;

            // scan to html[html_pos] == '<'

            for (; ; )
            {
                c = html[html_pos];
                if (html[html_pos] == '<')
                    break;
                else
                {
                    Text += c;
                    html_pos++;
                }
            }

            bool valid_text = false;
            for (int i=0; i < Text.Length; i++)
                if (BaseScanner.IsAlpha(Text[i]) || BaseScanner.IsDigit(Text[i]))
                {
                    valid_text = true;
                    break;
                }

            if (valid_text)
            {
                q = name + ".Text" + " = " + '\"' + Text + '\"' + ";";
                html_out.Add(q);
            }

            // here html[html_pos] == '<'

            // Two cases:
            // </asp: ....   This is closed tag of control
            // <asp:....   The control contains nested controls


            while (html.Substring(html_pos, "<asp:".Length) == "<asp:") 
            {
                Parse_HtmlControl(name);

                while (html[html_pos] != '<')
                    html_pos++;

            }

            if (html.Substring(html_pos, "</asp:".Length) == "</asp:")
            {
                while (html[html_pos] != '>') html_pos++;
                html_pos++;

                return;
            }
        }
    }
    #endregion CSharp_Parser Class
}

