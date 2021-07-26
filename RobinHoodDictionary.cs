using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;


// C# can't hide inherited functions...  :(
// TODO: should make RobinHoodTableInternal that declares everything protected, so we can derive from that and not get all the public functions

public class RobinHoodDictionary<TKey, TValue> : RobinHoodInternal<KeyValuePair<TKey, TValue>>
{
    // there is an alternate layout of this structure that might be better under some circumstances,
    // where we store the keys and values in separate arrays, but it would require rewriting the internal functions

    public RobinHoodDictionary(int capacity = 8)
    {
        InitialAllocateInternal(capacity);
    }

    public int Count => element_count;
    public int Capacity => m_values.Length;

    protected int FindKeyIndexWithHash(TKey key, int hash)
    {
        // this searches over all matching hashes in the table, checking for key equality
        int index = FindHashIndexInternal(hash);
        if (index >= 0)
        {
            int capacity = m_values.Length;
            int bucket_mask = capacity - 1;
            do
            {
                // TODO: implement EqualityComparer based key comparison (see Dictionary<> implementation) -- using Object.Equals for now :P
                if (m_values[index].Key.Equals(key))
                {
                    // found it!
                    return index;
                }
                index = (index + 1) & bucket_mask;
            } while (m_hashes[index] == hash);
        }
        return -1;
    }

    protected int FindKeyIndex(TKey key)
    {
        var hash = key.GetHashCode();
        if (hash == EMPTY_HASH)
            hash = EMPTY_REPLACEMENT_HASH;

        return FindKeyIndexWithHash(key, hash);
    }

    public void Add(TKey key, TValue value)
    {
        var hash = key.GetHashCode();
        if (hash == EMPTY_HASH)
            hash = EMPTY_REPLACEMENT_HASH;

        if (ContainsKeyWithHash(key, hash))
            throw new ArgumentException(); // an element with the same key already exists
        else
        {
            InsertInternal(hash, new KeyValuePair<TKey, TValue>(key, value));
        }
    }

    public void Clear()
    {
        ClearInternal();
    }

    public bool TryAdd(TKey key, TValue value)
    {
        var hash = key.GetHashCode();
        if (hash == EMPTY_HASH)
            hash = EMPTY_REPLACEMENT_HASH;

        if (!ContainsKeyWithHash(key, hash))
        {
            InsertInternal(hash, new KeyValuePair<TKey, TValue>(key, value));
            return true;
        }
        return false;
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        var hash = key.GetHashCode();
        if (hash == EMPTY_HASH)
            hash = EMPTY_REPLACEMENT_HASH;

        // this searches over all matching hashes in the table, checking for key equality
        int index = FindHashIndexInternal(hash);
        if (index >= 0)
        {
            int capacity = m_values.Length;
            int bucket_mask = capacity - 1;
            do
            {
                // TODO: implement EqualityComparer based key comparison (see Dictionary<> implementation) -- using Object.Equals for now :P
                if (m_values[index].Key.Equals(key))
                {
                    // found it!
                    value = m_values[index].Value;
                    return true;
                }
                index = (index + 1) & bucket_mask;
            } while (m_hashes[index] == hash);
        }
        value = default;
        return false;
    }

    public TValue this[TKey key]
    {
        get
        {
            if (this.TryGetValue(key, out TValue value))
                return value;
            else
                throw new KeyNotFoundException();
        }
//      set;          // TODO
    }

    public bool Remove(TKey key)
    {
        var hash = key.GetHashCode();
        if (hash == EMPTY_HASH)
            hash = EMPTY_REPLACEMENT_HASH;

        // this searches over all matching hashes in the table, checking for key equality
        int index = FindHashIndexInternal(hash);
        if (index >= 0)
        {
            int capacity = m_values.Length;
            int bucket_mask = capacity - 1;
            do
            {
                // TODO: implement EqualityComparer based key comparison (see Dictionary<> implementation) -- using Object.Equals for now :P
                if (m_values[index].Key.Equals(key))
                {
                    // found it!
                    return RemoveByIndexInternal(index);
                }
                index = (index + 1) & bucket_mask;
            } while (m_hashes[index] == hash);
        }
        return false;
    }

    protected bool ContainsKeyWithHash(TKey key, int hash)
    {
        // this searches over all matching hashes in the table, checking for key equality
        int index = FindHashIndexInternal(hash);
        if (index >= 0)
        {
            int capacity = m_values.Length;
            int bucket_mask = capacity - 1;
            do
            {
                // TODO: implement EqualityComparer based key comparison (see Dictionary<> implementation) -- using Object.Equals for now :P
                if (m_values[index].Key.Equals(key))
                {
                    // found it!
                    return true;
                }
                index = (index + 1) & bucket_mask;
            } while (m_hashes[index] == hash);
        }

        return false;
    }

    public bool ContainsKey(TKey key)
    {
        var hash = key.GetHashCode();
        if (hash == EMPTY_HASH)
            hash = EMPTY_REPLACEMENT_HASH;

        return ContainsKeyWithHash(key, hash);
    }

    public bool ContainsValue(TValue value)
    {
        for (int index = 0; index < m_values.Length; index++)
        {
            if (value.Equals(m_values[index]))
                return true;
        }
        return false;
    }

    public int EnsureCapacity(int capacity)
    {
        // there's probably a better way to do this.. :P  especially if there is more than a few powers of two in size here
        while (Capacity < capacity)
            GrowInternal();
        return Capacity;
    }
    // public void TrimExcess(int capacity);        // no way to reduce size at the moment (other than copy to a new one)

    // public Enumerator GetEnumerator();
    // public KeyCollection Keys { get; }
    // public ValueCollection Values { get; }
}