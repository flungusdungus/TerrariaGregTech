#nullable enable
using System.Numerics;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Util;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Cover.Detector;

// Port of common.cover.detector.AdvancedEnergyDetectorCover. Configurable
// min/max thresholds (raw EU or percent), always-latched output. createUIWidget
// + initializeMinMaxInputs dropped; fields default to 33% / 66% / percent.
public class AdvancedEnergyDetectorCover : EnergyDetectorCover, IUICover, IAdvancedDetectorCover
{
	private const int DefaultMinPercent = 33;
	private const int DefaultMaxPercent = 66;

	private long _minValue = DefaultMinPercent;
	private long _maxValue = DefaultMaxPercent;
	private bool _usePercent = true;

	public AdvancedEnergyDetectorCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide)
		: base(definition, coverHolder, attachedSide) { }

	public long MinValue => _minValue;
	public long MaxValue => _maxValue;
	public bool UsePercent => _usePercent;

	public void SetMinValue(long minValue) => _minValue = minValue;
	public void SetMaxValue(long maxValue) => _maxValue = maxValue;
	public void SetUsePercent(bool usePercent) => _usePercent = usePercent;

	// field 2=min, 3=max, 5=EU/percent. 1 (invert) falls through.
	public override void ApplySetting(int field, long value)
	{
		switch (field)
		{
			case 2: SetMinValue(System.Math.Max(0, value)); break;
			case 3: SetMaxValue(System.Math.Max(0, value)); break;
			case 5: SetUsePercent(value != 0); break;
			default: base.ApplySetting(field, value); break;
		}
	}

	protected override void Update()
	{
		if (CoverHolder.GetOffsetTimer() % global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(20) != 0) return;

		var provider = GetEnergyInfoProvider();
		if (provider == null) return;

		var energyInfo = provider.GetEnergyInfo();

		if (provider.SupportsBigIntEnergyValues())
		{
			if (_usePercent)
			{
				if (energyInfo.Capacity > BigInteger.Zero)
				{
					float ratio = RedstoneUtil.Ratio(energyInfo.Stored, energyInfo.Capacity);
					SetRedstoneSignalOutput(RedstoneUtil.ComputeLatchedRedstoneBetweenValues(
						ratio * 100, _maxValue, _minValue, IsInverted, RedstoneSignalOutput));
				}
				else
				{
					SetRedstoneSignalOutput(IsInverted ? 15 : 0);
				}
			}
			else
			{
				SetRedstoneSignalOutput(RedstoneUtil.ComputeLatchedRedstoneBetweenValues(
					energyInfo.Stored, new BigInteger(_maxValue), new BigInteger(_minValue),
					IsInverted, RedstoneSignalOutput));
			}
		}
		else
		{
			if (_usePercent)
			{
				if ((long)energyInfo.Capacity > 0)
				{
					float ratio = (float)energyInfo.Stored / (float)energyInfo.Capacity;
					SetRedstoneSignalOutput(RedstoneUtil.ComputeLatchedRedstoneBetweenValues(
						ratio * 100, _maxValue, _minValue, IsInverted, RedstoneSignalOutput));
				}
				else
				{
					SetRedstoneSignalOutput(IsInverted ? 15 : 0);
				}
			}
			else
			{
				SetRedstoneSignalOutput(RedstoneUtil.ComputeLatchedRedstoneBetweenValues(
					(float)(long)energyInfo.Stored, _maxValue, _minValue, IsInverted, RedstoneSignalOutput));
			}
		}
	}

	public override void Save(TagCompound tag)
	{
		base.Save(tag);
		tag["min"] = _minValue;
		tag["max"] = _maxValue;
		tag["percent"] = _usePercent;
	}

	public override void Load(TagCompound tag)
	{
		base.Load(tag);
		if (tag.ContainsKey("min")) _minValue = tag.GetLong("min");
		if (tag.ContainsKey("max")) _maxValue = tag.GetLong("max");
		_usePercent = tag.GetBool("percent");
	}
}
