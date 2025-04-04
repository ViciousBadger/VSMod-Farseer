using System.Collections;
using System.Collections.Generic;

namespace Farseer;

public readonly record struct Coord2D(int X, int Z);

public sealed class SpiralWalker : IEnumerable<Coord2D>, IEnumerator<Coord2D>
{
    private readonly int _radius;
    private readonly Coord2D _center;

    private int _x;
    private int _z;

    private int _dx = 0;
    private int _dz = -1;

    private Coord2D _currentCoordinate;

    public Coord2D Current => _currentCoordinate;

    object IEnumerator.Current => _currentCoordinate;

    public SpiralWalker(Coord2D center, int radius)
    {
        this._center = center;
        this._radius = radius;
        this._x = center.X;
        this._z = center.Z;
    }

    public void Dispose() { }

    public IEnumerator<Coord2D> GetEnumerator()
    {
        return this;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return this;
    }

    public bool MoveNext()
    {
        var done = _x < -_radius || _x > _radius || _z < -_radius || _x > _radius;

        _currentCoordinate = new Coord2D(_x, _z);

        if (_x == _z || (_x < 0 && _x == -_z) || (_x > 0 && _x == 1 - _z))
        {
            var t = _dx;
            _dx = -_dz;
            _dz = t;
        }

        _x += _dx;
        _z += _dz;

        return !done;
    }

    public void Reset()
    {
        _x = _center.X;
        _z = _center.Z;
        _dx = 0;
        _dz = -1;
    }
}
