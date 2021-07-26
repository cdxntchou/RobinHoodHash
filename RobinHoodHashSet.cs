using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;


public class RobinHoodHashSet<T> : RobinHoodInternal<T>
{
    public RobinHoodHashSet(int capacity = 8)
    {
        InitialAllocateInternal(capacity);
    }

    public int Count => element_count;
    public int Capacity => m_values.Length;

    public void Clear()
    {
        ClearInternal();
    }

    int FindItemIndexWithHash(T item, int hash)
    {
        // this searches over all matching hashes in the table, checking for item equality
        int index = FindHashIndexInternal(hash);
        if (index >= 0)
        {
            int capacity = m_values.Length;
            int bucket_mask = capacity - 1;
            do
            {
                // TODO: implement EqualityComparer based key comparison (see Dictionary<> implementation) -- using Object.Equals for now :P
                if (m_values[index].Equals(item))
                {
                    // found it!
                    return index;
                }
                index = (index + 1) & bucket_mask;
            } while (m_hashes[index] == hash);
        }
        return -1;
    }

    int FindItemIndex(T item)
    {
        var hash = item.GetHashCode();
        if (hash == EMPTY_HASH)
            hash = EMPTY_REPLACEMENT_HASH;

        return FindItemIndexWithHash(item, hash);
    }

    public bool Add(T item)
    {
        var hash = item.GetHashCode();
        if (hash == EMPTY_HASH)
            hash = EMPTY_REPLACEMENT_HASH;

        if (FindItemIndexWithHash(item, hash) < 0)
        {
            InsertInternal(hash, item);
            return true;
        }
        return false;
    }

    public bool Contains(T item)
    {
        return (FindItemIndex(item) >= 0);
    }

    // public void CopyTo(T[]);
    // public void CopyTo(T[], Int32);
    // public void CopyTo(T[], Int32, Int32);
    // public static System.Collections.Generic.IEqualityComparer<System.Collections.Generic.HashSet<T>> CreateSetComparer();

    public int EnsureCapacity(int capacity)
    {
        while (Capacity < capacity)
            GrowInternal();
        return Capacity;
    }

    // public void ExceptWith(System.Collections.Generic.IEnumerable<T> other);
    // public System.Collections.Generic.HashSet<T>.Enumerator GetEnumerator();
    // public void IntersectWith(System.Collections.Generic.IEnumerable<T> other);
    // public bool IsProperSubsetOf(System.Collections.Generic.IEnumerable<T> other);
    // public bool IsProperSupersetOf (System.Collections.Generic.IEnumerable<T> other);
    // public bool IsSubsetOf (System.Collections.Generic.IEnumerable<T> other);
    // public bool IsSupersetOf (System.Collections.Generic.IEnumerable<T> other);
    // public bool Overlaps (System.Collections.Generic.IEnumerable<T> other);


    // returns true if the item was removed
    public bool Remove(T item)
    {
        int index = FindItemIndex(item);
        if (index >= 0)
            return RemoveByIndexInternal(index);
        return false;
    }

    public bool TryGetValue(T equalItem, out T actualItem)
    {
        int index = FindItemIndex(equalItem);
        if (index >= 0)
        {
            actualItem = m_values[index];
            return true;
        }
        actualItem = default;
        return false;
    }

    // public void UnionWith(System.Collections.Generic.IEnumerable<T> other);
}


[Serializable]
public class SerializableRobinHoodHashSet<T> : RobinHoodHashSet<T>, ISerializationCallbackReceiver
{
    // serialized data
    [SerializeField]
    T[] values = null;

    void ISerializationCallbackReceiver.OnBeforeSerialize()
    {
        Assert.IsTrue(ValidateInternal());

        // Unity does not serialize null values well
        // so we should strip those out of the serialized data

        // copy from hashes/values to _hashes/_values
        if (element_count > 0)
        {
            values = new T[element_count];

            int write_index = 0;
            for (int i = 0; i < m_hashes.Length; i++)
            {
                if (m_hashes[i] != EMPTY_HASH)
                {
                    values[write_index] = m_values[i];
                    write_index++;
                }
            }

            // sort the serialized data, for deterministic ordering
            // and give the best diff/merge behavior
            Array.Sort(values);
        }
        else
        {
            values = null;
        }
    }

    void ISerializationCallbackReceiver.OnAfterDeserialize()
    {
        // copy from _hashes/_values to hashes/values
        Clear();

        for (int i = 0; i < values.Length; i++)
        {
            Add(values[i]);
        }

        values = null;
    }
}