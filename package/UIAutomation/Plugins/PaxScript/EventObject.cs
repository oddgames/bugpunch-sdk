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
using System.Reflection;

namespace PaxScript.Net
{
	#region EventObject Class
	/// <summary>
	/// Represents event definition.
	/// </summary>
	internal class EventObject: MemberObject
	{
		/// <summary>
		/// EventInfo of host-defined event.
		/// </summary>
		public EventInfo Event_Info = null;

		/// <summary>
		/// Type info of host-defined owner type.
		/// </summary>
		public Type OwnerType = null;

		/// <summary>
		/// Id of Add accessor.
		/// </summary>
		public int AddId = 0;

		/// <summary>
		/// Id of Remove accessor.
		/// </summary>
		public int RemoveId = 0;

		/// <summary>
		/// Id of private field saves event delegate.
		/// </summary>
		public int EventFieldId = 0;

		/// <summary>
		/// Constructor.
		/// </summary>
		internal EventObject(BaseScripter scripter, int event_id, int owner_id)
		: base(scripter, event_id, owner_id)
		{
		}
	}
	#endregion EventObject Class
}

