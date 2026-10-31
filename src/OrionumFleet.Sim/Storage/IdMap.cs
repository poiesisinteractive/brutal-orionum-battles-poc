namespace OrionumFleet.Sim;

/// <summary>
/// Table de hachage id → (table, index), à adressage ouvert et sondage linéaire. Remplace le
/// <c>byId</c> (Map JS) du TS. Elle n'est faite que de tableaux d'entiers : copier le monde la copie
/// d'un bloc. Elle est reconstruite à chaque élagage, puisque les index changent.
/// </summary>
public sealed class IdMap
{
    const int Empty = 0;

    int[] _keys;
    int[] _values;
    int _mask;

    public IdMap(int capacity = 256)
    {
        var size = 16;
        while (size < capacity * 2)
            size <<= 1;
        _keys = new int[size];
        _values = new int[size];
        _mask = size - 1;
    }

    public int Count { get; private set; }

    public void Set(int id, EntityRef entity)
    {
        if ((Count + 1) * 2 > _keys.Length)
            Grow();

        var slot = Hash(id) & _mask;
        while (_keys[slot] != Empty && _keys[slot] != id)
            slot = (slot + 1) & _mask;

        if (_keys[slot] == Empty)
            Count++;
        _keys[slot] = id;
        _values[slot] = Pack(entity);
    }

    public EntityRef Get(int id)
    {
        if (id <= 0)
            return EntityRef.None;

        var slot = Hash(id) & _mask;
        while (true)
        {
            var key = _keys[slot];
            if (key == id)
                return Unpack(_values[slot]);
            if (key == Empty)
                return EntityRef.None;
            slot = (slot + 1) & _mask;
        }
    }

    public void Clear()
    {
        Array.Clear(_keys);
        Count = 0;
    }

    public IdMap Clone()
    {
        var copy = new IdMap(1);
        copy._keys = (int[])_keys.Clone();
        copy._values = (int[])_values.Clone();
        copy._mask = _mask;
        copy.Count = Count;
        return copy;
    }

    void Grow()
    {
        var keys = _keys;
        var values = _values;
        _keys = new int[keys.Length * 2];
        _values = new int[keys.Length * 2];
        _mask = _keys.Length - 1;
        Count = 0;
        for (var i = 0; i < keys.Length; i++)
            if (keys[i] != Empty)
                Set(keys[i], Unpack(values[i]));
    }

    // Mélange de Knuth : les ids sont consécutifs, il faut les disperser dans la table.
    static int Hash(int id) => (int)((uint)id * 2654435761u >> 1);

    static int Pack(EntityRef e) => ((int)e.Kind << 28) | e.Index;

    static EntityRef Unpack(int value) => new((EntityKind)(value >> 28), value & 0x0FFF_FFFF);
}
