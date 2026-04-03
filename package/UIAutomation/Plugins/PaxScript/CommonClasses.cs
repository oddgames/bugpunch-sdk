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
using System.Collections;
using SL;

namespace PaxScript.Net
{
	#region TypedList Class
	/// <summary>
	/// Represents associative PaxArrayList.
	/// Each element of list contains pair (item, object).
	/// </summary>
	public class TypedList: IEnumerator, IEnumerable
	{
		/// <summary>
		/// List of first members in pairs (item, object).
		/// </summary>
		private PaxArrayList fItems;

		/// <summary>
		/// List of second members in pairs (item, object).
		/// </summary>
		private PaxArrayList fObjects;

		/// <summary>
		/// If 'true', list can contain duplicated items.
		/// </summary>
		public bool DupYes;

		/// <summary>
		/// It is necessary for IEnumerator implementation.
		/// </summary>
		int pos = -1;

		// IEnumerator

		/// <summary>
		/// Implements MoveNext of IEnumerator.
		/// </summary>
		public bool MoveNext()
		{
			if (pos < Items.Count - 1)
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

		/// <summary>
		/// Implements Reset of IEnumerator.
		/// </summary>
		public void Reset()
		{
			pos = -1;
		}

		/// <summary>
		/// Implements Current of IEnumerator.
		/// </summary>
		public object Current
		{
			get
			{
				return Items[pos];
			}
		}

		// IEnumerable

		/// <summary>
		/// Implements GetEnumerator of IEnumerable.
		/// </summary>
		public IEnumerator GetEnumerator()
		{
			return (IEnumerator) this;
		}

		/// <summary>
		/// Constructor
		/// </summary>
		public TypedList(bool dupyes)
		{
			this.DupYes = dupyes;
			fItems = new PaxArrayList();
			fObjects = new PaxArrayList();
		}

		/// <summary>
		/// Returns list of items (first elements in pairs).
		/// </summary>
		public PaxArrayList Items
		{
			get
			{
				return fItems;
			}
		}

		/// <summary>
		/// Returns list of objects (second elements in pairs).
		/// </summary>
		public PaxArrayList Objects
		{
			get
			{
				return fObjects;
			}
		}

		/// <summary>
		/// Returns number of pairs.
		/// </summary>
		public int Count
		{
			get
			{
				return fItems.Count;
			}
		}

		/// <summary>
		/// Adds pair (avalue, null) to list.
		/// </summary>
		public int Add(object avalue)
		{
			if (DupYes)
			{
				fItems.Add(avalue);
				fObjects.Add(null);
				return fItems.Count - 1;
			}
			else
			{
				int i = fItems.IndexOf(avalue);
				if (i == -1)
				{
					fItems.Add(avalue);
					fObjects.Add(null);
					return fItems.Count - 1;
				}
				else
					return i;
			}
		}

		/// <summary>
		/// Inserts pair (avalue, null) into list.
		/// </summary>
		public int Insert(int index, object avalue)
		{
			if (DupYes)
			{
				fItems.Insert(index, avalue);
				fObjects.Insert(index, null);
				return fItems.Count - 1;
			}
			else
			{
				int i = fItems.IndexOf(avalue);
				if (i == -1)
				{
					fItems.Insert(index, avalue);
					fObjects.Insert(index, null);
					return fItems.Count - 1;
				}
				else
					return i;
			}
		}

		/// <summary>
		/// Adds pair (avalue, anObject) to list.
		/// </summary>
		public int AddObject(object avalue, object anObject)
		{
			int i = Add(avalue);
			fObjects[i] = anObject;
			return i;
		}

		/// <summary>
		/// Removes pair [i].
		/// </summary>
		public void RemoveAt(int i)
		{
			fItems.RemoveAt(i);
			fObjects.RemoveAt(i);
		}

		/// <summary>
		/// Removes all pairs from list.
		/// </summary>
		public void Clear()
		{
			fItems.Clear();
			fObjects.Clear();
		}

		/// <summary>
		/// Returns index of item.
		/// </summary>
		public int IndexOf(object value)
		{
			return Items.IndexOf(value);
		}
	}

	#endregion TypedList Class

	#region StringList Class
	/// <summary>
	/// Represents associative list of strings.
	/// Each element of list contains pair (System.String, System.Object).
	/// </summary>
	public class StringList: TypedList
	{
		/// <summary>
		/// PaxHashTable which allows to speed up access to
		/// elements of list.
		/// </summary>
		PaxHashTable ht;

		/// <summary>
		/// Constructor.
		/// </summary>
		public StringList(bool dupyes): base(dupyes)
		{
			ht = new PaxHashTable();
		}

		/// <summary>
		/// Removes all pairs from list.
		/// </summary>
		public new void Clear()
		{
			base.Clear();
			ht.Clear();
		}

		/// <summary>
		/// Adds pair (avalue, anObject) to list.
		/// </summary>
		public int AddObject(string avalue, object anObject)
		{
			int i = Add(avalue);
			Objects[i] = anObject;
			return i;
		}

		/// <summary>
		/// Creates copy of object.
		/// </summary>
		public StringList Clone()
		{
			StringList result = new StringList(DupYes);
			for (int i = 0; i < Count; i++)
				result.AddObject(Items[i], Objects[i]);
			return result;
		}

		/// <summary>
		/// Adds pair (avalue, null) to list.
		/// </summary>
		public int Add(string avalue)
		{
			if (DupYes)
			{
				Items.Add(avalue);
				Objects.Add(null);

				return Items.Count - 1;
			}
			else
			{
				int i = IndexOf(avalue);
				if (i == -1)
				{
					ht.Add(avalue, Items.Count);

					Items.Add(avalue);
					Objects.Add(null);
					return Items.Count - 1;
				}
				else
					return i;
			}
		}

		/// <summary>
		/// Removes the string specified by the index parameter.
		/// </summary>
		public void Delete(int index)
		{
			if (index >= 0 && index < Count)
			{
				Items.RemoveAt(index);
				Objects.RemoveAt(index);
				ht.Clear();
				for (int i = 0; i < Count; i++)
					ht.Add(Items[i], i);
			}
		}

		/// <summary>
		/// Returns index of a pair which contains string s.
		/// </summary>
		public int IndexOf(string s)
		{
			if (DupYes)
			{
				for (int i = 0; i < Items.Count; i++)
				{
					if (Items[i].ToString() == s) return i;
				}
				return -1;
			}
			else
			{
				object v = ht[s];
				if (v == null)
					return - 1;
				else
					return (int) v;
			}
		}

		/// <summary>
		/// Returns index of a pair which contains string s in uppercase.
		/// </summary>
		public int UpcaseIndexOf(string s)
		{
			string upcase_s = s.ToUpper();

			for (int i = 0; i < Items.Count; i++)
			{
				if (Items[i].ToString().ToUpper() == upcase_s) return i;
			}
			return -1;
		}
#if !PORTABLE
		/// <summary>
		/// Loads list from file.
		/// </summary>
		public void LoadFromFile(string path)
		{
			Clear();
			using (StreamReader sr = new StreamReader(path))
			{
				while (sr.Peek() >= 0)
				{
					string s = sr.ReadLine();
					Add(s);
				}
			}
		}
#endif
#if !PORTABLE
		/// <summary>
		/// Saves list to file.
		/// </summary>
		public void SaveToFile(string path)
		{
			StreamWriter t = File.CreateText(path);
			for (int i=0; i < Count; i++)
			{
				t.WriteLine(Items[i].ToString());
			}
			t.Close();
		}
#endif
		/// <summary>
		/// Undocumented.
		/// </summary>
		public void Dump(string path)
		{
		#if dump

			StreamWriter t = File.CreateText(path);
			for (int i=0; i < Count; i++)
			{
				t.WriteLine(i.ToString() + ":" + this[i]);
			}
			t.Close();
		#endif
		}

		/// <summary>
		/// Returns string by index.
		/// </summary>
		public string this[int index]
		{
			get
			{
				if (Items[index] == null)
					return "*null";
				else
					return Items[index].ToString();
			}
		}

		/// <summary>
		/// Returns concatenation of all strings in list.
		/// </summary>
		public string text
		{
			set
			{
				Clear();
				int l = value.Length;
				if (l == 0)
					return;

                int i = 0;
                int start = i;
                for (;;)
                {
                     if (value[i] == '\r')
                     {
                         i++;
                         if (i < l) 
                         {
                             if (value[i] == '\n')
                             {
                                   string s = value.Substring(start, i - start - 1);
                                   Add(s);
                                   i++;
                                   start = i;
                                   if (i >= l)
                                       break;
                             }
                             else
                                 break;
                         }
                         else
                             break;
                     }
                     else if (value[i] == '\n')
                     {
                         int len = i - start - 1;
                         string s;
                         if (len > 0)
                             s = value.Substring(start, len);
                         else
                             s = "";
                         Add(s);
                         i++;
                         start = i;
                         if (i >= l)
                             break;
                     }
                     else
                     {
                         i++;
                         if (i >= l)
                         {
                             string s = value.Substring(start, i - start - 1);
                             Add(s);
                             break;
                         }
                     }
                }
			}
			get
			{
				string result = "";
				for (int i=0; i < Count; i++)
				{
					result += Items[i].ToString() + "\n\r";
				}
				return result;
			}
		}
	}
	#endregion StringList Class

	#region IntegerList Class
	/// <summary>
	/// Represents associative list of integers.
	/// Each element of list contains pair (System.Int32, System.Object).
	/// </summary>
	public class IntegerList: TypedList
	{
		/// <summary>
		/// Constructor.
		/// </summary>
		public IntegerList(bool dupyes): base(dupyes)
		{
		}

		/// <summary>
		/// Adds pair (avalue, null) to list.
		/// </summary>
		public int Add(int avalue)
		{
			return base.Add(avalue);
		}

		/// <summary>
		/// Inserts pair (avalue, null) into list.
		/// </summary>
		public int Insert(int index, int avalue)
		{
			return base.Insert(index, avalue);
		}

		/// <summary>
		/// Adds list l to given list.
		/// </summary>
		public void AddFrom(IntegerList l)
		{
			for (int i = 0; i < l.Count; i++)
				AddObject(l.Items[i], l.Objects[i]);
		}

		/// <summary>
		/// Adds pair (avalue, anObject) to list.
		/// </summary>
		public int AddObject(int avalue, object anObject)
		{
			return base.AddObject(avalue, anObject);
		}

		/// <summary>
		/// Returns index of a pair which contains avalue.
		/// </summary>
		public int IndexOf(int value)
		{
			return Items.IndexOf(value);
		}

		/// <summary>
		/// Deletes a pair which contains avalue from list.
		/// </summary>
		public void DeleteValue(int avalue)
		{
			int i = IndexOf(avalue);
			if (i != -1)
				Items.RemoveAt(i);
		}

		/// <summary>
		/// Creates duplicate list of the given list.
		/// </summary>
		public IntegerList Clone()
		{
			IntegerList result = new IntegerList(DupYes);
			for (int i = 0; i < Count; i++)
				result.Add(this[i]);
			return result;
		}

		/// <summary>
		/// Returns value of last pair (value, object) from list.
		/// </summary>
		public int Last
		{
			get
			{
				return this[Count - 1];
			}
			set
			{
				this[Count - 1] = value;
			}
		}

		/// <summary>
		/// Returns value of pair (value, object) with index i.
		/// </summary>
		public int this[int i]
		{
			get
			{
				return (int) Items[i];
			}
			set
			{
				Items[i] = (int) value;
			}
		}
	}
	#endregion IntegerList Class

	public class AssocIntegers
	{
		public int Count;
		public int [] Items1;
		public int [] Items2;

		public AssocIntegers(int max_card)
		{
			Count = 0;
			Items1 = new int[max_card];
			Items2 = new int[max_card];
		}

		public void Add(int v1, int v2)
		{
			if (Count == Items1.Length)
				Errors.RaiseException("Overflow in AssocIntegers object");

			Items1[Count] = v1;
			Items2[Count] = v2;
			++ Count;
		}

		public void AddFrom(AssocIntegers a)
		{
			for (int i = 0; i < a.Count; i++)
				Add(a.Items1[i], a.Items2[i]);
		}
	}

	public class SimpleIntegerList
	{
		public int Count;
		public int [] items;

		public SimpleIntegerList(int max_card)
		{
			Count = 0;
			items = new int[max_card];
		}

		public void Add(int value)
		{
			if (Count == items.Length)
				Errors.RaiseException("Overflow in SimpleIntegerList object");

			items[Count] = value;
			++ Count;
		}

		public void Clear()
		{
			Count = 0;
		}

		public int this[int i]
		{
			get
			{
				return items[i];
			}
			set
			{
				items[i] = value;
			}
		}
	}

	public class SimpleIntegerStack: SimpleIntegerList
	{
		public SimpleIntegerStack(int max_card): base(max_card)
		{
		}

		public void Push(int value)
		{
			if (Count == items.Length)
				Errors.RaiseException("Overflow in SimpleIntegerStack object");

			items[Count] = value;
			++ Count;
		}

		public void Pop()
		{
			-- Count;
		}

		public int Peek()
		{
			return items[Count - 1];
		}
	}

	#region IntegerStack Class
	/// <summary>
	/// Represents stack of integer values.
	/// Each element of stack contains pair (System.Int32, System.Object).
	/// </summary>
	public class IntegerStack: IntegerList
	{
		/// <summary>
		/// Constructor.
		/// </summary>
		public IntegerStack(): base(true)
		{
		}

		/// <summary>
		/// Pushes pair (i, null) into stak.
		/// </summary>
		public void Push(int i)
		{
			Add(i);
		}

		/// <summary>
		/// Pushes pair (i, anObject) into stack.
		/// </summary>
		public void PushObject(int i, object anObject)
		{
			AddObject(i, anObject);
		}

		/// <summary>
		/// Returns value of topmost pair (value, object)
		/// </summary>
		public int Peek()
		{
			return (int) Items[Count - 1];
		}

		/// <summary>
		/// Returns object of topmost pair (value, object)
		/// </summary>
		public object PeekObject()
		{
			return (object) Objects[Count - 1];
		}

		/// <summary>
		/// Pops topmost pair from stack.
		/// </summary>
		public int Pop()
		{
			int result = Peek();
			RemoveAt(Count - 1);
			return result;
		}

		/// <summary>
		/// Returns duplicate of the given stack.
		/// </summary>
		public new IntegerStack Clone()
		{
			IntegerStack result = new IntegerStack();
			for (int i = 0; i < Count; i++)
				result.Add(Items[i]);
			return result;
		}
	}
	#endregion IntegerStack Class

	#region ObjectStack Class
	/// <summary>
	/// Represents stack of System.Object values.
	/// Each element of stack contains pair (System.Int32, System.Object).
	/// </summary>
	public class ObjectStack: TypedList
	{
		/// <summary>
		/// Constructor.
		/// </summary>
		public ObjectStack(): base(true)
		{
		}

		/// <summary>
		/// Pushes pair (v, null) into stack.
		/// </summary>
		public void Push(object v)
		{
			Add(v);
		}

		/// <summary>
		/// Pushes pair (v, anObject) into stack.
		/// </summary>
		public void PushObject(object v, object anObject)
		{
			AddObject(v, anObject);
		}

		/// <summary>
		/// Returns value of topmost pair (value, object)
		/// </summary>
		public object Peek()
		{
			return Items[Count - 1];
		}

		/// <summary>
		/// Returns object of topmost pair (value, object)
		/// </summary>
		public object PeekObject()
		{
			return Objects[Count - 1];
		}

		/// <summary>
		/// Pops topmost pair from stack.
		/// </summary>
		public object Pop()
		{
			object result = Peek();
			RemoveAt(Count - 1);
			return result;
		}

		/// <summary>
		/// Returns duplicate of the given stack.
		/// </summary>
		public ObjectStack Clone()
		{
			ObjectStack result = new ObjectStack();
			for (int i = 0; i < Count; i++)
				result.Add(Items[i]);
			return result;
		}
	}
	#endregion ObjectStack Class
}

