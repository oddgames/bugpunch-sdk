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
	#region PropertyObject Class
	/// <summary>
	/// Represents definition of property.
	/// </summary>
	internal class PropertyObject: MemberObject
	{
		/// <summary>
		/// Value of property.
		/// </summary>
		object value;

		/// <summary>
		/// PropertyInfo of host-defined property.
		/// </summary>
		public PropertyInfo Property_Info = null;

		/// <summary>
		/// Type info of owner of host-defined property.
		/// </summary>
		public Type OwnerType = null;


		/// <summary>
		/// Id of the read accessor.
		/// </summary>
		public int ReadId = 0;

		/// <summary>
		/// Id of the write accessor.
		/// </summary>
		public int WriteId = 0;

		/// <summary>
		/// Number of parametes.
		/// </summary>
		public int ParamCount = 0;

		/// <summary>
		/// Sign of default property.
		/// </summary>
		public bool IsDefault = false;

		/// <summary>
		/// List of parameters.
		/// </summary>
		object[] p;

		/// <summary>
		/// Constructor.
		/// </summary>
		internal PropertyObject(BaseScripter scripter, int property_id, int owner_id, int param_count)
		: base(scripter, property_id, owner_id)
		{
			ParamCount = param_count;
			p = new object[ParamCount];
		}

		/// <summary>
		/// Returns 'true', if given property is indexer.
		/// </summary>
		internal bool IsIndexer
		{
			get
			{
				return ParamCount > 0;
			}
		}

		/// <summary>
		/// Returns value of property.
		/// </summary>
		internal object Value
		{
			get
			{
				if ((Imported) && (Static))
					return Property_Info.GetValue(OwnerType, p);
				else
					return value;
			}
			set
			{
				if ((Imported) && (Static))
					Property_Info.SetValue(OwnerType, value, p);
				else
					this.value = value;
			}
		}
	}
	#endregion PropertyObject Class
}

