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
public class RobinHoodTable<TValue> // : ISerializationCallbackReceiver
{
    // there are at least three possible layouts for the memory:
    //    int[], T[]            // better packing, memory coherent searches and key iteration, but access second cache line to grab value
    //    Tuple<int, T>[]       // worse packing, searches and key iteration may have large stride, less array overhead, probably fastest option for int-sized T
    //    T[]                   // best packing, least memory, but requires many calls to GetHashCode() which could be (very) slow depending on implementation
    // we choose the first one as the simplest and best general-case behavior

    // runtime data
    protected int[] m_hashes = null;
    protected TValue[] m_values = null;

    protected int element_count = 0;          // number of elements contained in the hash table
    protected int resize_threshold = 0;       // element_count threshold at which we will resize to the next larger power of 2

    public const int EMPTY_HASH = 0;

    public RobinHoodTable(int capacity = 8)
    {
        InitialAllocate(capacity);
    }

    public int Count => element_count;
    public int Capacity => m_values.Length;

    public void Clear()
    {
        if (element_count > 0)
        {
            element_count = 0;
            for (int i = 0; i < m_hashes.Length; i++)
            {
                m_hashes[i] = EMPTY_HASH;
                m_values[i] = default;
            }
        }
    }

    public bool ContainsKey(int key)
    {
        return (FindHashIndex(key) != -1);
    }

    public void Add(int key, TValue item, bool allowDuplicates = false)
    {
        if (!allowDuplicates && (FindHashIndex(key) >= 0))
            throw new ArgumentException(); // an element with the same key already exists
        else
            InsertInternal(key, item);
    }

    public bool TryAdd(int key, TValue item)
    {
        if (FindHashIndex(key) < 0)
        {
            InsertInternal(key, item);
            return true;
        }
        return false;
    }

    public bool Remove(int key)
    {
        int index = FindHashIndex(key);
        if (index >= 0)
            return RemoveByIndex(index);
        return false;
    }

    public bool TryGetValue(int key, out TValue value)
    {
        int index = FindHashIndex(key);
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
        int index = FindHashIndex(key);
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

    void InitialAllocate(int capacity)
    {
        ResizeBuffersInternal(capacity);
        element_count = 0;
        resize_threshold = capacity - capacity / 3;
    }

    void ResizeBuffersInternal(int new_capacity)
    {
        // check capacity is a non-negative value, and either zero or a power of 2
        Assert.IsTrue((new_capacity >= 0) && ((new_capacity & (new_capacity - 1)) == 0));

        if (m_values == null)
        {
            m_values = new TValue[new_capacity];
            m_hashes = new int[new_capacity];
        }
        else
        {
            int old_capacity = m_values.Length;
            if (new_capacity > old_capacity)
            {
                Array.Resize(ref m_values, new_capacity);
                Array.Resize(ref m_hashes, new_capacity);
            }
        }
    }

    // returns the FIRST index that matches hash, duplicate hashes will follow immediately after (with wrap around)
    protected int FindHashIndex(int hash)
    {
        Assert.IsTrue(hash != EMPTY_HASH);

        int bucket_index = -1;
	    if (element_count > 0)
	    {
            int capacity = m_values.Length;
            int bucket_mask = capacity - 1;     // assumes values.Count is a power of 2
            bucket_index = hash & bucket_mask;
            int dist = 0;
            while (true)
            {
                int bucket_hash = m_hashes[bucket_index];
                if (bucket_hash == hash)
                {
                    // found the hash!  yay!
                    break;
                }
                else if (bucket_hash == EMPTY_HASH)
                {
                    // found empty slot => key is not in the table
                    return -1;
                }
                else
                {
                    int ideal_bucket_index = bucket_hash & bucket_mask;
                    int dist_to_ideal_bucket = (bucket_index - ideal_bucket_index + capacity) & bucket_mask;
                    if (dist > dist_to_ideal_bucket)
                    {
                        // found slot with probe distance less than our current distance => key is not in the table
                        return -1;
                    }
                }

                // otherwise -- advance linearly and check again
                bucket_index = (bucket_index + 1) & bucket_mask;
                dist++;
            }
		}
        return bucket_index;
    }

    protected void Grow()
    {
        int old_capacity = m_values.Length;

        int old_bucket_mask = 0;
        if (old_capacity > 0)
        {
            // this protects us from calling get_bucket_mask() with a capacity of zero
            old_bucket_mask = old_capacity - 1;
        }

        int new_capacity = Math.Max(old_capacity * 2, 4);
        int new_bucket_mask = new_capacity - 1;

        ResizeBuffersInternal(new_capacity);

        resize_threshold = new_capacity - new_capacity / 3;

        // move elements to their new positions
        if (element_count > 0)
        {
            int cur_index = 0;
            int cur_hash = m_hashes[cur_index];

            // skip all the DIB > 0 elements at the start (they are part of a run starting at the end of the old table, so will be processed last)
            while (cur_hash != EMPTY_HASH)
            {
                int ideal_bucket_index = cur_hash & old_bucket_mask;
                int distance_to_ideal_bucket = (cur_index - ideal_bucket_index + old_capacity) & old_bucket_mask;

                if (distance_to_ideal_bucket <= 0)
                    break;

                cur_index++;
                cur_hash = m_hashes[cur_index];
            }

            // advance the end index to make sure we will wrap around and catch the skipped guys at the end
            int end_index = old_capacity + cur_index;

            int run_start_index = cur_index;
            int run_offset_low = 0;
            int run_offset_high = 0;

            for (; cur_index < end_index; cur_index++)
            {
                int read_index = cur_index & old_bucket_mask;   // read index wraps at old_capacity, cur_index does not
                cur_hash = m_hashes[read_index];

                if (cur_hash != EMPTY_HASH)                     // skip empty elements, don't need to be moved ;)
                {
                    // non-empty element, calculate it's ideal positions
                    int new_ideal_bucket_index = cur_hash & new_bucket_mask;
                    int old_ideal_bucket_index = cur_hash & old_bucket_mask;

                    // check the old DIB
                    int old_dib = (read_index - old_ideal_bucket_index + old_capacity) & old_bucket_mask;
                    if (old_dib == 0)
                    {
                        // this is the start of a new run
                        run_start_index = read_index;
                        run_offset_low = 0;
                        run_offset_high = 0;
                    }

                    // calculate the run_offset of the new position for this element
                    int run_offset = (new_ideal_bucket_index - run_start_index + new_capacity) & new_bucket_mask;

                    int write_index;
                    if (run_offset < old_capacity)
                    {
                        // write to low run
                        run_offset_low = (run_offset_low < run_offset) ? run_offset : run_offset_low;
                        write_index = (run_start_index + run_offset_low) & new_bucket_mask;
                        run_offset_low++;
                    }
                    else
                    {
                        // write to high run
                        run_offset_high = (run_offset_high < run_offset) ? run_offset : run_offset_high;
                        write_index = (run_start_index + run_offset_high) & new_bucket_mask;
                        run_offset_high++;
                    }

                    // don't bother copying if we are writing to the read index
                    if (write_index != read_index)
                    {
                        // double check write_index is empty
                        Assert.IsTrue(m_hashes[write_index] == EMPTY_HASH);

                        m_values[write_index] = m_values[read_index];
                        m_hashes[write_index] = cur_hash;

                        // also clear out the read position
                        m_values[read_index] = default;
                        m_hashes[read_index] = EMPTY_HASH;
                    }
                }
            }
        }
    }

    int InsertInternal(int hash, TValue data)
    {
        Assert.IsTrue(hash != EMPTY_HASH);

        int result_index = -1;

        int new_element_count = element_count + 1;
        if (new_element_count > resize_threshold)
            Grow();

        element_count = new_element_count;

        int capacity = m_values.Length;
        int bucket_mask = capacity - 1;
        int bucket_index = hash & bucket_mask;
        int dist = 0;

        while (true)
        {
            int bucket_hash = m_hashes[bucket_index];
            if (bucket_hash == EMPTY_HASH)
            {
                // found empty position -- place the new entry here
                m_values[bucket_index] = data;
                m_hashes[bucket_index] = hash;

                if (result_index == -1)
                    result_index = bucket_index;
                break;
            }
            else
            {
                int ideal_bucket_index = bucket_hash & bucket_mask;
                int distance_to_ideal_bucket = (bucket_index - ideal_bucket_index + capacity) & bucket_mask;

                if (distance_to_ideal_bucket < dist)
                {
                    if (result_index == -1)
                    {
                        result_index = bucket_index;
                    }

                    // swap everything
                    TValue temp_data = m_values[bucket_index];
                    m_values[bucket_index] = data;
                    m_hashes[bucket_index] = hash;

                    // pick up the old value that used to be here, and continue, looking for a place to put it
                    hash = bucket_hash;
                    data = temp_data;

                    dist = distance_to_ideal_bucket;
                }

                bucket_index = (bucket_index + 1) & bucket_mask;
                dist++;
            }
        }

        return result_index;
    }

    // returns true if the item was removed
    bool RemoveByIndex(int bucket_index)
    {
        bool removed = false;
        Assert.IsTrue((bucket_index >= 0) && (element_count > 0));

        int hash = m_hashes[bucket_index];
        if (hash != EMPTY_HASH)
        {
            int capacity = m_values.Length;
            int bucket_mask = capacity - 1;
            int cur_index = bucket_index;
            int next_index = (cur_index + 1) & bucket_mask;
            int next_hash = m_hashes[next_index];

            // traverse the nodes in front of us, and move them backwards by one step as long as their DIB is improved by doing so
            if (next_hash != EMPTY_HASH)
            {
                int ideal_bucket_index = next_hash & bucket_mask;
                int distance_to_ideal_bucket = (next_index - ideal_bucket_index + capacity) & bucket_mask;

                if (distance_to_ideal_bucket > 0)
                {
                    do
                    {
                        // distance is at least one, so copy backwards (reducing distance by one)
                        m_values[cur_index] = m_values[next_index];
                        m_hashes[cur_index] = next_hash;

                        cur_index = next_index;
                        next_index = (cur_index + 1) & bucket_mask;
                        next_hash = m_hashes[next_index];

                        ideal_bucket_index = next_hash & bucket_mask;
                        distance_to_ideal_bucket = (next_index - ideal_bucket_index + capacity) & bucket_mask;
                    } while ((cur_index != bucket_index) &&
                             (next_hash != EMPTY_HASH) &&
                             (distance_to_ideal_bucket > 0));
                }
            }

            // make sure to clear the last empty spot (the target spot if there was no back-copying)
            m_hashes[cur_index] = EMPTY_HASH;
            m_values[cur_index] = default;
            element_count--;

            removed = true;
        }

        return removed;
    }

    // validate that the hash table is correctly set up
    public bool Validate()
    {
        bool result = true;

        int capacity = m_values.Length;
        if ((element_count < 0) || (element_count > capacity))
        {
            // ERROR: element count is outside the valid range!
            result = false;
        }

        if ((resize_threshold < 1) || (resize_threshold >= capacity))
        {
            // ERROR: resize threshold is outside valid range!
            result = false;
        }

        int empty_count = 0;
        int valid_count = 0;
        int dib_count = 0;
        int max_dib = -1;
        int dib0_count = 0;

        int prev_dist = 0;

        if (capacity > 0)
        {
            int bucket_mask = capacity - 1;

            int last_hash = m_hashes[capacity - 1];
            if (last_hash != EMPTY_HASH)
            {
                int ideal_bucket_index = last_hash & bucket_mask;
                int distance_to_ideal_bucket = ((capacity - 1) - ideal_bucket_index + capacity) & bucket_mask;
                prev_dist = distance_to_ideal_bucket;
            }
            for (int bucket_index = 0; bucket_index < capacity; bucket_index++)
            {
                int hash = m_hashes[bucket_index];
                if (hash == EMPTY_HASH)
                {
                    empty_count++;
                    prev_dist = -1;
                }
                else
                {
                    valid_count++;

                    int ideal_bucket_index = hash & bucket_mask;
                    int distance_to_ideal_bucket = (bucket_index - ideal_bucket_index + capacity) & bucket_mask;
                    int dist = distance_to_ideal_bucket;

                    max_dib = (max_dib > dist ? max_dib : dist);

                    dib_count += dist;

                    if (dist == 0)
                    {
                        dib0_count++;
                    }

                    if (dist > prev_dist + 1)
                    {
                        // ERROR! overall distance would be improved by swapping this and the previous element
                        // ERROR!  dib distance is out of order!  elements not a valid rh table
                        result = false;
                    }

                    prev_dist = dist;
                }
            }
        }

        if (element_count != valid_count)
        {
            // ERROR! element_count is incorrect!
            result = false;
        }

        if (empty_count + element_count != capacity)
        {
            // ERROR! totals don't add up
            result = false;
        }

        if ((element_count > 0) && (dib0_count <= 0))
        {
            // ERROR!  must be at least one element at dib 0
            result = false;
        }

        if (dib0_count > element_count)
        {
            // ERROR!  too many dib 0 elements
            result = false;
        }

        if (max_dib >= element_count)
        {
            // ERROR!   max dib is too large!
        }

        return result;
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
        Assert.IsTrue(Validate());

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