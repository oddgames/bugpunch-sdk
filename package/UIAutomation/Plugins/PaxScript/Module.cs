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
using System.IO;

namespace PaxScript.Net
{
	#region Module Class
	/// <summary>
	/// Represents module of a script.
	/// </summary>
	public class Module
	{
		/// <summary>
		/// Name of module.
		/// </summary>
		string name;

		/// <summary>
		/// Source code of script.
		/// </summary>
		string text = "";

		/// <summary>
		/// Scripting language name.
		/// </summary>
		string language;

		/// <summary>
		/// File name wwhich contains source code of script.
		/// </summary>
		string file_name = "";

		/// <summary>
		/// Start position at symbol table.
		/// </summary>
		public int S1 = 0;

		/// <summary>
		/// End position at symbol table.
		/// </summary>
		public int S2 = 0;

		/// <summary>
		/// End position at p-code.
		/// </summary>
		public int P1 = 0;

		/// <summary>
		/// End position at p-code.
		/// </summary>
		public int P2 = 0;

		/// <summary>
		/// Memory stream which keeps compiled script.
		/// </summary>
		MemoryStream buff_stream = null;

		/// <summary>
		/// Name index of module name.
		/// </summary>
		int name_index;

		/// <summary>
		/// Kernel of scripter.
		/// </summary>
		BaseScripter scripter;

		/// <summary>
		/// Constructor.
		/// </summary>
		internal Module(BaseScripter scripter, string name, string language)
		{
			this.name = name;
			this.language = language;
			this.scripter = scripter;
			name_index = scripter.names.Add(name);
		}

		/// <summary>
		/// Returns line of source code.
		/// </summary>
		internal string GetLine(int line_number)
		{
			StringList l = new StringList(true);
			l.text = text;
			if ((line_number >= 0) && (line_number < l.Count))
				return l[line_number];
			else
				return "";
		}

		/// <summary>
		/// Executes before compile stage.
		/// </summary>
		internal void BeforeCompile()
		{
			S1 = scripter.symbol_table.Card + 1;
			P1 = scripter.code.Card + 1;
		}

		/// <summary>
		/// Executes after compile stage.
		/// </summary>
		internal void AfterCompile()
		{
			S2 = scripter.symbol_table.Card;
			P2 = scripter.code.Card;
		}

		/// <summary>
		/// Returns 'true', if id belongs to given module.
		/// </summary>
		internal bool IsInternalId(int id)
		{
			return (id >= S1) && (id <= S2);
		}

		/// <summary>
		/// Saves compiled module to a stream.
		/// </summary>
		internal void SaveToStream(Stream s)
        {
            BinaryWriter bw = new BinaryWriter(s);
            {
                bw.Write(language);
                bw.Write(file_name);
                bw.Write(S1);
                bw.Write(S2);
                bw.Write(P1);
                bw.Write(P2);
                scripter.code.SaveToStream(bw, this);
                scripter.symbol_table.SaveToStream(bw, this);
            }
#if PORTABLE
            bw.Flush();
#else
			bw.Close();
#endif
        }

		/// <summary>
		/// Pre-loads compiled module from a stream.
		/// </summary>
		internal void PreLoadFromStream(Stream s)
		{
			 buff_stream = new MemoryStream((int)s.Length);
#if GE_2008
			 s.CopyTo(buff_stream);
#else
             for (int i = 0; i < s.Length; i++)
             {
                 int b = s.ReadByte();
                 buff_stream.WriteByte((byte)b);
             }
#endif
        }
		/// <summary>
		/// Loads compiled module from a stream.
		/// </summary>
		internal void LoadFromStream()
		{
			buff_stream.Position = 0;
			BinaryReader br = new BinaryReader(buff_stream);
			{
				language = br.ReadString();
				file_name = br.ReadString();
				S1 = br.ReadInt32();
				S2 = br.ReadInt32();
				P1 = br.ReadInt32();
				P2 = br.ReadInt32();

				int ds = scripter.symbol_table.Card - S1 + 1;
				int dp = scripter.code.Card - P1 + 1;

				scripter.code.LoadFromStream(br, this, ds, dp);
				scripter.symbol_table.LoadFromStream(br, this, ds, dp);
			}
#if PORTABLE
#else
			br.Close();
			buff_stream.Close();
#endif
		}

		/// <summary>
		/// Retuns 'true', if it is a source code module.
		/// </summary>
		internal bool IsSourceCodeModule
		{
			get
			{
				return buff_stream != null;
			}
		}

		/// <summary>
		/// Retuns name index of module name.
		/// </summary>
		internal int NameIndex
		{
			get
			{
				return name_index;
			}
		}

		/// <summary>
		/// Retuns source code of script.
		/// </summary>
		public string Text
		{
			get
			{
				return text;
			}
			set
			{
				text = value;
			}
		}

		/// <summary>
		/// Retuns file name which contains source script.
		/// </summary>
		public string FileName
		{
			get
			{
				return file_name;
			}
			set
			{
				file_name = value;
			}
		}

		/// <summary>
		/// Retuns name of module.
		/// </summary>
		public string Name
		{
			get
			{
				return name;
			}
		}

		/// <summary>
		/// Retuns scripting language of module.
		/// </summary>
		public string LanguageName
		{
			get
			{
				return language;
			}
		}

		/// <summary>
		/// Retuns scripting language index of module.
		/// </summary>
		public PaxLanguage Language
		{
			get
			{
				if (PaxSystem.StrEql(language,"CSharp"))
					return PaxLanguage.CSharp;
				else if (PaxSystem.StrEql(language,"Pascal"))
					return PaxLanguage.Pascal;
				else
					return PaxLanguage.VB;
			}
		}
	}
	#endregion Module Class
}
