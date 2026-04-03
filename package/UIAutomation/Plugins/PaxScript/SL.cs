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

#if SILVERLIGHT
#if PORTABLE
#else
using System.Windows.Controls;
#endif


namespace SL
{
#if PORTABLE
    public class PaxComponent 
    {
    }
#else
    public class PaxComponent : Control
    {
    }
#endif

public class PaxArrayList: IList, IEnumerator, IEnumerable
{
	private object[] content = null;
	private int count;
	private int delta = 64;
	int pos = -1;

	public PaxArrayList()
	{
		count = 0;
	}

	public bool MoveNext()
	{
		if (pos < Count - 1)
		{
			pos ++;
			return true;
		}
		else
		{
			Reset();
			return false;
		}
	}

	public void Reset()
	{
		pos = -1;
	}

	public object Current
	{
		get
		{
			return content[pos];
		}
	}

	public IEnumerator GetEnumerator()
	{
		return (IEnumerator) this;
	}

	public void Clear()
	{
		content = null;
		count = 0;
	}

	public bool IsFixedSize
	{
		get
		{
			 return true;
		}
	}

	public bool IsReadOnly
	{
		get
		{
			 return false;
		}
	}

	public void CopyTo(Array array, int index)
	{
		int j = index;
		for (int i = 0; i < Count; i++)
		{
			array.SetValue(content[i], j);
			j++;
		}
	}

	public bool IsSynchronized
	{
		get
		{
			return false;
		}
	}

	public object SyncRoot
	{
		get
		{
			return this;
		}
	}

	public virtual Object Clone()
	{
		PaxArrayList result = new PaxArrayList();
		result.count = count;
		result.delta = delta;
		result.content = new object[content.Length];
		content.CopyTo(result.content, 0);
		return result;
	}

	private void Grow()
	{
		if (content == null)
		{
			content = new object[delta];
			return;
		}

		object[] temp = new object[count + delta];
		content.CopyTo(temp, 0);
		content = temp;
	}

	public virtual int IndexOf(object value)
	{
		for (int i = 0; i < Count; i++)
		{
			object v = content[i];
			if (Comp.eq(v, value))
				return i;
		}
		return -1;
	}

	public virtual bool Contains(object value)
	{
		for (int i = 0; i < Count; i++)
			if (Comp.eq(content[i], value))
				return true;
		return false;
	}

	public virtual int Add(Object value)
	{

		if (count == 0)
		{
			content = new object[delta];
			content[0] = value;
			count = 1;
			return 0;
		}

		if (count % delta == 0)
			Grow();

		content[count] = value;
		count++;
		return count - 1;
	}

	public virtual void Insert(int index, Object value)
	{
		if (count % delta == 0)
			Grow();

		if ((index < count) && (index >= 0))
		{
			count++;

			for (int i = Count - 1; i > index; i--)
			{
				content[i] = content[i - 1];
			}
			content[index] = value;
		}
	}

	public void Remove(object value)
	{
		RemoveAt(IndexOf(value));
	}

	public virtual void RemoveAt(int index)
	{
		if ((index >= 0) && (index < Count))
		{
			for (int i = index; i < Count - 1; i++)
			{
				content[i] = content[i + 1];
			}
			count--;
		}
	}

	public int Count
	{
		get
		{
			return count;
		}
	}

	public object this[int index]
	{
		get
		{
			return content[index];
		}
		set
		{
			content[index] = value;
		}
	}

}

public class PaxHashTableRec
{
	public object key;
	public object value;

	public PaxHashTableRec(object akey, object avalue)
	{
		key = akey;
		value = avalue;
	}
}

public class PaxHashTable: IEnumerator, IEnumerable
{
	const int MaxHash = 199;
	object[] a = new object[MaxHash + 1];
	int pos_i = -1;
	int pos_j = -1;
	int count = 0;

	public PaxHashTable()
	{
	}

	public bool MoveNext()
	{
		if ((pos_i == -1) && (pos_j ==-1))
		{
			for (int i = 0; i <= MaxHash; i++)
			{
				if (a[i] != null)
				{
					pos_i = i;
					pos_j = 0;
					return true;
				}
			}
			Reset();
			return false;
		}
		else
		{
			PaxArrayList ai = a[pos_i] as PaxArrayList;
			if (pos_j < ai.Count - 1)
			{
				pos_j++;
				return true;
			}

			for (int i = pos_i + 1; i <= MaxHash; i++)
			{
				if (a[i] != null)
				{
					pos_i = i;
					pos_j = 0;
					return true;
				}
			}

			Reset();
			return false;
		}
	}

	public void Reset()
	{
		pos_i = -1;
		pos_j = -1;
	}

	public object Current
	{
		get
		{
			PaxArrayList ai = a[pos_i] as PaxArrayList;
			if (ai == null)
				return null;

			return (ai[pos_j] as PaxHashTableRec).key;
		}
	}

	public IEnumerator GetEnumerator()
	{
		return (IEnumerator) this;
	}

	public virtual void Add(Object key, Object value)
	{
		int h = key.GetHashCode() % MaxHash;
		if (h < 0) h = - h;

		if (a[h] == null)
		  a[h] = new PaxArrayList();

		PaxHashTableRec r = new PaxHashTableRec(key, value);
		(a[h] as PaxArrayList).Add(r);
		count++;
	}


	public virtual void Remove(Object key)
	{
		int h = key.GetHashCode() % MaxHash;
		if (h < 0) h = - h;

		if (a[h] == null)
			return;
		PaxArrayList temp = a[h] as PaxArrayList;
		if (temp.Contains(key))
		{
			temp.Remove(key);
			count--;
		}
	}

	public virtual bool Contains(Object key)
	{
		int h = key.GetHashCode() % MaxHash;
		if (h < 0) h = - h;

		if (a[h] == null)
			return false;

		PaxArrayList temp = a[h] as PaxArrayList;
		for (int j = 0; j < temp.Count; j++)
		{
			PaxHashTableRec r = temp[j] as PaxHashTableRec;
			if (Comp.eq(r.key, key))
				return true;
		}
		return false;
	}

	public virtual bool ContainsKey(Object key)
	{
		int h = key.GetHashCode() % MaxHash;
		if (h < 0) h = - h;

		if (a[h] == null)
			return false;

		PaxArrayList temp = a[h] as PaxArrayList;
		for (int j = 0; j < temp.Count; j++)
		{
			PaxHashTableRec r = temp[j] as PaxHashTableRec;
			if (Comp.eq(r.key, key))
				return true;
		}
		return false;
	}

	public virtual void Clear()
	{
		for (int i = 0; i <= MaxHash; i++)
			a[i] = null;
		count = 0;
		Reset();
	}

	public int Count
	{
		get
		{
			return count;
		}
	}

	public PaxHashTable Keys
	{
		get
		{
			return this;
		}
	}

	public object this[object key]
	{
		get
		{
			int h = key.GetHashCode() % MaxHash;
			if (h < 0) h = - h;

			if (a[h] == null)
				return null;

			PaxArrayList temp = a[h] as PaxArrayList;
			for (int j = 0; j < temp.Count; j++)
			{
				PaxHashTableRec r = temp[j] as PaxHashTableRec;
				if (Comp.eq(r.key, key))
					return r.value;
			}
			return null;
		}
		set
		{
			int h = key.GetHashCode() % MaxHash;
			if (h < 0) h = - h;

			if (a[h] == null)
				return;

			PaxArrayList temp = a[h] as PaxArrayList;
			for (int j = 0; j < temp.Count; j++)
			{
				PaxHashTableRec r = temp[j] as PaxHashTableRec;
				if (Comp.eq(r.key, key))
				{
					r.value = value;
					return;
				}
			}
		}
	}

}

public class Comp
{
	public static bool eq(object v1, object v2)
	{
		Type t1 = v1.GetType();
		Type t2 = v2.GetType();
		if (t1 == typeof(int) && t2 == typeof(int))
		{
			int i1 = (int) v1;
			int i2 = (int) v2;
			return i1 == i2;
		}
		else if (t1 == typeof(string) && t2 == typeof(string))
		{
			string i1 = (string) v1;
			string i2 = (string) v2;
			return i1 == i2;
		}
		else
			return v1 == v2;
	}
}
}

#else
using System.ComponentModel;

namespace SL
{

    public class PaxComponent : Component
    {
    }

    public class PaxArrayList : ArrayList
    {

    }
    public class PaxHashTable : Hashtable
    {

    }
}

#endif