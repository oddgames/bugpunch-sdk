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
	#region FieldObject Class
	/// <summary>
	/// Represents field definition.
	/// </summary>
	internal class FieldObject: MemberObject
	{
		/// <summary>
		/// Value of field.
		/// </summary>
		object value;

		/// <summary>
		/// FieldInfo of host-defined field.
		/// </summary>
		public FieldInfo Field_Info;

		/// <summary>
		/// Type info of host-defined field owner.
		/// </summary>
		public Type OwnerType;

		/// <summary>
		/// Constructor.
		/// </summary>
		internal FieldObject(BaseScripter scripter, int field_id, int owner_id)
		: base(scripter, field_id, owner_id)
		{
			Field_Info = null;
			OwnerType = null;
		}

		/// <summary>
		/// Returns field value.
		/// </summary>
		public object Value
		{
			get
			{
				if ((Imported) && (Static))
					return Field_Info.GetValue(OwnerType);
				else
					return value;
			}
			set
			{
				if ((Imported) && (Static))
					Field_Info.SetValue(OwnerType, value);
				else
					this.value = value;
			}
		}
	}
	#endregion FieldObject Class
}
