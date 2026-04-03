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

namespace PaxScript.Net
{
	#region TokenClass Enum
	/// <summary>
	/// Represents class of token.
	/// </summary>
	internal enum TokenClass
	{
		/// <summary>
		/// Non-initialized token.
		/// </summary>
		None,

		/// <summary>
		/// Separator.
		/// </summary>
		Separator,

		/// <summary>
		/// Identifier.
		/// </summary>
		Identifier,

		/// <summary>
		/// Keyword.
		/// </summary>
		Keyword,

		/// <summary>
		/// Boolean literal.
		/// </summary>
		BooleanConst,

		/// <summary>
		/// Integer literal.
		/// </summary>
		IntegerConst,

		/// <summary>
		/// String literal.
		/// </summary>
		StringConst,

		/// <summary>
		/// Character literal.
		/// </summary>
		CharacterConst,

		/// <summary>
		/// Real literal.
		/// </summary>
		RealConst,

		/// <summary>
		/// Special symbol.
		/// </summary>
		Special
	}
	#endregion TokenClass Enum

	#region Token Class
	/// <summary>
	/// Represents token of source code.
	/// </summary>
	internal class Token
	{
		/// <summary>
		/// Undocumented.
		/// </summary>
		public string atext;

		/// <summary>
		/// Position of token.
		/// </summary>
		public int position;

		/// <summary>
		/// Position of token.
		/// </summary>
		public int length;

		/// <summary>
		/// Id of token.
		/// </summary>
		public int id;

		/// <summary>
		/// Class of token.
		/// </summary>
		public TokenClass tokenClass;

		/// <summary>
		/// Owner of token.
		/// </summary>
		BaseScanner scanner;

		/// <summary>
		/// Constructor.
		/// </summary>
		public Token(BaseScanner scanner)
		{
			position = 0;
			length = 0;
			this.scanner = scanner;
		}

		/// <summary>
		/// Assigns token.
		/// </summary>
		public void AssignTo(Token t)
		{
			t.position = position;
			t.length = length;
			t.id = id;
			t.tokenClass = tokenClass;
		}

		/// <summary>
		/// Returns substring in text which represents given token.
		/// </summary>
		public string Text
		{
			get
			{
				return scanner.buff.Substring(position, length);
			}
		}

		/// <summary>
		/// Returns first character of the given token.
		/// </summary>
		public char Char
		{
			get
			{
				return scanner.buff[position];
			}
		}

		/// <summary>
		/// Returns length of token.
		/// </summary>
		public int Len
		{
			get
			{
				return length;
			}
		}
	}
	#endregion Token Class

	#region BaseScanner Class
	/// <summary>
	/// Represents base scanner class. All scanners must inherit this class.
	/// </summary>
	internal class BaseScanner
	{
		public char SingleCharacter = 'f';
		public char DoubleCharacter = 'd';
		public char DecimalCharacter = 'm';
		public bool Upcase = false;

		/// <summary>
		/// Position of current character.
		/// </summary>
		public int p;

		/// <summary>
		/// Line number of current line.
		/// </summary>
		int line_number;

		/// <summary>
		/// Position of current character in the current line.
		/// </summary>
		int pos;

		/// <summary>
		/// Source code buffer.
		/// </summary>
		public string buff;

		/// <summary>
		/// Current token.
		/// </summary>
		public Token token;

		/// <summary>
		/// EOF character.
		/// </summary>
		public const char CHAR_EOF = '\u001A';

		/// <summary>
		/// Decimal separator.
		/// </summary>
		public string DecimalSeparator;

		/// <summary>
		/// Scanner history.
		/// </summary>
		private IntegerStack history;

		/// <summary>
		/// Scanner history record length.
		/// </summary>
		public const int HISTORY_REC_LENGTH = 7;

		/// <summary>
		/// Id of #if directive.
		/// </summary>
		private const int ppIF = 1;

		/// <summary>
		/// Id of #elif directive.
		/// </summary>
		private const int ppELIF = 2;

		/// <summary>
		/// Id of #else directive.
		/// </summary>
		private const int ppELSE = 3;

		/// <summary>
		/// Id of #endif directive.
		/// </summary>
		private const int ppENDIF = 4;

		/// <summary>
		/// Stack of conditional directives
		/// </summary>
		private IntegerStack def_stack;

		/// <summary>
		/// Represents kernel of scripter.
		/// </summary>
		BaseScripter scripter;

		/// <summary>
		/// Represents parser object.
		/// </summary>
		BaseParser parser;

		/// <summary>
		/// Constructor.
		/// </summary>
		public BaseScanner(BaseParser parser)
		{
			token = new Token(this);
			history = new IntegerStack();
			def_stack = new IntegerStack();
			this.parser = parser;

			System.Globalization.NumberFormatInfo curr_info = System.Globalization.NumberFormatInfo.CurrentInfo;
			DecimalSeparator = curr_info.NumberDecimalSeparator;
		}

		/// <summary>
		/// Initializes scanner.
		/// </summary>
		internal void Init(BaseScripter scripter, string code)
		{
			this.scripter = scripter;
			buff = code + CHAR_EOF + CHAR_EOF + CHAR_EOF;
			p = -1;
			line_number = 0;
			pos = 0;
			history.Clear();
			def_stack.Clear();
		}

		/// <summary>
		/// Creates error object.
		/// </summary>
		public void RaiseError(string message)
		{
			scripter.Dump();
			scripter.code.n = scripter.code.Card;
			parser.RaiseError(true, message);
		}

		/// <summary>
		/// Creates error object.
		/// </summary>
		public void RaiseErrorEx(string message, params object[] p)
		{
			scripter.Dump();
			scripter.code.n = scripter.code.Card;
			parser.RaiseErrorEx(true, message, p);
		}

		/// <summary>
		/// Returns n-th character of source code started from current position
		/// </summary>
		public char LA(int n)
		{
			return buff[p + n];
		}

		/// <summary>
		/// Returns 'true', if character c is a letter
		/// </summary>
		public static bool IsAlpha(char c)
		{
			return ((c >= 'a') && (c <= 'z')) ||
				   ((c >= 'A') && (c <= 'Z')) ||
				   (c == '_');
		}

		/// <summary>
		/// Returns 'true', if character c is a digit
		/// </summary>
		public static bool IsDigit(char c)
		{
			return (c >= '0') && (c <= '9');
		}

		/// <summary>
		/// Returns 'true', if character c is a hex digit
		/// </summary>
		public static bool IsHexDigit(char c)
		{
			return IsDigit(c) ||
				   ((c >= 'a') && (c <= 'f')) ||
				   ((c >= 'A') && (c <= 'F'));
		}

		/// <summary>
		/// Returns 'true', if character c represents end of file.
		/// </summary>
		public bool IsEOF(char c)
		{
			return (c == CHAR_EOF);
		}

		/// <summary>
		/// Returns 'true', if current character c represents end of file.
		/// </summary>
		public bool IsEOF()
		{
			return (LA(0) == CHAR_EOF);
		}

		/// <summary>
		/// Returns 'true', if current c is a new line character.
		/// </summary>
		public bool IsNewLine(char c)
		{
			return ((c == '\u000A') ||
					(c == '\u000D') ||
					(c == '\u0085') ||
					(c == '\u2028') ||
					(c == '\u2029'));
		}

		/// <summary>
		/// Returns 'true', if current c is a whitespace.
		/// </summary>
		public bool IsWhitespace(char c)
		{
			return ((c == '\u0009') ||
					(c == '\u000B') ||
					(c == '\u000C') ||
					(c == ' ')
					);
		}

		/// <summary>
		/// Returns current line number.
		/// </summary>
		public int LineNumber
		{
			get
			{
				return line_number;
			}
		}

		/// <summary>
		/// Emits separator and increses current line number
		/// </summary>
		public void IncLineNumber()
		{
			line_number++;
			pos = 0;
			parser.GenSeparator();
			token.position = p + 1;
		}

		/// <summary>
		/// Scans whitespace
		/// </summary>
		public void ScanWhiteSpace()
		{
			token.position = p + 1;
		}

		/// <summary>
		/// Scans new line
		/// </summary>
		public virtual void ScanNewLine()
		{
			int nl = TestNewLine();
			if (nl > 0)
			{
				SkipChars(nl);
				IncLineNumber();
				if (!IsNewLine(LA(0)))
					p--;
				else
					ScanNewLine();
			}
			token.position = p + 1;
		}

		/// <summary>
		/// Checks if current character begins new line.
		/// </summary>
		public int TestNewLine()
		{
			if (
				(LA(0) == '\u000A') || // LF
				(LA(0) == '\u0085') || // new line character
				(LA(0) == '\u2028') || // line separator character
				(LA(0) == '\u2029') // paragraph separator character
			   )
			{
				return 1;
			}
			else if (LA(0) == '\u000D') // CR
			{
				if (LA(1) == '\u000A')
					return 1;
				else
					return 1;
			}
			else
				return 0;
		}

		/// <summary>
		/// Scans n characters.
		/// </summary>
		public void SkipChars(int n)
		{
			for (int i = 1; i <= n; i++)
			{
				GetNextChar();
			}
		}

		/// <summary>
		/// Increases scanner position and returns current character.
		/// </summary>
		public char GetNextChar()
		{
			p++;
			pos++;
			return buff[p];
		}

		/// <summary>
		/// Scans identifier.
		/// </summary>
		public void ScanIdentifier()
		{
			if (LA(0) == '@')
				GetNextChar();
			while (IsAlpha(LA(1)) || IsDigit(LA(1)) || (LA(1) == '\\'))
			{
				char c = GetNextChar();
				if (c == '\\')
				{
					if (LA(1) == 'x')
						ScanHexadecimalEscapeSequence();
					else if (LA(1) == 'u')
						ScanUnicodeEscapeSequence(true);
					else if (LA(1) == 'U')
						ScanUnicodeEscapeSequence(true);
					else
						ScanSimpleEscapeSequence();
				}
			}
			token.tokenClass = TokenClass.Identifier;
		}

		/// <summary>
		/// Scans escape sequence.
		/// </summary>
		void ScanSimpleEscapeSequence()
		{
			char c = GetNextChar();
			if ((c == '\'') ||
				(c == '\\') ||
				(c == '"') ||
				(c == '0') ||
				(c == 'a') ||
				(c == 'b') ||
				(c == 'f') ||
				(c == 'n') ||
				(c == 'r') ||
				(c == 't') ||
				(c == 'v'))
				GetNextChar();
			else
				// Unrecognized escape sequence
				RaiseError(Errors.CS1009);
		}

		/// <summary>
		/// Scans hexadecimal escape sequence.
		/// </summary>
		void ScanHexadecimalEscapeSequence()
		{
			GetNextChar();
			int old_pos = p;
			ScanHexDigits();
			if (p - old_pos == 0)
				// Unrecognized escape sequence
				RaiseError(Errors.CS1009);
			else if (p - old_pos > 4)
				// Too many characters in character literal
				RaiseError(Errors.CS1012);
			GetNextChar();
		}

		/// <summary>
		/// Scans unicode escape sequence.
		/// </summary>
		void ScanUnicodeEscapeSequence(bool in_string)
		{
			char c = GetNextChar();
			int old_pos = p;
			int limit;
			if (in_string)
			{
				if (c == 'u')
					limit = 4;
				else
					limit = 8;
			}
			else
				limit = - 1;
			int k = 0;
			while (IsHexDigit(LA(1)))
			{
				GetNextChar();
				k++;
				if (k == limit)
					break;
			}
			if (c == 'u')
			{
				if (p - old_pos < 4)
					// Unrecognized escape sequence
					RaiseError(Errors.CS1009);
				else if (p - old_pos > 4)
					// Too many characters in character literal
					RaiseError(Errors.CS1012);
			}
			else if (c == 'U')
			{
				if (p - old_pos < 8)
					// Unrecognized escape sequence
					RaiseError(Errors.CS1009);
				else if (p - old_pos > 8)
					// Too many characters in character literal
					RaiseError(Errors.CS1012);
			}
			GetNextChar();
		}

		/// <summary>
		/// Scans digits.
		/// </summary>
		void ScanDigits()
		{
			while (IsDigit(LA(1))) GetNextChar();
		}

		/// <summary>
		/// Scans hex digits.
		/// </summary>
		void ScanHexDigits()
		{
			while (IsHexDigit(LA(1))) GetNextChar();
		}

		/// <summary>
		/// Scans suffix of integer literal.
		/// </summary>
		void ScanIntegerTypeSuffix()
		{
			if ((LA(1) == 'u') || (LA(1) == 'U'))
			{
				GetNextChar();
				if (LA(1) == 'l')
				{
					// The 'l' suffix is easily confused with the digit '1' -- use 'L' for clarity
					scripter.CreateWarningObject(Errors.CS0078);
					GetNextChar();
				}
				else if (LA(1) == 'L')
				{
					GetNextChar();
				}

			}
			else if ((LA(1) == 'l') || (LA(1) == 'L'))
			{
				if (LA(1) == 'l')
				{
					// The 'l' suffix is easily confused with the digit '1' -- use 'L' for clarity
					scripter.CreateWarningObject(Errors.CS0078);
				}

				GetNextChar();
				if ((LA(1) == 'u') || (LA(1) == 'U'))
					GetNextChar();
			}
		}

		/// <summary>
		/// Scans integer or real literal.
		/// </summary>
		public void ScanNumberLiteral()
		{
			if (LA(0) == '.')
			{
				ScanDigits();
				if ((LA(1) == 'e') || (LA(1) == 'E'))
				{
					GetNextChar();
					if ((LA(1) == '+') || (LA(1) == '-'))
						GetNextChar();
					ScanDigits();
				}
				if ((LA(1) == SingleCharacter) || (LA(1) == UpSingleCharacter) ||
					(LA(1) == DoubleCharacter) || (LA(1) == UpDoubleCharacter) ||
					(LA(1) == DecimalCharacter) || (LA(1) == UpDecimalCharacter))
					GetNextChar();
				token.tokenClass = TokenClass.RealConst;
				return;
			}

			ScanDigits();

			if ((LA(1) == 'u') || (LA(1) == 'U') || (LA(1) == 'l') || (LA(1) == 'L'))
			{
				ScanIntegerTypeSuffix();
				token.tokenClass = TokenClass.IntegerConst;
			}
			else if (((LA(1) == 'x') || (LA(1) == 'X')) && (LA(0) == '0'))
			{
				GetNextChar();
				ScanHexDigits();
				if ((LA(1) == 'u') || (LA(1) == 'U') || (LA(1) == 'l') || (LA(1) == 'L'))
					ScanIntegerTypeSuffix();
				token.tokenClass = TokenClass.IntegerConst;
			}
			else if (LA(1) == '.')
			{
				if (!IsDigit(LA(2)))
				{
					token.tokenClass = TokenClass.IntegerConst;
					return;
				}
				GetNextChar();
				ScanDigits();
				if ((LA(1) == 'e') || (LA(1) == 'E'))
				{
					GetNextChar();
					if ((LA(1) == '+') || (LA(1) == '-'))
						GetNextChar();
					ScanDigits();
				}
				if ((LA(1) == SingleCharacter) || (LA(1) == UpSingleCharacter) ||
					(LA(1) == DoubleCharacter) || (LA(1) == UpDoubleCharacter) ||
					(LA(1) == DecimalCharacter) || (LA(1) == UpDecimalCharacter))
					GetNextChar();
				token.tokenClass = TokenClass.RealConst;
			}
			else if ((LA(1) == 'e') || (LA(1) == 'E'))
			{
				GetNextChar();
				if ((LA(1) == '+') || (LA(1) == '-'))
					GetNextChar();
				ScanDigits();
				if ((LA(1) == SingleCharacter) || (LA(1) == UpSingleCharacter) ||
					(LA(1) == DoubleCharacter) || (LA(1) == UpDoubleCharacter) ||
					(LA(1) == DecimalCharacter) || (LA(1) == UpDecimalCharacter))
					GetNextChar();
				token.tokenClass = TokenClass.RealConst;
			}
			else if ((LA(1) == SingleCharacter) || (LA(1) == UpSingleCharacter) ||
					 (LA(1) == DoubleCharacter) || (LA(1) == UpDoubleCharacter) ||
					 (LA(1) == DecimalCharacter) || (LA(1) == UpDecimalCharacter))
			{
				GetNextChar();
				token.tokenClass = TokenClass.RealConst;
			}
			else
				token.tokenClass = TokenClass.IntegerConst;
		}

		/// <summary>
		/// Scans character literal.
		/// </summary>
		public void ScanCharLiteral()
		{
			token.tokenClass = TokenClass.CharacterConst;
			char c = GetNextChar();
			if (c == '\\')
			{
				if (LA(1) == 'x')
					ScanHexadecimalEscapeSequence();
				else if ((LA(1) == 'u') || (LA(1) == 'U'))
					ScanUnicodeEscapeSequence(false);
				else
					ScanSimpleEscapeSequence();
			}
			else if (c == '\'')
			{
				// Empty character literal
				RaiseError(Errors.CS1011);
			}
			else
				GetNextChar();
		}

        public void ScanSeparator()
        {
            if (LA(1) == '\u000A')
                GetNextChar();
            token.tokenClass = TokenClass.Separator;
            line_number++;
            token.id = line_number;
        }

		/// <summary>
		/// Parses string.
		/// </summary>
		public virtual string ParseString(string s)
		{
			int i = 0;
			string result = "";
			for (;;)
			{
				if (i >= s.Length)
					return result;
				char c = s[i];
				if (c == '\\')
				{
					int old_pos;
					string l;
					switch (s[i + 1])
					{
						case 'x':
							i++;
							old_pos = i;
							while (IsHexDigit(s[i + 1])) i++;
							if (i - old_pos == 0)
								// Unrecognized escape sequence
								RaiseError(Errors.CS1009);
							else if (i - old_pos > 4)
								// Too many characters in character literal
								RaiseError(Errors.CS1012);
							l = s.Substring(old_pos + 1, i - old_pos);
							c = (char) int.Parse(l, System.Globalization.NumberStyles.AllowHexSpecifier);
							result = result + c;
							i++;
							break;
						case 'u':
							i++;
							old_pos = i;
							while (IsHexDigit(s[i + 1]))
							{
								 i++;
								 if (i - old_pos == 4)
									break;
							}
							if (i - old_pos < 4)
								// Unrecognized escape sequence
								RaiseError(Errors.CS1009);
							l = s.Substring(old_pos + 1, i - old_pos);
							c = (char) int.Parse(l, System.Globalization.NumberStyles.AllowHexSpecifier);
							result = result + c;
							i++;
							break;
						case 'U':
							i++;
							old_pos = i;
							while (IsHexDigit(s[i + 1]))
							{
								 i++;
								 if (i - old_pos == 8)
									break;
							}
							if (i - old_pos < 8)
								// Unrecognized escape sequence
								RaiseError(Errors.CS1009);
							l = s.Substring(old_pos + 1, i - old_pos);
							c = (char) int.Parse(l, System.Globalization.NumberStyles.AllowHexSpecifier);
							result = result + c;
							i++;
							break;
						case '\'':
						case '\\':
							i++;
							result = result + s[i++];
							break;
						case '"':
							i++;
							result = result + '"';
							i++;
							break;
						case '0':
							i++;
							result = result + '\0';
							i++;
							break;
						case 'a':
							i++;
							result = result + '\a';
							i++;
							break;
						case 'b':
							i++;
							result = result + '\b';
							i++;
							break;
						case 'f':
							i++;
							result = result + '\f';
							i++;
							break;
						case 'n':
							i++;
							result = result + '\n';
							i++;
							break;
						case 'r':
							i++;
							result = result + '\r';
							i++;
							break;
						case 't':
							i++;
							result = result + '\t';
							i++;
							break;
						case 'v':
							i++;
							result = result + '\v';
							i++;
							break;
						default:
						{
							// Unrecognized escape sequence
							RaiseError(Errors.CS1009);
							break;
						}
					}

				}
				else
				{
					result = result + c;
					i++;
				}

			}
		}

		/// <summary>
		/// Scans regular string literal.
		/// </summary>
		public void ScanRegularStringLiteral(char ch)
		{
			for (;;)
			{
				char c = GetNextChar();

				if (IsNewLine(c))
				{
					// Newline in constant
					RaiseError(Errors.CS1010);
				}

				if (c == '\\')
				{
					if (LA(1) == 'x')
						ScanHexadecimalEscapeSequence();
					else if (LA(1) == 'u')
						ScanUnicodeEscapeSequence(true);
					else if (LA(1) == 'U')
						ScanUnicodeEscapeSequence(true);
					else
						ScanSimpleEscapeSequence();
				}
				if (LA(0) == ch)
					break;
			}
			token.tokenClass = TokenClass.StringConst;
		}

		/// <summary>
		/// Parses verbatim string literal.
		/// </summary>
		public string ParseVerbatimString(string s)
		{
			s = s.Substring(1);
			string result = @"""";
			bool b = false;
			for (int i = 1; i < s.Length - 1; i++)
			{
				if (s[i] == '"')
				{
					if (b)
					{
						b = false;
						result += s[i];
					}
					else
						b = true;
				}
				else
					result += s[i];
			}
			result += @"""";
			return result;
		}

		/// <summary>
		/// Scans verbatim string literal.
		/// </summary>
		public virtual void ScanVerbatimStringLiteral(char ch)
		{
			int p = token.position;
			GetNextChar(); // skip '@'
			for (;;)
			{
				int nl = TestNewLine();
				if (nl > 0)
				{
					SkipChars(nl);
					IncLineNumber();
				}
				char c = GetNextChar();

				if (IsEOF(c))
				{
					// Unterminated string literal
					RaiseError(Errors.CS1039);
					return;
				}

				if (c == ch)
				{
					if (LA(1) == ch)
						GetNextChar();
					else
						break;
				}
			}
			token.tokenClass = TokenClass.StringConst;
			token.position = p;
		}

		/// <summary>
		/// Scans special symbol.
		/// </summary>
		public void ScanSpecial()
		{
			token.tokenClass = TokenClass.Special;
		}

		/// <summary>
		/// Scans end of file.
		/// </summary>
		public void ScanEOF()
		{
			token.tokenClass = TokenClass.Special;
		}

		/// <summary>
		/// Reads new token.
		/// </summary>
		public virtual void ReadCustomToken() {}

		/// <summary>
		/// Reads new token.
		/// </summary>
		public Token ReadToken()
		{
			history.Push(p);
			history.Push(line_number);
			history.Push(pos);

			history.Push(token.position);
			history.Push(token.length);
			history.Push(token.id);
			history.Push((int) token.tokenClass);

			token.position = p + 1;
			token.tokenClass = TokenClass.None;
			ReadCustomToken();
			token.length = p - token.position + 1;

			return token;
		}

		/// <summary>
		/// Restores previous position of scanner.
		/// </summary>
		public void BackUp()
		{
			token.tokenClass = (TokenClass) history.Pop();
			token.id = (int) history.Pop();
			token.length = (int) history.Pop();
			token.position = (int) history.Pop();

			token.atext = token.Text;

			pos = (int) history.Pop();
			line_number = (int) history.Pop();
			p = (int) history.Pop();
		}

		/// <summary>
		/// Returns position of scanner.
		/// </summary>
		public int Pos
		{
			get
			{
				return pos;
			}
		}

		/// <summary>
		/// Scans whitespaces.
		/// </summary>
		public void ScanWhiteSpaces()
		{
			while (IsWhitespace(LA(0))) GetNextChar();
		}

		/// <summary>
		/// Scans directive name.
		/// </summary>
		public string ScanPPDirectiveName()
		{
			token.position = p;
			ScanIdentifier();
			token.length = p - token.position + 1;
			return token.Text;
		}

		/// <summary>
		/// Scans conditional symbol.
		/// </summary>
		public string ScanConditionalSymbol()
		{
			token.position = p;
			ScanIdentifier();
			token.length = p - token.position + 1;
			GetNextChar();
			return token.Text;
		}

		/// <summary>
		/// Scans new line.
		/// </summary>
		void ScanPPNewLine()
		{
			if (!IsNewLine(LA(0)))
			{
				for (;;)
				{
					char c = GetNextChar();
					if (IsNewLine(c))
						break;
				}
				ScanNewLine();
			}
			token.position = p;
			p--;
		}

		/// <summary>
		/// Scans message of conditional directive.
		/// </summary>
		string ScanPPMessage()
		{
			string s = "";
			for (;;)
			{
				char c = GetNextChar();
				if (IsNewLine(c))
					break;
				else
					s += c;
			}
			ScanWhiteSpaces();
			return s;
		}

		/// <summary>
		/// Scans expression of conditional directive.
		/// </summary>
		bool ScanPPExpression()
		{
			ScanWhiteSpaces();
			bool result = ScanPPOrExpression();
			ScanWhiteSpaces();
			return result;
		}

		/// <summary>
		/// Scans OR expression of conditional directive.
		/// </summary>
		bool ScanPPOrExpression()
		{
			bool result = ScanPPAndExpression();
			ScanWhiteSpaces();
			while ((LA(0) == '|') && (LA(1) == '|'))
			{
				GetNextChar();
				GetNextChar();
				GetNextChar();
				ScanWhiteSpaces();

				result = result || ScanPPAndExpression();
			}
			return result;
		}

		/// <summary>
		/// Scans AND expression of conditional directive.
		/// </summary>
		bool ScanPPAndExpression()
		{
			bool result = ScanPPEqualityExpression();
			ScanWhiteSpaces();
			while ((LA(0) == '&') && (LA(1) == '&'))
			{
				GetNextChar();
				GetNextChar();
				GetNextChar();
				ScanWhiteSpaces();

				result = result && ScanPPEqualityExpression();
			}
			return result;
		}

		/// <summary>
		/// Scans equality expression of conditional directive.
		/// </summary>
		bool ScanPPEqualityExpression()
		{
			bool result = ScanPPUnaryExpression();
			ScanWhiteSpaces();
			while (
					((LA(0) == '=') && (LA(1) == '=')) ||
					((LA(0) == '!') && (LA(1) == '='))
				   )
			{
				bool eq = (LA(0) == '=') && (LA(1) == '=');

				GetNextChar();
				GetNextChar();
				GetNextChar();
				ScanWhiteSpaces();
				if (eq)
				{
					result = result == ScanPPUnaryExpression();
				}
				else
				{
					result = result != ScanPPUnaryExpression();
				}
			}
			return result;
		}

		/// <summary>
		/// Scans unary expression of conditional directive.
		/// </summary>
		bool ScanPPUnaryExpression()
		{
			bool result;
			if (LA(0) == '!')
			{
				GetNextChar();
				ScanWhiteSpaces();
				result = ! ScanPPUnaryExpression();
			}
			else
			{
				result = ScanPPPrimaryExpression();
			}
			return result;
		}

		/// <summary>
		/// Scans primary expression of conditional directive.
		/// </summary>
		bool ScanPPPrimaryExpression()
		{
			bool result;
			if (LA(0) == '(')
			{
				GetNextChar();
				ScanWhiteSpaces();
				result = ScanPPExpression();
				ScanWhiteSpaces();
				if (LA(0) != ')')
					RaiseErrorEx(Errors.CS1003, ")");
				GetNextChar();
			}
			else
			{
				string s = ScanConditionalSymbol();
				if (s == "true")
					result = true;
				else if (s == "false")
					result = false;
				else
				{
					result = scripter.PPDirectiveList.IndexOf(s) != -1;
				}
			}
			return result;
		}

		/// <summary>
		/// Scans skipped section conditional directive.
		/// </summary>
		void ScanSkippedSection()
		{
			for (;;)
			{
				char c = GetNextChar();

				if (c == '#')
					break;

				int nl = TestNewLine();
				if (nl > 0)
				{
					SkipChars(nl);
					IncLineNumber();
				}

				if (IsEOF(LA(1)))
				{
					GetNextChar();
					ScanEOF();
					return;
				}
			}
		}

		/// <summary>
		/// Returns 'true', if set of conditional directives has been completed.
		/// </summary>
		public bool ConditionalDirectivesAreCompleted()
		{
			return def_stack.Count == 0;
		}

		/// <summary>
		/// Scans conditional directive.
		/// </summary>
		public virtual void ScanPPDirective()
		{
			GetNextChar();
			ScanWhiteSpaces();

			string s = ScanPPDirectiveName();
			if (PaxSystem.CompareStrings(s, "end", Upcase))
			{
				GetNextChar();
				ScanWhiteSpaces();
				s += ScanPPDirectiveName();
			}

			if (PaxSystem.CompareStrings(s,  "define", Upcase))
			{
/*
				if (history.Count > HISTORY_REC_LENGTH)
					// Cannot define/undefine preprocessor symbols after first token in file
					RaiseError(Errors.CS1032);
*/
				GetNextChar();
				ScanWhiteSpaces();
				s = ScanConditionalSymbol();

				scripter.PPDirectiveList.Add(s);

				parser.GenDefine(s);

				ScanPPNewLine();
				token.tokenClass = TokenClass.None;
			}
			else if (PaxSystem.CompareStrings(s, "undef", Upcase))
			{
/*
				if (history.Count > HISTORY_REC_LENGTH)
					// Cannot define/undefine preprocessor symbols after first token in file
					RaiseError(Errors.CS1032);
*/

				GetNextChar();
				ScanWhiteSpaces();
				s = ScanConditionalSymbol();

				int i = scripter.PPDirectiveList.IndexOf(s);
				if (i != -1)
					scripter.PPDirectiveList.RemoveAt(i);

				parser.GenUndef(s);

				ScanPPNewLine();
				token.tokenClass = TokenClass.None;
			}
			else if (PaxSystem.CompareStrings(s, "if", Upcase))
			{
				GetNextChar();
				ScanWhiteSpaces();
				bool b = ScanPPExpression();

				def_stack.PushObject(ppIF, b);

				if (!b)
				{
					ScanSkippedSection();
					if (IsEOF())
						// #endif directive expected
						RaiseError(Errors.CS1027);
					else
						ScanPPDirective();
				}
				else
				{
					ScanPPNewLine();
					token.tokenClass = TokenClass.None;
				}
			}
			else if (PaxSystem.CompareStrings(s, "elif", Upcase))
			{
				GetNextChar();
				ScanWhiteSpaces();
				bool b = ScanPPExpression();

				if (def_stack.Count == 0)
					// Unexpected preprocessor directive
					RaiseError(Errors.CS1028);

				int w = def_stack.Peek();
				b &= ! ((bool) def_stack.PeekObject());

				if ((w != ppIF) && (w != ppELIF))
					// Unexpected preprocessor directive
					RaiseError(Errors.CS1028);

				def_stack.PushObject(ppELIF, b);

				if (!b)
				{
					ScanSkippedSection();
					if (IsEOF())
						// #endif directive expected
						RaiseError(Errors.CS1027);
					else
						ScanPPDirective();
				}
				else
				{
					ScanPPNewLine();
					token.tokenClass = TokenClass.None;
				}
			}
			else if (PaxSystem.CompareStrings(s, "else", Upcase))
			{
				GetNextChar();
				ScanWhiteSpaces();

				if (def_stack.Count == 0)
					// Unexpected preprocessor directive
					RaiseError(Errors.CS1028);

				bool b = false;
				int i = def_stack.Count - 1;

				for (;;)
				{
					int w = def_stack[i];
					if (w == ppIF)
					{
						bool temp = (bool) def_stack.Objects[i];
						if (temp)
							b = true;
						break;
					}
					else if (w == ppELIF)
					{
						bool temp = (bool) def_stack.Objects[i];
						if (temp)
						{
							b = true;
							break;
						}
					}
					else
						// Unexpected preprocessor directive
						RaiseError(Errors.CS1028);

					i--;
					if (i < 0)
						break;

				}

				b = !b;
				def_stack.PushObject(ppELSE, b);

				if (!b)
				{
					ScanSkippedSection();
					if (IsEOF())
						// #endif directive expected
						RaiseError(Errors.CS1027);
					else
						ScanPPDirective();
				}
				else
				{
					ScanPPNewLine();
					token.tokenClass = TokenClass.None;
				}
			}
			else if (PaxSystem.CompareStrings(s, "endif", Upcase))
			{
				GetNextChar();
				ScanWhiteSpaces();

				int k1 = 0;
				int k2 = 0;
				for (int i = 0; i < def_stack.Count; i++)
				{
					int w = def_stack[i];
					if (w == ppIF)
						k1 ++;
					else if (w == ppENDIF)
						k2 ++;
				}
				if (k2 >= k1)
					// Unexpected preprocessor directive
					RaiseError(Errors.CS1028);

				for (int i = def_stack.Count - 1; i >= 0; i--)
				{
					if (def_stack[i] == ppIF)
					{
						while (def_stack.Count > i)
							def_stack.Pop();
						break;
					}
				}

				ScanPPNewLine();
				token.tokenClass = TokenClass.None;
			}
			else if (PaxSystem.CompareStrings(s, "region", Upcase))
			{
				GetNextChar();
				ScanWhiteSpaces();
				s = ScanPPMessage();
				parser.GenStartRegion(s);
				token.tokenClass = TokenClass.None;
			}
			else if (PaxSystem.CompareStrings(s, "endregion", Upcase))
			{
				GetNextChar();
				ScanWhiteSpaces();
				s = ScanPPMessage();
				parser.GenEndRegion(s);
				token.tokenClass = TokenClass.None;
			}
			else if (PaxSystem.CompareStrings(s, "warning", Upcase))
			{
				GetNextChar();
				ScanWhiteSpaces();
				s = ScanPPMessage();
				scripter.CreateWarningObjectEx(Errors.CS1030, s);
				token.tokenClass = TokenClass.None;
			}
			else if (PaxSystem.CompareStrings(s, "error", Upcase))
			{
				GetNextChar();
				ScanWhiteSpaces();
				s = ScanPPMessage();
				scripter.CreateErrorObjectEx(Errors.CS1029, s);
				token.tokenClass = TokenClass.None;
			}
			else if (PaxSystem.CompareStrings(s, "line", Upcase))
			{
				GetNextChar();
				ScanWhiteSpaces();

				s = "";
				if (IsDigit(LA(1)))
				{
					while (IsDigit(LA(1))) s += GetNextChar();
					if (s == "")
						RaiseError(Errors.CS1576);
					string file_name = "";
					for (;;)
					{
						char c = GetNextChar();
						if (IsNewLine(c))
							break;
						else
							file_name += c;
					}
					ScanWhiteSpaces();
				}
				else
				{
					while (IsAlpha(LA(1))) s += GetNextChar();
					if (!PaxSystem.CompareStrings(s, "default", Upcase))
						RaiseError(Errors.CS1576);
				}
				token.tokenClass = TokenClass.None;
			}
			else
			{
				// Preprocessor directive expected
				RaiseError(Errors.CS1024);
			}
		}

		/// <summary>
		/// Scans single line comment.
		/// </summary>
		public void ScanSingleLineComment()
		{
			for (;;)
			{
				GetNextChar();
				if (IsEOF())
					break;

				if (TestNewLine() > 0)
				{
					ScanNewLine();
					break;
				}
			}
			token.position = p + 1;
		}

		/// <summary>
		/// Returns current character
		/// </summary>
		public char CurrChar
		{
			get
			{
				return buff[p];
			}
		}

		/// <summary>
		/// Returns code of current character
		/// </summary>
		public int CurrCharCode
		{
			get
			{
				return (int) buff[p];
			}
		}

		public char UpSingleCharacter
		{
			get
			{
				return Char.ToUpper(SingleCharacter);
			}
		}

		public char UpDoubleCharacter
		{
			get
			{
				return Char.ToUpper(DoubleCharacter);
			}
		}

		public char UpDecimalCharacter
		{
			get
			{
				return Char.ToUpper(DecimalCharacter);
			}
		}
	}
	#endregion BaseScanner Class
}




