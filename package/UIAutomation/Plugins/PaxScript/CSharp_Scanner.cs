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

namespace PaxScript.Net
{
	#region CSharp_Scanner Class
	/// <summary>
	/// Lexer of C# language.
	/// </summary>
	internal class CSharp_Scanner: BaseScanner
	{
		/// <summary>
		/// Constructor.
		/// </summary>
		public CSharp_Scanner(BaseParser parser): base(parser)
		{
		}

		/// <summary>
		/// Reads new token.
		/// </summary>
		public override void ReadCustomToken()
		{
			char c;

			for (;;)
			{
				c = GetNextChar();

				if (IsWhitespace(c))
					ScanWhiteSpace();
				else if (IsNewLine(c))
				{
					ScanNewLine();
				}
				else if (IsEOF(c)) ScanEOF();
				else if (IsAlpha(c))
				{
					ScanIdentifier();
				}
				else if (c == '@')
				{
					char c1 = LA(1);
					if (c1 == '"')
						ScanVerbatimStringLiteral('"');
					else
						ScanIdentifier();
				}
				else if (c == '"') ScanRegularStringLiteral('"');
				else if (IsDigit(c)) ScanNumberLiteral();
				else if (c == '\'') ScanCharLiteral();
				else if (c == '(') ScanSpecial();
				else if (c == ')') ScanSpecial();
				else if (c == ',') ScanSpecial();
				else if (c == ':') ScanSpecial();
				else if (c == '?') ScanSpecial();
				else if (c == ';') ScanSpecial();
				else if (c == '~') ScanSpecial();
				else if (c == '.')
				{
					if (IsDigit(LA(1)))
						ScanNumberLiteral();
					else
						ScanSpecial();
				}
				else if (c == '(') ScanSpecial();
				else if (c == ')') ScanSpecial();
				else if (c == '[') ScanSpecial();
				else if (c == ']') ScanSpecial();
				else if (c == '{')
					ScanSpecial();
				else if (c == '}') ScanSpecial();
				else if (c == '=')
				{
					if (LA(1) == '=')
						GetNextChar();
					ScanSpecial();
				}
				else if (c == '+')
				{
					if (LA(1) == '+')
						GetNextChar();
					else if (LA(1) == '=')
						GetNextChar();
					ScanSpecial();
				}
				else if (c == '-')
				{
					if (LA(1) == '-')
						GetNextChar();
					else if (LA(1) == '=')
						GetNextChar();
					ScanSpecial();
				}
				else if (c == '*')
				{
					if (LA(1) == '=')
						GetNextChar();
					ScanSpecial();
				}
				else if (c == '/')
				{
					if (LA(1) == '=')
						GetNextChar();
					else if (LA(1) == '*') // delimited comment
					{
						for (;;)
						{
							GetNextChar();

							int nl = TestNewLine();
							if (nl > 0)
							{
								SkipChars(nl);
								IncLineNumber();
							}

							if (IsEOF(LA(1)))
							{
								// End-of-file found, '*/' expected
								RaiseError(Errors.CS1035);
								return;
							}

							if ((LA(1) == '*') && (LA(2) == '/'))
							{
								SkipChars(2);
								break;
							}
						}
						continue;
					}
					else if (LA(1) == '/') // single-line comment
					{
						ScanSingleLineComment();
						continue;
					}
					ScanSpecial();
				}
				else if (c == '%')
				{
					if (LA(1) == '=')
						GetNextChar();
					ScanSpecial();
				}
				else if (c == '&')
				{
					if (LA(1) == '=')
						GetNextChar();
					else if (LA(1) == '&')
						GetNextChar();
					ScanSpecial();
				}
				else if (c == '|')
				{
					if (LA(1) == '=')
						GetNextChar();
					else if (LA(1) == '|')
						GetNextChar();
					ScanSpecial();
				}
				else if (c == '^')
				{
					if (LA(1) == '=')
						GetNextChar();
					ScanSpecial();
				}
				else if (c == '<')
				{
					if (LA(1) == '<')
						GetNextChar();
					else if (LA(1) == '=')
						GetNextChar();
					ScanSpecial();
				}
				else if (c == '>')
				{
					if (LA(1) == '>')
						GetNextChar();
					else if (LA(1) == '=')
						GetNextChar();
					ScanSpecial();
				}
				else if (c == '!')
				{
					if (LA(1) == '=')
						GetNextChar();
					ScanSpecial();
				}
				else if (c == '#')
				{
					ScanPPDirective();
					continue;
				}
				else
					RaiseError(Errors.SYNTAX_ERROR);

				if (token.tokenClass != TokenClass.None) break;
			}
		}
	}
	#endregion CSharp_Scanner Class
}
