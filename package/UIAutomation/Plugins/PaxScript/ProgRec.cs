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
	#region ProgRec Class
	/// <summary>
	/// Represents p-code instruction.
	/// </summary>
	public sealed class ProgRec
	{
		/// <summary>
		/// Operation.
		/// </summary>
		public int op = 0;

		/// <summary>
		/// Id of the first argument.
		/// </summary>
		public int arg1 = 0;

		/// <summary>
		/// Id of the second argument.
		/// </summary>
		public int arg2 = 0;

		/// <summary>
		/// Id of result.
		/// </summary>
		public int res = 0;

		public int FinalNumber = 0;

		public int tag;

		public Upcase upcase;

        public bool labeled = false;

		/// <summary>
		/// Resets p-code instruction.
		/// </summary>
		internal void Reset()
		{
			op = 0;
			arg1 = 0;
			arg2 = 0;
			res = 0;
			FinalNumber = 0;
			tag = 0;
			labeled = false;
			upcase = Upcase.None;
		}

		internal ProgRec Clone()
		{
			ProgRec result = new ProgRec();
			result.op = op;
			result.arg1 = arg1;
			result.arg2 = arg2;
			result.res = res;
			result.upcase = upcase;
			return result;
		}

		/// <summary>
		/// Saves p-code instruction to a stream.
		/// </summary>
		internal void SaveToStream(BinaryWriter bw)
		{
			bw.Write(op);
			bw.Write(arg1);
			bw.Write(arg2);
			bw.Write(res);
			bw.Write((int)upcase);
		}

		/// <summary>
		/// Restores p-code instruction from a stream.
		/// </summary>
		internal void LoadFromStream(BinaryReader br)
		{
			op = br.ReadInt32();
			arg1 = br.ReadInt32();
			arg2 = br.ReadInt32();
			res = br.ReadInt32();
			upcase = (Upcase) br.ReadInt32();
		}
	}
	#endregion ProgRec Class
}
