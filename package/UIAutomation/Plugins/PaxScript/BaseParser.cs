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
using System.IO;

namespace PaxScript.Net
{
	#region BaseParser Class
	/// <summary>
	/// Represents base parser class. All parsers must inherit this class.
	/// </summary>
	internal class BaseParser
	{
		/// <summary>
		/// Represents kernel of scripter.
		/// </summary>
		internal BaseScripter scripter;

		/// <summary>
		/// Represents symbol table.
		/// </summary>
		internal SymbolTable symbol_table;

		/// <summary>
		/// Represents p-code.
		/// </summary>
		internal Code code;

		/// <summary>
		/// Stack of levels (namespaces, types, methods).
		/// </summary>
		protected IntegerStack level_stack;

		/// <summary>
		/// Stack of blocks.
		/// </summary>
		IntegerStack block_stack;

		/// <summary>
		/// Total list of blocks.
		/// </summary>
		IntegerList block_list;

		/// <summary>
		/// Block counter.
		/// </summary>
		int block_count;

		/// <summary>
		/// Id of current compiled module.
		/// </summary>
		public int curr_module;

		/// <summary>
		/// Counter of temporary fields.
		/// </summary>
		int temp_count;

		/// <summary>
		/// Scanner object.
		/// </summary>
		protected BaseScanner scanner;

		/// <summary>
		/// Stack of the break labels.
		/// </summary>
		protected EntryStack BreakStack;

		/// <summary>
		/// Stack of the continue labels.
		/// </summary>
		protected EntryStack ContinueStack;

		/// <summary>
		/// Keyword list.
		/// </summary>
		protected StringList keywords;

		/// <summary>
		/// Returns true, if language is not case sensitive.
		/// </summary>
		protected bool upcase = false;

		/// <summary>
		/// Returns true, if parser must add new name to symbol table.
		/// </summary>
		protected bool DECLARE_SWITCH = false;

		/// <summary>
		/// Returns true, if parser must check declare switch.
		/// </summary>
		protected bool DECLARATION_CHECK_SWITCH = false;

		/// <summary>
		/// Returns true, if parser must process new name as field of object
		/// or class.
		/// </summary>
		protected bool REF_SWITCH = false;

		/// <summary>
		/// Name of scripting language.
		/// </summary>
		public string language;

		/// <summary>
		/// Represents current token.
		/// </summary>
		public Token curr_token;

		/// <summary>
		/// Returns 'true', if parser allows keywords in the member access expressions.
		/// </summary>
		public bool AllowKeywordsInMemberAccessExpressions = false;

		/// <summary>
		/// Constructor.
		/// </summary>
		public BaseParser()
		{
			level_stack = new IntegerStack();
			block_stack = new IntegerStack();
			block_list = new IntegerList(true);
			BreakStack = new EntryStack();
			ContinueStack = new EntryStack();
			keywords = new StringList(false);
		}

		/// <summary>
		/// Initializes parser with a new module.
		/// </summary>
		internal virtual void Init(BaseScripter scripter, Module m)
		{
			this.scripter = scripter;
			code = scripter.code;
			symbol_table = scripter.symbol_table;

			scanner.Init(scripter, m.Text);
			temp_count = 0;
			curr_module = m.NameIndex;

			level_stack.Clear();
			level_stack.Push(0);
			level_stack.Push(RootNamespaceId);

			block_count = 0;
			block_stack.Clear();
			block_stack.Push(0);

			block_list.Clear();

			DECLARE_SWITCH = false;
			DECLARATION_CHECK_SWITCH = false;
		}

		/// <summary>
		/// Initializes parser with an expression.
		/// </summary>
		internal void InitExpression(BaseScripter scripter, Module m, int sub_id, string expr)
		{
			curr_token.id = 0;
			this.scripter = scripter;
			code = scripter.code;
			symbol_table = scripter.symbol_table;

			scanner.Init(scripter, expr);
			temp_count = 0;
			curr_module = m.NameIndex;

			level_stack.Clear();
			level_stack.Push(0);
			level_stack.Push(sub_id);

			block_count = 0;
			block_stack.Clear();
			block_stack.Push(0);

			block_list.Clear();

			DECLARE_SWITCH = false;
		}

		/// <summary>
		/// Emits new line of paxScript.NET IL.
		/// </summary>
		public virtual void Gen(int op, int arg1, int arg2, int res)
		{
			if ((op == code.OP_CREATE_REFERENCE) || (op == code.OP_EVAL))
			{
				code.Card ++;
				code.SetInstruction(code.Card, code.OP_NOP, 0, 0, 0);
			}
			code.Card ++;
			code.SetInstruction(code.Card, op, arg1, arg2, res);
			if (op == code.OP_ASSIGN)
			{
				code.Card ++;
				code.SetInstruction(code.Card, code.OP_NOP, 0, 0, 0);
				code.Card ++;
				code.SetInstruction(code.Card, code.OP_NOP, 0, 0, 0);
			}
			else if (op == code.OP_ADD_PARAM)
			{
				symbol_table[arg2].Count ++;
			}
		}

		/// <summary>
		/// Removes intruction from paxScript.NET IL.
		/// </summary>
		public void DiscardInstruction(int op, int arg1, int arg2, int res)
		{
			for (int i = code.Card; i >= 1; i--)
			if (code[i].op == op &&
				(code[i].arg1 == arg1 || arg1 == -1) &&
				(code[i].arg2 == arg2 || arg2 == -1) &&
				(code[i].res == res || res == -1)
				)
			{
				code[i].op = code.OP_NOP;
				break;
			}
		}

		/// <summary>
		/// Emits new line of paxScript.NET IL at the given position.
		/// </summary>
		public void GenAt(int n, int op, int arg1, int arg2, int res)
		{
			code.SetInstruction(n, op, arg1, arg2, res);
		}

		/// <summary>
		/// Emits code.OP_CREATE_REF_TYPE operator.
		/// </summary>
		public int GenTypeRef(int type_id)
		{
			int result = NewVar();
			SetName(result, GetName(type_id) + "&");
			symbol_table[result].Level = symbol_table[type_id].Level;
			Gen(code.OP_CREATE_REF_TYPE, type_id, 0, result);
			return result;
		}

		/// <summary>
		/// Emits binary operator.
		/// </summary>
		public int BinOp(int op, int arg1, int arg2)
		{
			int result = NewVar();
			Gen(op, arg1, arg2, result);
			return result;
		}

		/// <summary>
		/// Emits unary operator.
		/// </summary>
		public int UnaryOp(int op, int arg1)
		{
			int result = NewVar();
			Gen(op, arg1, 0, result);
			return result;
		}

		/// <summary>
		/// Returns current level.
		/// </summary>
		public int CurrLevel
		{
			get
			{
				return level_stack.Peek();
			}
		}

		/// <summary>
		/// Returns current block.
		/// </summary>
		public int CurrBlock
		{
			get
			{
				return block_stack.Peek();
			}
		}

		/// <summary>
		/// Creates new temporary variable.
		/// </summary>
		public int NewVar()
		{
			int result = symbol_table.AppVar();
			symbol_table[result].Level = CurrLevel;
			symbol_table[result].Block = CurrBlock;

			temp_count ++;
			symbol_table[result].Name = "$$" + temp_count.ToString();
			return result;
		}

		/// <summary>
		/// Creates new temporary variable in the symbol table and initializes it.
		/// </summary>
		public int NewVar(object v)
		{
			int result = symbol_table.AppVar();
			symbol_table[result].Level = 0;
			symbol_table[result].Value = v;
			symbol_table[result].Kind = MemberKind.Var;

			if (v.GetType() == 1.GetType())
				symbol_table[result].TypeId = (int) StandardType.Int;
			else if (v.GetType() == "".GetType())
				symbol_table[result].TypeId = (int) StandardType.String;
			else if (v.GetType() == 0.0.GetType())
				symbol_table[result].TypeId = (int) StandardType.Double;

			temp_count ++;
			symbol_table[result].Name = "$$" + temp_count.ToString();

			return result;
		}

		/// <summary>
		/// Creates new field reference variable in the symbol table.
		/// </summary>
		public int NewRef(string name)
		{
			int result = symbol_table.AppVar();
			symbol_table[result].Name = name;
			symbol_table[result].Level = CurrLevel;
			symbol_table[result].Kind = MemberKind.Ref;
			return result;
		}

		/// <summary>
		/// Creates new constant in the symbol table.
		/// </summary>
		public int NewConst(object v)
		{
			int result = symbol_table.AppVar();
			symbol_table[result].Level = 0;
			symbol_table[result].Name = v.ToString();
			symbol_table[result].Value = v;
			symbol_table[result].Kind = MemberKind.Const;

			Type t = v.GetType();
			if (t == typeof(bool))
				symbol_table[result].TypeId = (int) StandardType.Bool;
			else if (t == typeof(byte))
				symbol_table[result].TypeId = (int) StandardType.Byte;
			else if (t == typeof(char))
				symbol_table[result].TypeId = (int) StandardType.Char;
			else if (t == typeof(decimal))
				symbol_table[result].TypeId = (int) StandardType.Decimal;
			else if (t == typeof(double))
				symbol_table[result].TypeId = (int) StandardType.Double;
			else if (t == typeof(float))
				symbol_table[result].TypeId = (int) StandardType.Float;
			else if (t == typeof(int))
				symbol_table[result].TypeId = (int) StandardType.Int;
			else if (t == typeof(long))
				symbol_table[result].TypeId = (int) StandardType.Long;
			else if (t == typeof(sbyte))
				symbol_table[result].TypeId = (int) StandardType.Sbyte;
			else if (t == typeof(short))
				symbol_table[result].TypeId = (int) StandardType.Short;
			else if (t == typeof(string))
				symbol_table[result].TypeId = (int) StandardType.String;
			else if (t == typeof(uint))
				symbol_table[result].TypeId = (int) StandardType.Uint;
			else if (t == typeof(ulong))
				symbol_table[result].TypeId = (int) StandardType.Ulong;
			else if (t == typeof(ushort))
				symbol_table[result].TypeId = (int) StandardType.Ushort;
			else
				symbol_table[result].TypeId = (int) StandardType.Object;

			return result;
		}

		/// <summary>
		/// Creates new label in the symbol table.
		/// </summary>
		public int NewLabel()
		{
			return symbol_table.AppLabel();
		}

		/// <summary>
		/// Sets up label with the current emitted line of IL.
		/// </summary>
		public void SetLabelHere(int l)
		{
			if (code[code.Card].op == code.OP_EVAL)
			{
				symbol_table.SetLabel(l, code.Card);
				int i = code.Card;
				int id = code[i].res;
				code[i].op = code.OP_LABEL;
				code.Card++;
				i = code.Card;
				code[i].op = code.OP_EVAL;
				code[i].res = id;
			}
			else
			{
				Gen(code.OP_LABEL, 0, 0, 0);
				symbol_table.SetLabel(l, code.Card);
			}
		}

		/// <summary>
		/// Returns true if current token represents an operator from
		/// the oper_list.
		/// </summary>
		public bool IsOperator(IntegerList oper_list, out int op)
		{
			op = 0;
			int i = oper_list.IndexOf(curr_token.id);
			if (i >= 0)
			{
				op = oper_list[i];
				Call_SCANNER();
				return true;
			}
			else
			  return false;
		}

		/// <summary>
		/// Raises compile-time error.
		/// </summary>
		public void RaiseError(bool fatal, string message)
		{
			scripter.Dump();

			scripter.CreateErrorObject(message);
			if (fatal)
			{
				code.n = code.Card;
				scripter.RaiseException(message);
			}
		}

		/// <summary>
		/// Raises compile-time error.
		/// </summary>
		public void RaiseErrorEx(bool fatal, string message, params object[] p)
		{
			RaiseError(fatal, String.Format(message, p));
		}

		/// <summary>
		/// Matches current token with a target string. If success - reads new
		/// token. If not success - calls RaiseError.
		/// </summary>
		public virtual void Match(string s)
		{
			if (upcase)
			{
				if (s.ToUpper() != curr_token.Text.ToUpper())
					RaiseErrorEx(true, Errors.EXPECTED, s, curr_token.Text);
			}
			else
			{
				if (s != curr_token.Text)
					RaiseErrorEx(true, Errors.EXPECTED, s, curr_token.Text);
			}

			Call_SCANNER();
		}

		/// <summary>
		/// Matches current token with a target char. If success - reads new
		/// token. If not success - calls RaiseError.
		/// </summary>
		public virtual void Match(char c)
		{
			if (c != curr_token.Char)
				RaiseErrorEx(true, Errors.EXPECTED, c.ToString(), curr_token.Text);
			Call_SCANNER();
		}

		/// <summary>
		/// Matches current token with a target char. If success - returns
		/// 'true' and reads new token. If not success - returns false.
		/// </summary>
		public bool CondMatch(char c)
		{
			if (c == curr_token.Char)
			{
				Call_SCANNER();
				return true;
			}
			else
				return false;
		}

		/// <summary>
		/// Returns 'true', if s is a keyword.
		/// </summary>
		public virtual bool IsKeyword(string s)
		{
			if (upcase)
			{
				string up_s = s.ToUpper();
				for (int i = 0; i < keywords.Count; i++)
				{
					if (keywords[i].ToUpper() == up_s)
						return true;
				}
				return false;
			}
			else
				return keywords.IndexOf(s) != -1;
		}

		/// <summary>
		/// Parses identifier.
		/// </summary>
		public virtual int Parse_Ident()
		{
			if (curr_token.tokenClass != TokenClass.Identifier)
			{
				if (curr_token.id == 0)
					if (curr_token.Text != "void")
						// Identifier expected
						RaiseError(true, Errors.CS1001);
			}

			int result = curr_token.id;
			Call_SCANNER();
			return result;
		}

		/// <summary>
		/// Parses label.
		/// </summary>
		public int Parse_NewLabel()
		{
			int result = Parse_Ident();
			symbol_table[result].Kind = MemberKind.Label;
			return result;
		}

		/// <summary>
		/// Restore scanner position by returning k last tokens.
		/// </summary>
		public void Backup_SCANNER(int k)
		{
			for (int i=0; i < k; i++)
				scanner.BackUp();
			int line_number = scanner.LineNumber;
			while (code[code.Card].op == code.OP_SEPARATOR && code[code.Card].arg2 > line_number)
			{
				code.Card --;
			}
		}

		/// <summary>
		/// Read new token without processing.
		/// </summary>
		public int ReadToken()
		{
			int result = 0;
			for (;;)
			{
				result ++;
				curr_token = scanner.ReadToken();
				if (curr_token.tokenClass != TokenClass.Separator)
					break;
			}
			curr_token.atext = curr_token.Text;
			return result;
		}

		/// <summary>
		/// Emits line separator.
		/// </summary>
		public void GenSeparator()
		{
			ProgRec r = code[code.Card];
			if (! (r.arg2 == scanner.LineNumber && r.op == code.OP_SEPARATOR))
				Gen(code.OP_SEPARATOR, curr_module, scanner.LineNumber, 0);
		}

		/// <summary>
		/// Emits #region directive
		/// </summary>
		public void GenStartRegion(string name)
		{
			int id = symbol_table.AppConst(name, name, StandardType.String);
			Gen(code.OP_START_REGION, id, 0, 0);
		}

		/// <summary>
		/// Emits #endregion directive
		/// </summary>
		public void GenEndRegion(string name)
		{
			int id = symbol_table.AppConst(name, name, StandardType.String);
			Gen(code.OP_END_REGION, id, 0, 0);
		}

		/// <summary>
		/// Emits #define directive
		/// </summary>
		public void GenDefine(string name)
		{
			int id = symbol_table.AppConst(name, name, StandardType.String);
			Gen(code.OP_DEFINE, id, 0, 0);
		}

		/// <summary>
		/// Emits #undef directive
		/// </summary>
		public void GenUndef(string name)
		{
			int id = symbol_table.AppConst(name, name, StandardType.String);
			Gen(code.OP_UNDEF, id, 0, 0);
		}

		/// <summary>
		/// Returns 'true', if id represents a variable declared in
		/// a method with 'sub_id' id.
		/// </summary>
		bool IsDeclaredLocalVar(int id, int sub_id)
		{
			for (int i= code.Card; i >= 1; i--)
				if (code[i].op == code.OP_DECLARE_LOCAL_VARIABLE)
				{
					if (code[i].arg1 == id && code[i].arg2 == sub_id)
						return true;
				}
				else if (code[i].op == code.OP_DECLARE_LOCAL_VARIABLE_RUNTIME)
				{
					if (code[i].arg1 == id && code[i].arg2 == sub_id)
						return true;
				}
				else if ((code[i].op == code.OP_ADD_PARAM) || (code[i].op == code.OP_ADD_PARAMS))
				{
					if ((code[i].arg2 == id) && (code[i].arg1 == sub_id))
						return true;
				}
				else if (code[i].op == code.OP_DECLARE_LOCAL_SIMPLE)
				{
					if (code[i].arg1 == id)
						return true;
				}
				else if (code[i].op == code.OP_CREATE_METHOD)
				{
					if (code[i].arg1 == sub_id)
						return false;
				}
			return false;
		}

		/// <summary>
		/// Reads new token.
		/// </summary>
		public virtual void Call_SCANNER()
		{
			curr_token = scanner.ReadToken();
			curr_token.atext = curr_token.Text;

			switch (curr_token.tokenClass)
			{
				case TokenClass.Identifier:
				{
					if (IsKeyword(curr_token.Text))
					{
						if (REF_SWITCH && AllowKeywordsInMemberAccessExpressions)
						{
							curr_token.id = NewRef(curr_token.Text);
							REF_SWITCH = false;
						}
						else
						{
							curr_token.id = 0;
							curr_token.tokenClass = TokenClass.Keyword;
						}
					}
					else
					{
						if (REF_SWITCH)
						{
							curr_token.id = NewRef(curr_token.Text);
							REF_SWITCH = false;
						}
						else if (DECLARE_SWITCH)
						{
							if (DECLARATION_CHECK_SWITCH)
							{
								int id = LookupID(curr_token.Text);
								int block_id = symbol_table[id].Block;
								if ((id != 0) && (block_id != 0))
									if (symbol_table[id].Level == CurrLevel)
									{
										bool ok = false;
										for (int i = block_list.Count - 1; i >= 0; i--)
										{
											int temp_block = block_list[i];
											if ((temp_block < block_id) &&
												(temp_block < CurrBlock))
											{
												ok = true;
												break;
											}

											if (temp_block == block_id)
												break;
										}

										if ((!ok) && (language != "VB"))
										{
											if (CurrBlock == block_id)
											{
												// A local variable named 'variable' is already defined in this scope
												RaiseErrorEx(false, Errors.CS0128, curr_token.Text);
											}
											else if (CurrBlock > block_id)
											{
												// A local variable named 'var' cannot be declared in this scope
												RaiseErrorEx(false, Errors.CS0136,
													curr_token.Text, "parent or current");
											}
											else
											{
												// A local variable named 'var' cannot be declared in this scope
												RaiseErrorEx(false, Errors.CS0136,
													curr_token.Text, "child");
											}
										}
									}

							}

							curr_token.id = symbol_table.AppVar();
							SymbolRec s = symbol_table[curr_token.id];
							s.Name = curr_token.Text;
							s.Level = CurrLevel;
							s.Block = CurrBlock;
						}
						else
						{
							curr_token.id = LookupID(curr_token.Text);

							if (curr_token.id != 0)
							{
								int l = symbol_table[curr_token.id].Level;
								MemberKind k = symbol_table[l].Kind;
								if (k == MemberKind.Type)
								{
									if (CurrLevel != 0)
										curr_token.id = 0;
								}
								else if (!IsDeclaredLocalVar(curr_token.id, CurrLevel))
									curr_token.id = 0;
							}

							if (curr_token.id == 0)
							{
								curr_token.id = symbol_table.AppVar();
								SymbolRec s = symbol_table[curr_token.id];
								s.Name = curr_token.Text;
								s.Level = CurrLevel;
								s.Block = CurrBlock;
								s.Kind = MemberKind.Var;
								Gen(code.OP_EVAL, 0, 0, curr_token.id);
							}
						}
					}
					break;
				}
				case TokenClass.IntegerConst:
				{
					string s = curr_token.Text;
					if ((PaxSystem.PosCh('u', s) >= 0) || (PaxSystem.PosCh('U', s) >= 0))
					{
						if ((PaxSystem.PosCh('l', s) >= 0) || (PaxSystem.PosCh('L', s) >= 0))
						{
							ulong val;
							if ((PaxSystem.PosCh('x', s) >= 0) || (PaxSystem.PosCh('X', s) >= 0))
							{
								s = s.Substring(2);
								val = ulong.Parse(s.Substring(0, s.Length - 2), System.Globalization.NumberStyles.AllowHexSpecifier);
							}
							else
								val = ulong.Parse(s.Substring(0, s.Length - 2));
							curr_token.id = symbol_table.AppConst(curr_token.Text, val,
														StandardType.Ulong);
						}
						else
						{
							uint val;
							if ((PaxSystem.PosCh('x', s) >= 0) || (PaxSystem.PosCh('X', s) >= 0))
							{
								s = s.Substring(2);
								val = uint.Parse(s.Substring(0, s.Length - 1), System.Globalization.NumberStyles.AllowHexSpecifier);
							}
							else
								val = uint.Parse(s.Substring(0, s.Length - 1));
							curr_token.id = symbol_table.AppConst(curr_token.Text, val,
														StandardType.Uint);
						}
					}
					else if ((PaxSystem.PosCh('l', s) >= 0) || (PaxSystem.PosCh('L', s) >= 0))
					{
						if ((PaxSystem.PosCh('u', s) >= 0) || (PaxSystem.PosCh('U', s) >= 0))
						{
							ulong val;
							if ((PaxSystem.PosCh('x', s) >= 0) || (PaxSystem.PosCh('X', s) >= 0))
							{
								s = s.Substring(2);
								val = ulong.Parse(s.Substring(0, s.Length - 2), System.Globalization.NumberStyles.AllowHexSpecifier);
							}
							else
								val = ulong.Parse(s.Substring(0, s.Length - 2));
							curr_token.id = symbol_table.AppConst(curr_token.Text, val,
														StandardType.Ulong);
						}
						else
						{
							long val;
							if ((PaxSystem.PosCh('x', s) >= 0) || (PaxSystem.PosCh('X', s) >= 0))
							{
								s = s.Substring(2);
								val = long.Parse(s.Substring(0, s.Length - 1), System.Globalization.NumberStyles.AllowHexSpecifier);
							}
							else
								val = long.Parse(s.Substring(0, s.Length - 1));
							curr_token.id = symbol_table.AppConst(curr_token.Text, val,
														StandardType.Long);
						}
					}
					else
					{
						ulong val;

						if ((PaxSystem.PosCh('x', s) >= 0) || (PaxSystem.PosCh('X', s) >= 0))
						{
							s = s.Substring(2);
							val = ulong.Parse(s, System.Globalization.NumberStyles.AllowHexSpecifier);
						}
						else
							val = ulong.Parse(s);
						if (val <= (ulong) int.MaxValue)
						{
							curr_token.id = symbol_table.AppConst(curr_token.Text, (int) val,
														StandardType.Int);
						}
						else  if (val <= (uint) uint.MaxValue)
						{
							curr_token.id = symbol_table.AppConst(curr_token.Text, (uint) val,
														StandardType.Uint);
						}
						else  if (val <= (long) long.MaxValue)
						{
							curr_token.id = symbol_table.AppConst(curr_token.Text, (long) val,
														StandardType.Long);
						}
						else
						{
							curr_token.id = symbol_table.AppConst(curr_token.Text, val,
														StandardType.Ulong);
						}
					}
					break;
				}
				case TokenClass.RealConst:
				{
					string s = curr_token.Text;
					if ((PaxSystem.PosCh(SingleCharacter, s) >= 0) || (PaxSystem.PosCh(UpSingleCharacter, s) >= 0))
					{
						s = s.Substring(0, s.Length - 1);
						if (scanner.DecimalSeparator == ",")
							s = s.Replace('.', ',');
						float val = float.Parse(s, System.Globalization.NumberStyles.Any);
						curr_token.id = symbol_table.AppConst(curr_token.Text, val,
														StandardType.Float);
					}
					else if ((PaxSystem.PosCh(DoubleCharacter, s) >= 0) || (PaxSystem.PosCh(UpDoubleCharacter, s) >= 0))
					{
						s = s.Substring(0, s.Length - 1);
						if (scanner.DecimalSeparator == ",")
							s = s.Replace('.', ',');

						double val = double.Parse(s, System.Globalization.NumberStyles.Any);
						curr_token.id = symbol_table.AppConst(curr_token.Text, val,
														StandardType.Double);
					}
					else if ((PaxSystem.PosCh(DecimalCharacter, s) >= 0) || (PaxSystem.PosCh(UpDecimalCharacter, s) >= 0))
					{
						s = s.Substring(0, s.Length - 1);
						if (scanner.DecimalSeparator == ",")
							s = s.Replace('.', ',');

						decimal val = decimal.Parse(s, System.Globalization.NumberStyles.Any);
						curr_token.id = symbol_table.AppConst(curr_token.Text, val,
														StandardType.Decimal);
					}
					else
					{
						if (scanner.DecimalSeparator == ",")
							s = s.Replace('.', ',');
						double val = double.Parse(s);
						curr_token.id = symbol_table.AppConst(curr_token.Text, val,
														StandardType.Double);
					}
					break;
				}
				case TokenClass.CharacterConst:
				{
					string s = scanner.ParseString(curr_token.Text);
					char c = s[1];
					curr_token.id = symbol_table.AppCharacterConst(c);
					break;
				}
				case TokenClass.StringConst:
				{
					if (PaxSystem.PosCh('@', curr_token.Text) == 0 && language != "VB")
					{
						string s = scanner.ParseVerbatimString(curr_token.Text);
						curr_token.id = symbol_table.AppConst(curr_token.Text,
													s.Substring(1, s.Length - 2),
													StandardType.String);
					}
					else
					{
						string s = scanner.ParseString(curr_token.Text);
						curr_token.id = symbol_table.AppConst(curr_token.Text,
													s.Substring(1, s.Length - 2),
													StandardType.String);
					}
					break;
				}
			}
		}

		/// <summary>
		/// Performs search of id of a local variable.
		/// </summary>
		public int LookupID(string s)
		{
			return symbol_table.LookupIDLocal(s, CurrLevel, upcase);
/*
			int result = 0;
			for (int i=level_stack.Count - 1; i > 0; i--)
			{
				result = symbol_table.LookupID(s, level_stack[i], upcase);
				if (result != 0)
					break;
			}
			return result;
*/
		}

		public int LookupTypeID(string s)
		{
			return symbol_table.LookupTypeByName(s, upcase);
		}

		/// <summary>
		/// Returns id of variable which represents current 'this'.
		/// </summary>
		public int CurrThisID
		{
			get
			{
				for (int i=level_stack.Count - 1; i>=0; i--)
				{
					int l = level_stack[i];
					MemberKind k = GetKind(l);
					if ((k == MemberKind.Method)||(k == MemberKind.Constructor)||
						(k == MemberKind.Destructor))
						return (l+3);
				}
				return 0;
			}
		}

		/// <summary>
		/// Returns id of variable which represents current parsed class.
		/// </summary>
		public int CurrClassID
		{
			get
			{
				for (int i=level_stack.Count - 1; i>=0; i--)
				{
					int l = level_stack[i];
					MemberKind k = GetKind(l);
					if (k == MemberKind.Type)
						return l;
				}
				return 0;
			}
		}

		/// <summary>
		/// Returns id of variable which represents current parsed method.
		/// </summary>
		public int CurrSubId
		{
			get
			{
				for (int i=level_stack.Count - 1; i>=0; i--)
				{
					int l = level_stack[i];
					MemberKind k = GetKind(l);
					if (k != MemberKind.Type)
						return l;
				}
				return 0;
			}
		}

		/// <summary>
		/// Returns id of result of current parsed method.
		/// </summary>
		public int CurrResultId
		{
			get
			{
				return symbol_table.GetResultId(CurrSubId);
			}
		}

		/// <summary>
		/// Returns 'true' if current token is equal s.
		/// </summary>
		public bool IsCurrText(string s)
		{
			if (upcase)
				return curr_token.Text.ToUpper() == s.ToUpper();
			else
			{
				if (s[0] == curr_token.Char)
					return curr_token.Text == s;
				else
					return false;
			}
		}

		/// <summary>
		/// Returns 'true' if current token is equal c.
		/// </summary>
		public bool IsCurrText(char c)
		{
			return curr_token.Char == c && curr_token.Len == 1;
		}

		/// <summary>
		/// Returns 'true' if current token is an identifier.
		/// </summary>
		public bool IsIdentifier()
		{
			return curr_token.tokenClass == TokenClass.Identifier;
		}

		/// <summary>
		/// Returns 'true' if current token is a constant.
		/// </summary>
		public bool IsConstant()
		{
			return (curr_token.tokenClass == TokenClass.CharacterConst) ||
				   (curr_token.tokenClass == TokenClass.IntegerConst) ||
				   (curr_token.tokenClass == TokenClass.RealConst) ||
				   (curr_token.tokenClass == TokenClass.BooleanConst) ||
				   (curr_token.tokenClass == TokenClass.StringConst);
		}

		/// <summary>
		/// Returns id of a variable which keeps result of method with 'sub_id'
		/// id.
		/// </summary>
		public int GetResultId(int sub_id)
		{
			return symbol_table.GetResultId(sub_id);
		}

		/// <summary>
		/// Returns id of 'this' varible of method with 'sub_id' id
		/// </summary>
		public int GetThisId(int sub_id)
		{
			return symbol_table.GetThisId(sub_id);
		}

		/// <summary>
		/// Returns name of id
		/// </summary>
		public string GetName(int id)
		{
			return symbol_table[id].Name;
		}

		/// <summary>
		/// Assigns name of id
		/// </summary>
		public void SetName(int id, string value)
		{
			symbol_table[id].Name = value;
		}

		/// <summary>
		/// Returns id of type of given id.
		/// </summary>
		public int GetTypeId(int id)
		{
			return symbol_table[id].TypeId;
		}

		/// <summary>
		/// Assigns id of type of given id.
		/// </summary>
		public void SetTypeId(int id, int value)
		{
			symbol_table[id].TypeId = value;
		}

		/// <summary>
		/// Returns kind of given id.
		/// </summary>
		public MemberKind GetKind(int id)
		{
			return symbol_table[id].Kind;
		}

		/// <summary>
		/// Assigns kind of given id.
		/// </summary>
		public void PutKind(int id, MemberKind value)
		{
			symbol_table[id].Kind = value;
		}

		public void SetForward(int sub_id, bool value)
		{
			symbol_table[sub_id].IsForward = value;
		}

		/// <summary>
		/// Returns value of id.
		/// </summary>
		public object GetVal(int id)
		{
			return symbol_table[id].Val;
		}

		/// <summary>
		/// Assigns value to id.
		/// </summary>
		public void PutVal(int id, object value)
		{
			symbol_table[id].Val = value;
		}

		/// <summary>
		/// Returns level of id.
		/// </summary>
		public int GetLevel(int id)
		{
			return symbol_table[id].Level;
		}

		/// <summary>
		/// Assigns level to id.
		/// </summary>
		public void PutLevel(int id, int value)
		{
			symbol_table[id].Level = value;
		}

		/// <summary>
		/// Returns number of p-code instructions.
		/// </summary>
		public int CodeCard
		{
			get
			{
				return code.Card;
			}
			set
			{
				code.Card = value;
			}
		}

		/// <summary>
		/// Returns last (current) instruction of p-code.
		/// </summary>
		public ProgRec TopInstruction
		{
			get
			{
				int i = code.Card;
				while (code[i].op == code.OP_SEPARATOR)
					i = i - 1;
				return code[i];
			}
		}

		/// <summary>
		/// Returns id of 'true' constant.
		/// </summary>
		public int TRUE_id
		{
			get
			{
				return symbol_table.TRUE_id;
			}
		}

		/// <summary>
		/// Returns id of 'false' constant.
		/// </summary>
		public int FALSE_id
		{
			get
			{
				return symbol_table.FALSE_id;
			}
		}

		/// <summary>
		/// Returns id of BR constant.
		/// </summary>
		public int BR_id
		{
			get
			{
				return symbol_table.BR_id;
			}
		}

		/// <summary>
		/// Returns id of 'null'.
		/// </summary>
		public int NULL_id
		{
			get
			{
				return symbol_table.NULL_id;
			}
		}

		public int DATETIME_CLASS_id
		{
			get
			{
				return symbol_table.DATETIME_CLASS_id;
			}
		}

		/// <summary>
		/// Returns id of root (noname) namespace.
		/// </summary>
		public int RootNamespaceId
		{
			get
			{
				return symbol_table.ROOT_NAMESPACE_id;
			}
		}

		/// <summary>
		/// Returns id of 'System' namespace.
		/// </summary>
		public int SystemNamespaceId
		{
			get
			{
				return symbol_table.SYSTEM_NAMESPACE_id;
			}
		}

		/// <summary>
		/// Returns id of 'System.Object' class.
		/// </summary>
		public int ObjectClassId
		{
			get
			{
				return symbol_table.OBJECT_CLASS_id;
			}
		}

		/// <summary>
		/// Returns id of 'System.Object[]' class.
		/// </summary>
		public int ArrayOfObjectClassId
		{
			get
			{
				return symbol_table.ARRAY_OF_OBJECT_CLASS_id;
			}
		}

		/// <summary>
		/// Returns id of 'System.ValueType' class.
		/// </summary>
		public int ValueTypeClassId
		{
			get
			{
				return symbol_table.VALUETYPE_CLASS_id;
			}
		}

		/// <summary>
		/// Parses an expression.
		/// </summary>
		public virtual int Parse_Expression()
		{
			return Parse_Ident();
		}

		/// <summary>
		/// Parses 'println statement'.
		/// </summary>
		public void Parse_PrintlnStmt()
		{
			Parse_PrintStmt();
			Gen(code.OP_PRINT, symbol_table.BR_id, 0, 0);
		}

		/// <summary>
		/// Parses 'print statement'.
		/// </summary>
		public void Parse_PrintStmt()
		{
			Call_SCANNER();
			for (;;)
			{
					Gen(code.OP_PRINT, Parse_Expression(), 0, 0);
					if (IsCurrText(","))
						Call_SCANNER();
					else
						break;
			}
			Match(";");
		}

		/// <summary>
		/// Emits beginning of namespace declaration.
		/// </summary>
		public void BeginNamespace(int namespace_id)
		{
			int owner_id = symbol_table[namespace_id].Level;
			symbol_table[namespace_id].Kind = MemberKind.Type;
			level_stack.Push(namespace_id);
			Gen(code.OP_CREATE_NAMESPACE, namespace_id, owner_id, (int) ClassKind.Namespace);
			Gen(code.OP_ADD_MODIFIER, namespace_id, (int) Modifier.Static, 0);
			Gen(code.OP_BEGIN_USING, namespace_id, 0, 0);
		}

		/// <summary>
		/// Emits end of namespace declaration.
		/// </summary>
		public void EndNamespace(int namespace_id)
		{
			level_stack.Pop();
			Gen(code.OP_END_USING, namespace_id, 0, 0);
		}

		/// <summary>
		/// Emits beginning of class declaration.
		/// </summary>
		public void BeginClass(int class_id, ModifierList ml)
		{
			int owner_id = symbol_table[class_id].Level;
			symbol_table[class_id].Kind = MemberKind.Type;
			level_stack.Push(class_id);
			Gen(code.OP_CREATE_CLASS, class_id, owner_id, (int) ClassKind.Class);
			Gen(code.OP_ADD_MODIFIER, class_id, (int) Modifier.Static, 0);
			for (int i = 0; i < ml.Count; i++)
				Gen(code.OP_ADD_MODIFIER, class_id, (int) ml[i], 0);
			Gen(code.OP_BEGIN_USING, class_id, 0, 0);
		}

		/// <summary>
		/// Emits end of class declaration.
		/// </summary>
		public void EndClass(int class_id)
		{
			level_stack.Pop();
			Gen(code.OP_END_USING, class_id, 0, 0);
			Gen(code.OP_END_CLASS, class_id, 0, 0);
		}

		/// <summary>
		/// Emits beginning of struct declaration.
		/// </summary>
		public void BeginStruct(int struct_id, ModifierList ml)
		{
			int owner_id = symbol_table[struct_id].Level;
			symbol_table[struct_id].Kind = MemberKind.Type;
			level_stack.Push(struct_id);
			Gen(code.OP_CREATE_CLASS, struct_id, owner_id, (int) ClassKind.Struct);
			Gen(code.OP_ADD_MODIFIER, struct_id, (int) Modifier.Static, 0);
			Gen(code.OP_ADD_MODIFIER, struct_id, (int) Modifier.Sealed, 0);
			for (int i = 0; i < ml.Count; i++)
				Gen(code.OP_ADD_MODIFIER, struct_id, (int) ml[i], 0);
			Gen(code.OP_BEGIN_USING, struct_id, 0, 0);
		}

		/// <summary>
		/// Emits end of struct declaration.
		/// </summary>
		public void EndStruct(int struct_id)
		{
			level_stack.Pop();
			Gen(code.OP_END_USING, struct_id, 0, 0);
			Gen(code.OP_END_CLASS, struct_id, 0, 0);
		}

		/// <summary>
		/// Emits beginning of array declaration.
		/// </summary>
		public void BeginArray(int array_id, ModifierList ml)
		{
			int owner_id = symbol_table[array_id].Level;
			symbol_table[array_id].Kind = MemberKind.Type;
			level_stack.Push(array_id);
			Gen(code.OP_CREATE_CLASS, array_id, owner_id, (int) ClassKind.Struct);
			Gen(code.OP_ADD_ANCESTOR, array_id, ObjectClassId, 0);
			Gen(code.OP_ADD_MODIFIER, array_id, (int) Modifier.Static, 0);
			Gen(code.OP_ADD_MODIFIER, array_id, (int) Modifier.Sealed, 0);
			Gen(code.OP_ADD_MODIFIER, array_id, (int) Modifier.Public, 0);
			for (int i = 0; i < ml.Count; i++)
				Gen(code.OP_ADD_MODIFIER, array_id, (int) ml[i], 0);
			Gen(code.OP_BEGIN_USING, array_id, 0, 0);
		}

		/// <summary>
		/// Emits end of array declaration.
		/// </summary>
		public void EndArray(int array_id)
		{
			level_stack.Pop();
			Gen(code.OP_END_USING, array_id, 0, 0);
			Gen(code.OP_END_CLASS, array_id, 0, 0);
		}

		/// <summary>
		/// Emits beginning of subrange declaration.
		/// </summary>
		public void BeginSubrange(int type_id, StandardType type_base)
		{
			int owner_id = symbol_table[type_id].Level;
			symbol_table[type_id].Kind = MemberKind.Type;
			level_stack.Push(type_id);
			Gen(code.OP_CREATE_CLASS, type_id, owner_id, (int) ClassKind.Subrange);
			Gen(code.OP_ADD_ANCESTOR, type_id, (int) type_base, 0);
			Gen(code.OP_ADD_MODIFIER, type_id, (int) Modifier.Static, 0);
			Gen(code.OP_ADD_MODIFIER, type_id, (int) Modifier.Sealed, 0);
			Gen(code.OP_ADD_MODIFIER, type_id, (int) Modifier.Public, 0);
			Gen(code.OP_BEGIN_USING, type_id, 0, 0);
		}

		/// <summary>
		/// Emits end of subrange declaration.
		/// </summary>
		public void EndSubrange(int type_id)
		{
			level_stack.Pop();
			Gen(code.OP_END_USING, type_id, 0, 0);
			Gen(code.OP_END_CLASS, type_id, 0, 0);
		}

		/// <summary>
		/// Emits beginning of interface declaration.
		/// </summary>
		public void BeginInterface(int interface_id, ModifierList ml)
		{
			int owner_id = symbol_table[interface_id].Level;
			symbol_table[interface_id].Kind = MemberKind.Type;
			level_stack.Push(interface_id);
			Gen(code.OP_CREATE_CLASS, interface_id, owner_id, (int) ClassKind.Interface);
			Gen(code.OP_ADD_MODIFIER, interface_id, (int) Modifier.Static, 0);
			for (int i = 0; i < ml.Count; i++)
				Gen(code.OP_ADD_MODIFIER, interface_id, (int) ml[i], 0);
			Gen(code.OP_BEGIN_USING, interface_id, 0, 0);
		}

		/// <summary>
		/// Emits end of interface declaration.
		/// </summary>
		public void EndInterface(int interface_id)
		{
			level_stack.Pop();
			Gen(code.OP_END_USING, interface_id, 0, 0);
			Gen(code.OP_END_CLASS, interface_id, 0, 0);
		}

		/// <summary>
		/// Emits beginning of enum declaration.
		/// </summary>
		public void BeginEnum(int enum_id, ModifierList ml, int type_base)
		{
			int owner_id = symbol_table[enum_id].Level;
			symbol_table[enum_id].Kind = MemberKind.Type;
			level_stack.Push(enum_id);
			Gen(code.OP_CREATE_CLASS, enum_id, owner_id, (int) ClassKind.Enum);
			Gen(code.OP_ADD_MODIFIER, enum_id, (int) Modifier.Sealed, 0);
			for (int i = 0; i < ml.Count; i++)
				Gen(code.OP_ADD_MODIFIER, enum_id, (int) ml[i], 0);
			Gen(code.OP_BEGIN_USING, enum_id, 0, 0);
		}

		/// <summary>
		/// Emits end of enum declaration.
		/// </summary>
		public void EndEnum(int enum_id)
		{
			level_stack.Pop();
			Gen(code.OP_END_USING, enum_id, 0, 0);
			Gen(code.OP_END_CLASS, enum_id, 0, 0);
		}

		/// <summary>
		/// Emits beginning of delegate declaration.
		/// </summary>
		public void BeginDelegate(int delegate_id, ModifierList ml)
		{
			int owner_id = symbol_table[delegate_id].Level;
			symbol_table[delegate_id].Kind = MemberKind.Type;
			level_stack.Push(delegate_id);
			Gen(code.OP_CREATE_CLASS, delegate_id, owner_id, (int) ClassKind.Delegate);
			Gen(code.OP_ADD_MODIFIER, delegate_id, (int) Modifier.Static, 0);
			Gen(code.OP_ADD_MODIFIER, delegate_id, (int) Modifier.Sealed, 0);
			for (int i = 0; i < ml.Count; i++)
				Gen(code.OP_ADD_MODIFIER, delegate_id, (int) ml[i], 0);
			Gen(code.OP_BEGIN_USING, delegate_id, 0, 0);
		}

		/// <summary>
		/// Emits end of delegate declaration.
		/// </summary>
		public void EndDelegate(int delegate_id)
		{
			level_stack.Pop();
			Gen(code.OP_END_USING, delegate_id, 0, 0);
			Gen(code.OP_END_CLASS, delegate_id, 0, 0);
		}

		/// <summary>
		/// Emits beginning of field declaration.
		/// </summary>
		public void BeginField(int field_id, ModifierList ml, int type_id)
		{
			int owner_id = symbol_table[field_id].Level;
			symbol_table[field_id].Kind = MemberKind.Field;
			Gen(code.OP_CREATE_FIELD, field_id, owner_id, 0);
			for (int i = 0; i < ml.Count; i++)
				Gen(code.OP_ADD_MODIFIER, field_id, (int) ml[i], 0);
			Gen(code.OP_ASSIGN_TYPE, field_id, type_id, 0);
		}

		/// <summary>
		/// Emits end of field declaration.
		/// </summary>
		public void EndField(int field_id)
		{
		}

		/// <summary>
		/// Emits beginning of method declaration.
		/// </summary>
		public virtual int BeginMethod(int sub_id, MemberKind k, ModifierList ml, int res_type_id)
		{
			int owner_id = symbol_table[sub_id].Level;
			symbol_table[sub_id].Kind = k;
			NewLabel();
			level_stack.Push(sub_id);
			int res_id = NewVar();

			Gen(code.OP_ASSIGN_TYPE, res_id, res_type_id, 0);
			Gen(code.OP_ASSIGN_TYPE, sub_id, res_type_id, 0);

			int this_id = NewVar();

			if (!ml.HasModifier(Modifier.Static))
			{
				symbol_table[this_id].TypeId = owner_id;
				symbol_table[this_id].Name = "this";
			}
			Gen(code.OP_CREATE_METHOD, sub_id, owner_id, 0);
			for (int i = 0; i < ml.Count; i++)
				Gen(code.OP_ADD_MODIFIER, sub_id, (int) ml[i], 0);

			Gen(code.OP_ASSIGN_TYPE, sub_id, res_type_id, 0);

			BeginBlock();

			return sub_id;
		}

		/// <summary>
		/// Emits beginning of method's statement list.
		/// </summary>
		public virtual void InitMethod(int sub_id)
		{
			int lg = sub_id + 1;
			Gen(code.OP_INIT_METHOD, sub_id, 0, 0);
			Gen(code.OP_GO, lg, 0, 0);
			Gen(code.OP_LABEL, 0, 0, 0);
		}

		/// <summary>
		/// Emits end of method declaration.
		/// </summary>
		public virtual void EndMethod(int sub_id)
		{
			Gen(code.OP_END_METHOD, sub_id, symbol_table.Card, 0);
			int lg = sub_id + 1;
			Gen(code.OP_RET, sub_id, 0, 0);
			level_stack.Pop();
			SetLabelHere(lg);

			EndBlock();
		}

		SymbolRec GetSymbolRec(int id)
		{
			return symbol_table[id];
		}

		public void ReplaceForwardDeclaration(int id)
		{
			int forward_id = symbol_table.LookupForwardDeclaration(id, upcase);

			if (forward_id == 0)
				return;

			int owner_id = GetSymbolRec(symbol_table.GetThisId(forward_id)).TypeId;

			int i;
			for (i = 1; i <= code.Card; i++)
			{
				if (code[i].op == code.OP_CREATE_METHOD && forward_id == code[i].arg1)
					break;
			}

			bool is_static = false;

			i--;
			for (;;)
			{
				i++;

				if (code[i].op == code.OP_ADD_MODIFIER && forward_id == code[i].arg1)
				{
					if (code[i].arg2 == (int) Modifier.Static)
						is_static = true;
				}

				if (code[i].op == code.OP_END_METHOD && forward_id == code[i].arg1)
				{
					code[i].op = code.OP_NOP;
					break;
				}
				else
				{
					if (code[i].op != code.OP_SEPARATOR)
						code[i].op = code.OP_NOP;
				}
			}
			code[i + 1].op = code.OP_NOP;

			symbol_table[forward_id].Kind = MemberKind.None;
			symbol_table[forward_id].Name = "";
			symbol_table[forward_id + 1].Kind = MemberKind.None;
			symbol_table[forward_id + 1].Name = "";
			symbol_table[forward_id + 2].Kind = MemberKind.None;
			symbol_table[forward_id + 2].Name = "";
			symbol_table[forward_id + 3].Kind = MemberKind.None;
			symbol_table[forward_id + 3].Name = "";

			code.ReplaceId(forward_id, id);

			if (!is_static)
			{
				for (i = code.Card; i >= 1; i--)
				{
					if (code[i].op == code.OP_ADD_MODIFIER && id == code[i].arg1)
					{
						if (code[i].arg2 == (int) Modifier.Static)
						{
							code[i].op = code.OP_NOP;
							break;
						}
					}
				}
				GetSymbolRec(symbol_table.GetThisId(id)).TypeId = owner_id;
				GetSymbolRec(symbol_table.GetThisId(id)).Name = "this";
			}

			Gen(code.OP_ADD_MODIFIER, id, (int) Modifier.Public, 0);
		}

		/// <summary>
		/// Emits beginning of property declaration.
		/// </summary>
		public void BeginProperty(int property_id, ModifierList ml, int type_id, int param_count)
		{
			int owner_id = symbol_table[property_id].Level;
			symbol_table[property_id].Kind = MemberKind.Property;
			Gen(code.OP_CREATE_PROPERTY, property_id, owner_id, param_count);
			for (int i = 0; i < ml.Count; i++)
				Gen(code.OP_ADD_MODIFIER, property_id, (int) ml[i], 0);
			Gen(code.OP_ASSIGN_TYPE, property_id, type_id, 0);
		}

		/// <summary>
		/// Emits end of property declaration.
		/// </summary>
		public void EndProperty(int property_id)
		{
		}

		/// <summary>
		/// Emits beginning of event declaration.
		/// </summary>
		public void BeginEvent(int event_id, ModifierList ml, int type_id, int param_count)
		{
			int owner_id = symbol_table[event_id].Level;
			symbol_table[event_id].Kind = MemberKind.Event;
			Gen(code.OP_CREATE_EVENT, event_id, owner_id, param_count);
			for (int i = 0; i < ml.Count; i++)
				Gen(code.OP_ADD_MODIFIER, event_id, (int) ml[i], 0);
			Gen(code.OP_ASSIGN_TYPE, event_id, type_id, 0);
		}

		/// <summary>
		/// Emits end of event declaration.
		/// </summary>
		public void EndEvent(int event_id)
		{
		}

		/// <summary>
		/// Emits beginning of block.
		/// </summary>
		public void BeginBlock()
		{
			block_count ++;
			block_stack.Push(block_count);
			block_list.Add(CurrBlock);
		}

		/// <summary>
		/// Emits end of block.
		/// </summary>
		public void EndBlock()
		{
			block_stack.Pop();
			block_list.Add(CurrBlock);
		}

		/// <summary>
		/// Returns 'true', if end of file has been read.
		/// </summary>
		public bool IsEOF()
		{
			return scanner.IsEOF();
		}

		/// <summary>
		/// Returns 'true', if set conditional directives have been completed.
		/// </summary>
		public bool ConditionalDirectivesAreCompleted()
		{
			return scanner.ConditionalDirectivesAreCompleted();
		}

		/// <summary>
		/// Parses boolean literal.
		/// </summary>
		public virtual int Parse_BooleanLiteral()
		{
			if (IsCurrText("true"))
			{
				Match("true");
				return TRUE_id;
			}
			else
			{
				Match("false");
				return FALSE_id;
			}
		}

		/// <summary>
		/// Parses string literal.
		/// </summary>
		public virtual int Parse_StringLiteral()
		{
			int result = curr_token.id;
			Call_SCANNER();
			return result;
		}

		/// <summary>
		/// Parses character literal.
		/// </summary>
		public virtual int Parse_CharacterLiteral()
		{
			int result = curr_token.id;
			Call_SCANNER();
			return result;
		}

		/// <summary>
		/// Parses integer literal.
		/// </summary>
		public virtual int Parse_IntegerLiteral()
		{
			int result = curr_token.id;
			Call_SCANNER();
			return result;
		}

		/// <summary>
		/// Parses real literal.
		/// </summary>
		public virtual int Parse_RealLiteral()
		{
			int result = curr_token.id;
			Call_SCANNER();
			return result;
		}

		/// <summary>
		/// Undocumented.
		/// </summary>
		public void MoveSeparator()
		{
			int i = code.Card;
			while (code[i].op != code.OP_SEPARATOR) i--;
			code.Card ++;
			code[code.Card].op = code.OP_SEPARATOR;
			code[code.Card].arg1 = code[i].arg1;
			code[code.Card].arg2 = code[i].arg2;
			code[code.Card].res = code[i].res;
			code[i].op = code.OP_NOP;
		}

		/// <summary>
		/// Checks modifier list.
		/// </summary>
		internal void CheckModifiers(ModifierList ml, ModifierList true_list)
		{
			for (int i = 0; i < ml.Count; i++)
			{
				Modifier m = ml[i];
				if (!true_list.HasModifier(m))
					// The modifier 'modifier' is not valid for this item
					RaiseErrorEx(false, Errors.CS0106, m.ToString());
			}
		}

		public char SingleCharacter
		{
			get
			{
				return scanner.SingleCharacter;
			}
		}

		public char DoubleCharacter
		{
			get
			{
				return scanner.DoubleCharacter;
			}
		}

		public char DecimalCharacter
		{
			get
			{
				return scanner.DecimalCharacter;
			}
		}

		public char UpSingleCharacter
		{
			get
			{
				return scanner.UpSingleCharacter;
			}
		}

		public char UpDoubleCharacter
		{
			get
			{
				return scanner.UpDoubleCharacter;
			}
		}

		public char UpDecimalCharacter
		{
			get
			{
				return scanner.UpDecimalCharacter;
			}
		}
		public void AddModuleFromFile(string file_name)
		{
#if !PORTABLE
			if (scripter.Modules.IndexOf(file_name) >= 0)
				return;

			if (!File.Exists(file_name))
				return;

			scripter.AddModule(file_name, language);
			scripter.AddCodeFromFile(file_name, file_name);
#endif
        }
		public void SetUpcase(bool value)
        {
			if (value)
				code.SetUpcase(code.Card, Upcase.Yes);
			else
				code.SetUpcase(code.Card, Upcase.No);
		}

		public void SetStaticLocalVar(int id)
        {
			symbol_table[id].is_static = true;
		}


		public bool IsStaticLocalVar(int id)
		{
			return symbol_table[id].is_static;
        }

        public virtual string ParseASPXPage(string s)
        {
            return "";
        }

		/// <summary>
		/// Parses program.
		/// </summary>
		public virtual void Parse_Program()
		{
		}
	}
	#endregion BaseParser Class
}
