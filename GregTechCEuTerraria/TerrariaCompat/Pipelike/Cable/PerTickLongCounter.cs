#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Cable;

// LOCKED - verbatim port of PerTickLongCounter. Single-tick lookback;
// auto-rolls on tick advance. Caller-supplied tick (we take it directly
// instead of upstream's Level.getGameTime to stay unit-testable).
public class PerTickLongCounter
{
	private readonly long _defaultValue;
	private long _lastUpdatedWorldTime;
	private long _lastValue;
	private long _currentValue;

	public PerTickLongCounter() : this(0) { }

	public PerTickLongCounter(long defaultValue)
	{
		_defaultValue = defaultValue;
		_currentValue = defaultValue;
		_lastValue = defaultValue;
	}

	private void CheckValueState(long currentWorldTime)
	{
		if (currentWorldTime != _lastUpdatedWorldTime)
		{
			_lastValue = currentWorldTime == _lastUpdatedWorldTime + 1
				? _currentValue
				: _defaultValue;
			_lastUpdatedWorldTime = currentWorldTime;
			_currentValue = _defaultValue;
		}
	}

	public long Get(long currentWorldTime)
	{
		CheckValueState(currentWorldTime);
		return _currentValue;
	}

	public long GetLast(long currentWorldTime)
	{
		CheckValueState(currentWorldTime);
		return _lastValue;
	}

	public void Increment(long currentWorldTime, long value)
	{
		CheckValueState(currentWorldTime);
		_currentValue += value;
	}

	public void Set(long currentWorldTime, long value)
	{
		CheckValueState(currentWorldTime);
		_currentValue = value;
	}
}
