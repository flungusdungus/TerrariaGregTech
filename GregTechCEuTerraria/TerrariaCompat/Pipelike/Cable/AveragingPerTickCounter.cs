#nullable enable
using System;
using System.Linq;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Cable;

// LOCKED - verbatim port of AveragingPerTickCounter.
// Ring-buffer averaging over `length` ticks (upstream default 20 = 1s @ 20 TPS;
// pass length=60 for 1s @ Terraria's 60 TPS). Caller-supplied tick (we take
// it directly instead of upstream's Level.getGameTime to stay unit-testable).
public class AveragingPerTickCounter
{
	private readonly long _defaultValue;
	private readonly long[] _values;
	private long _lastUpdatedWorldTime = 0;
	private int _currentIndex = 0;
	private bool _dirty = true;
	private double _lastAverage = 0;

	public AveragingPerTickCounter() : this(0, 20) { }

	public AveragingPerTickCounter(long defaultValue, int length)
	{
		_defaultValue = defaultValue;
		_values = new long[length];
		Array.Fill(_values, defaultValue);
	}

	private void CheckValueState(long currentWorldTime)
	{
		if (currentWorldTime != _lastUpdatedWorldTime)
		{
			long dif = currentWorldTime - _lastUpdatedWorldTime;
			if (dif >= _values.Length || dif < 0)
			{
				Array.Fill(_values, _defaultValue);
				_currentIndex = 0;
			}
			else
			{
				for (int i = _currentIndex + 1; i <= _currentIndex + dif; i++)
				{
					_values[i % _values.Length] = _defaultValue;
				}
				_currentIndex += (int)dif;
				if (_currentIndex >= _values.Length)
					_currentIndex = _currentIndex % _values.Length;
			}
			_lastUpdatedWorldTime = currentWorldTime;
			_dirty = true;
		}
	}

	public long GetLast(long currentWorldTime)
	{
		CheckValueState(currentWorldTime);
		return _values[_currentIndex];
	}

	public double GetAverage(long currentWorldTime)
	{
		CheckValueState(currentWorldTime);
		if (!_dirty) return _lastAverage;
		_dirty = false;
		return _lastAverage = _values.Sum() / (double)_values.Length;
	}

	public void Increment(long currentWorldTime, long value)
	{
		CheckValueState(currentWorldTime);
		_values[_currentIndex] += value;
	}

	public void Set(long currentWorldTime, long value)
	{
		CheckValueState(currentWorldTime);
		_values[_currentIndex] = value;
	}
}
