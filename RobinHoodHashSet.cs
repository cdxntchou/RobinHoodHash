using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;

// RobinHoodTable is effectively a MultiValueDictionary<int, T>
//      where the key is assumed to be a non-zero, well-behaved hash code that doesn't require further hashing
//
// internally stores the values in a robin-hood hash table structure.
// assuming good hash distribution on the key, this gives us quick and cache-friendly search
// and enumeration of values matching any key
//
// Uses 0 as a special-case hash key to indicate an empty slot -- so 0 is not allowed as a hash key.
//
// This implementation does not use tombstones, it will properly re-order elements on deletion.
// This means the dictionary is always in a deterministic order based on it's element hashes and size.
//
// When growing the hash table, we don't use the standard "insert all elements to a new table" approach.
// Rather we resize the arrays in place, and then do a sweep to move elements to their new positions.
// This has two benefits: 
//   1) for large table sizes, the resize in place can be much faster than allocating a new table (assuming your allocator is smart)
//   2) the sweep is more memory coherent, and guarantees each element is moved once,
//      whereas the repeated-insert can move each element many times,
//      particularly when there are a lot of hash collisions.



//[Serializable]  // TODO
public class RobinHoodTable<TValue> : RobinHoodInternal<TValue>
{
    public RobinHoodTable(int capacity = 8)
    {
        InitialAllocateInternal(capacity);
    }

    public int Count => element_count;
    public int Capacity => m_values.Length;

    public void Clear()
    {
        ClearInternal();
    }

    public bool ContainsKey(int key)
    {
        return (FindHashIndexInternal(key) != -1);
    }

    public void Add(int key, TValue item, bool allowDuplicates = false)
    {
        if (!allowDuplicates && (FindHashIndexInternal(key) >= 0))
            throw new ArgumentException(); // an element with the same key already exists
        else
            InsertInternal(key, item);
    }

    public bool TryAdd(int key, TValue item)
    {
        if (FindHashIndexInternal(key) < 0)
        {
            InsertInternal(key, item);
            return true;
        }
        return false;
    }

    public bool Remove(int key)
    {
        int index = FindHashIndexInternal(key);
        if (index >= 0)
            return RemoveByIndexInternal(index);
        return false;
    }

    public bool TryGetValue(int key, out TValue value)
    {
        int index = FindHashIndexInternal(key);
        if (index >= 0)
        {
            value = m_values[index];
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }

    // this is how we iterate all values matching a key
    public IEnumerable<TValue> GetValues(int key)
    {
        int index = FindHashIndexInternal(key);
        if (index >= 0)
        {
            int capacity = m_values.Length;
            int bucket_mask = capacity - 1;
            do
            {
                yield return m_values[index];
                index = (index + 1) & bucket_mask;
            } while (m_hashes[index] == key);
        }
    }
}


[Serializable]
public class SerializableRobinHoodTable<TValue> : RobinHoodTable<TValue>, ISerializationCallbackReceiver
{
    // serialized data
    [SerializeField]
    int[] hashes = null;

    [SerializeField]
    TValue[] values = null;


    void ISerializationCallbackReceiver.OnBeforeSerialize()
    {
        Assert.IsTrue(ValidateInternal());

        // Unity does not serialize null values well
        // so we should strip those out of the serialized data

        // copy from hashes/values to _hashes/_values
        if (element_count > 0)
        {
            hashes = new int[element_count];
            values = new TValue[element_count];

            int write_index = 0;
            for (int i = 0; i < m_hashes.Length; i++)
            {
                if (m_hashes[i] != EMPTY_HASH)
                {
                    hashes[write_index] = m_hashes[i];
                    values[write_index] = m_values[i];
                    write_index++;
                }
            }

            // sort the serialized data, for deterministic ordering
            // and give the best diff/merge behavior
            Array.Sort(hashes, values);
        }
        else
        {
            hashes = null;
            values = null;
        }
    }

    void ISerializationCallbackReceiver.OnAfterDeserialize()
    {
        // copy from _hashes/_values to hashes/values
        Clear();

        for (int i = 0; i < hashes.Length; i++)
        {
            if (hashes[i] != EMPTY_HASH)
            {
                Add(hashes[i], values[i]);
            }
        }

        hashes = null;
        values = null;
    }
}