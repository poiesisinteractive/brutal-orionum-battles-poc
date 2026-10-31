namespace OrionumFleet.Sim;

/// <summary>
/// Table contiguë d'entités (structs), dans l'ordre de création. On y accède par <c>ref</c> ;
/// copier le monde revient à copier ces tableaux. Remplace les tableaux d'objets du TS.
/// </summary>
public sealed class EntityTable<T>
    where T : struct
{
    T[] _items;

    public EntityTable(int capacity = 16) => _items = new T[Math.Max(capacity, 1)];

    public int Count { get; private set; }

    public ref T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
                ThrowIndex(index);
            return ref _items[index];
        }
    }

    public Span<T> AsSpan() => _items.AsSpan(0, Count);

    /// <summary>Ajoute une entité et rend une référence vers son slot.</summary>
    public ref T Add(scoped in T item)
    {
        if (Count == _items.Length)
            Array.Resize(ref _items, _items.Length * 2);
        _items[Count] = item;
        return ref _items[Count++];
    }

    public void Clear() => Count = 0;

    /// <summary>Raccourcit la table après un compactage fait par l'appelant.</summary>
    public void Truncate(int count) => Count = count;

    /// <summary>Copie profonde (les structs sont blittables : une copie de tableau suffit).</summary>
    public EntityTable<T> Clone()
    {
        var copy = new EntityTable<T>(_items.Length);
        Array.Copy(_items, copy._items, Count);
        copy.Count = Count;
        return copy;
    }

    static void ThrowIndex(int index) => throw new IndexOutOfRangeException($"Index {index} hors de la table");
}
