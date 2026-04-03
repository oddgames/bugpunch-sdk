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
	#region ParserList Class
	/// <summary>
	/// Represents list of parsers registered by scripter.
	/// </summary>
	internal class ParserList
	{
		/// <summary>
		/// List of parsers.
		/// </summary>
		PaxArrayList fItems;

		/// <summary>
		/// Constructor.
		/// </summary>
		public ParserList()
		{
			fItems = new PaxArrayList();
		}

		/// <summary>
		/// Adds new parser.
		/// </summary>
		public int Add(BaseParser p)
		{
			return fItems.Add(p);
		}

		/// <summary>
		/// Returns parser by scripting language name.
		/// </summary>
		public BaseParser FindParser(string language)
		{
			int i;
			BaseParser p;

			for (i=0; i<fItems.Count; i++)
			{
				p = (BaseParser) fItems[i];
				if (p.language == language)
					return p;
			}
			return null;
		}
	}
	#endregion ParserList Class
}
