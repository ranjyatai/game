using System;
using System.Collections.Generic;

/// <summary>
/// 可被 JsonUtility 序列化的字典，以 key/value pair 列表形式存储。
/// </summary>
[Serializable]
public class SerializableDictionary<TKey, TValue>
{
    [Serializable]
    private struct Pair
    {
        public TKey   key;
        public TValue value;
    }

    private List<Pair> _pairs = new List<Pair>();

    public TValue this[TKey key]
    {
        get
        {
            for (int i = 0; i < _pairs.Count; i++)
                if (EqualityComparer<TKey>.Default.Equals(_pairs[i].key, key))
                    return _pairs[i].value;
            throw new KeyNotFoundException($"Key not found: {key}");
        }
        set
        {
            for (int i = 0; i < _pairs.Count; i++)
            {
                if (EqualityComparer<TKey>.Default.Equals(_pairs[i].key, key))
                {
                    _pairs[i] = new Pair { key = key, value = value };
                    return;
                }
            }
            _pairs.Add(new Pair { key = key, value = value });
        }
    }

    public bool ContainsKey(TKey key)
    {
        for (int i = 0; i < _pairs.Count; i++)
            if (EqualityComparer<TKey>.Default.Equals(_pairs[i].key, key))
                return true;
        return false;
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        for (int i = 0; i < _pairs.Count; i++)
        {
            if (EqualityComparer<TKey>.Default.Equals(_pairs[i].key, key))
            {
                value = _pairs[i].value;
                return true;
            }
        }
        value = default;
        return false;
    }

    public void Remove(TKey key)
    {
        for (int i = _pairs.Count - 1; i >= 0; i--)
            if (EqualityComparer<TKey>.Default.Equals(_pairs[i].key, key))
                _pairs.RemoveAt(i);
    }

    public IEnumerable<TKey> Keys
    {
        get { foreach (var p in _pairs) yield return p.key; }
    }
}
