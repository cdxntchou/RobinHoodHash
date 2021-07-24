using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;


// C# can't hide inherited functions...  :(
// TODO: should make RobinHoodTableInternal that declares everything protected, so we can derive from that and not get all the public functions

public class RobinHoodDictionary<TKey, TValue> : RobinHoodTable<KeyValuePair<TKey, TValue>>
{
    // there might be a slightly faster version of this table
    // where we store the keys and values in separate arrays

    public RobinHoodDictionary(int capacity = 8) : base(capacity)
    {
        // table = new RobinHoodTable<KeyValuePair<TKey, TValue>>(capacity);
    }

    // public int Count => table.Count;
    // public int Capacity => table.Capacity;

    public void Add(TKey key, TValue value)
    {
        var hash = key.GetHashCode();
        if (hash == EMPTY_HASH)
            hash = 1;

        // allow duplicates because there could be hash collisions
        base.Add(hash, new KeyValuePair<TKey, TValue>(key, value), allowDuplicates: true);
    }

    //     public void Clear()
    //     {
    //         base.Clear();
    //     }

    public bool TryAdd(TKey key, TValue value)
    {
        var hash = key.GetHashCode();
        if (hash == EMPTY_HASH)
            hash = 1;
        return base.TryAdd(hash, new KeyValuePair<TKey, TValue>(key, value));
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        var hash = key.GetHashCode();
        if (hash == EMPTY_HASH)
            hash = 1;

        // this searches over all matching hashes in the table, checking for key equality
        int index = base.FindHashIndex(hash);
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
            if (TryGetValue(key, out TValue value))
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
            hash = 1;
        return base.Remove(hash);
    }

    public bool ContainsKey(TKey key)
    {
        var hash = key.GetHashCode();
        if (hash == EMPTY_HASH)
            hash = 1;
        return base.ContainsKey(hash);
    }

//    public bool ContainsValue(TValue value)       // needs general iterator

    public int EnsureCapacity(int capacity)
    {
        // there's probably a better way to do this.. :P
        while (Capacity < capacity)
            base.Grow();
        return Capacity;
    }

    // public void TrimExcess(int capacity);        // no way to reduce size at the moment (other than copy to a new one)

    // public Enumerator GetEnumerator();
    // public KeyCollection Keys { get; }
    // public ValueCollection Values { get; }
}