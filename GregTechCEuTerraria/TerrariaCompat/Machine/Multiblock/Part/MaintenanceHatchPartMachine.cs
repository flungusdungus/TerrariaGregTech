#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Machine;
using GregTechCEuTerraria.Api.Machine.Feature.Multiblock;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Tool;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Items.Registry;
using GregTechCEuTerraria.TerrariaCompat.Items.Tools;
using GregTechCEuTerraria.TerrariaCompat.Net;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

// Port of MaintenanceHatchPartMachine. LV/HV maintenance hatch + duct-tape
// auto-apply tick + player hand-fix.
//
// Adaptations: Configure(IsConfigurable) replaces BlockEntityCreationInfo;
// state rides SaveData + viewer-sync; durability not modelled (Terraria
// tools don't wear). Verbatim: ALL_PROBLEMS start, 20-tick auto-tape,
// fix walk order (creative -> tape -> tools), piecewise time multiplier.
public class MaintenanceHatchPartMachine : TieredPartMachine, IMaintenanceMachine
{
	public const float MAX_DURATION_MULTIPLIER = 1.1f;
	public const float MIN_DURATION_MULTIPLIER = 0.9f;
	public const float DURATION_ACTION_AMOUNT  = 0.01f;

	protected override string Label => "Maintenance Hatch";

	public bool IsConfigurable { get; private set; }
	protected NotifiableItemStackHandler? ItemStackHandler;

	public override Api.Capability.IItemHandler? ExposedItemHandler => ItemStackHandler;

	private bool  _isTaped;
	private int   _timeActive;
	private byte  _maintenanceProblems;
	private float _durationMultiplier = 1f;

	private TickableSubscription? _maintenanceSubs;

	public MaintenanceHatchPartMachine() : base() { }

	public void Configure(bool isConfigurable)
	{
		IsConfigurable = isConfigurable;
		Tier = isConfigurable
			? (int)VoltageTier.HV
			: (int)VoltageTier.LV;
		EnsureTraits();
		// Verbatim onLoad arm (dormant today - MaintenanceConfig.Enabled = false).
		UpdateMaintenanceSubscription();
	}

	protected override void OnDefinitionBound()
	{
		base.OnDefinitionBound();
		if (Definition == null) return;
		Configure(Definition.PartConfigurable);
	}

	public override Item[]? GetSlotGroup(TerrariaCompat.Machine.SlotGroup group) =>
		group == TerrariaCompat.Machine.SlotGroup.Inventory && ItemStackHandler != null
			? ItemStackHandler.Storage.Stacks
			: base.GetSlotGroup(group);

	public override void AppendTooltip(List<string> lines)
	{
		base.AppendTooltip(lines);
		var imm = (IMaintenanceMachine)this;
		// MUST route through accessor - gates on MaintenanceConfig.Enabled.
		int problems = imm.GetNumMaintenanceProblems();
		lines.Add(problems == 0 ? "[c/55FF55:No problems]" : $"[c/FF5555:Problems: {problems}/6]");
		if (_isTaped) lines.Add("Taped");
		if (IsConfigurable)
			lines.Add($"Duration Multiplier: {_durationMultiplier:F2}");
	}

	protected virtual NotifiableItemStackHandler CreateInventory() =>
		new(1, IO.BOTH, IO.BOTH);

	protected virtual void EnsureTraits()
	{
		if (ItemStackHandler != null) return;
		ItemStackHandler = CreateInventory();
		Traits.Attach(ItemStackHandler);
		Traits.RegisterPersistent("ItemStackHandler", ItemStackHandler);
		if (RegistryItemLoader.TryGet("gtceu:duct_tape", out int ductTapeType))
			ItemStackHandler.SetFilter(item => item.type == ductTapeType);
		_maintenanceProblems = ((IMaintenanceMachine)this).StartProblems();
	}

	// === IMaintenanceMachine state ===========================================

	public bool IsFullAuto() => false;
	public bool IsTaped()    => _isTaped;

	public byte StartProblems() => IMaintenanceMachine.ALL_PROBLEMS;

	public byte GetMaintenanceProblems() => _maintenanceProblems;

	public void SetMaintenanceProblems(byte problems)
	{
		_maintenanceProblems = problems;
		UpdateMaintenanceSubscription();
		if (IsServer) MachineStateSyncPacket.Broadcast(this);
	}

	public int GetTimeActive() => _timeActive;
	public void SetTimeActive(int time) => _timeActive = time;

	public float GetDurationMultiplier() => _durationMultiplier;

	public void SetDurationMultiplier(float value)
	{
		if (value < MIN_DURATION_MULTIPLIER) value = MIN_DURATION_MULTIPLIER;
		if (value > MAX_DURATION_MULTIPLIER) value = MAX_DURATION_MULTIPLIER;
		_durationMultiplier = value;
		if (IsServer) MachineStateSyncPacket.Broadcast(this);
	}

	public void SetTaped(bool isTaped)
	{
		if (_isTaped == isTaped) return;
		_isTaped = isTaped;
		if (IsServer) MachineStateSyncPacket.Broadcast(this);
	}

	public float GetTimeMultiplier()
	{
		float result = 1f;
		if (_durationMultiplier < 1.0f)
			result = -20f * _durationMultiplier + 21f;
		else
			result =  -8f * _durationMultiplier +  9f;
		// HALF_UP rounding (upstream BigDecimal).
		return (float)System.Math.Round(result, 2, System.MidpointRounding.AwayFromZero);
	}

	private void UpdateMaintenanceSubscription()
	{
		if (((IMaintenanceMachine)this).HasMaintenanceProblems())
		{
			_maintenanceSubs ??= SubscribeServerTick(MaintenanceTick);
		}
		else if (_maintenanceSubs != null)
		{
			_maintenanceSubs.Unsubscribe();
			_maintenanceSubs = null;
		}
	}

	// Not "Update" because that would shadow TileEntity.Update.
	public void MaintenanceTick()
	{
		if (GetOffsetTimer() % global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(20) != 0) return;
		var imm = (IMaintenanceMachine)this;
		if (imm.HasMaintenanceProblems())
		{
			if (ConsumeDuctTape(ItemStackHandler!, 0))
			{
				FixAllMaintenanceProblems();
				SetTaped(true);
			}
		}
		else
		{
			UpdateMaintenanceSubscription();
		}
	}

	private static bool ConsumeDuctTape(NotifiableItemStackHandler handler, int slot)
	{
		if (!RegistryItemLoader.TryGet("gtceu:duct_tape", out int ductTapeType)) return false;
		var stored = handler.GetSlot(slot);
		if (stored.IsAir || stored.type != ductTapeType) return false;
		var extracted = handler.Extract(slot, 1, false);
		return !extracted.IsAir && extracted.type == ductTapeType;
	}

	private static bool ConsumeHeldDuctTape(Player player)
	{
		if (!RegistryItemLoader.TryGet("gtceu:duct_tape", out int ductTapeType)) return false;
		var held = player.HeldItem;
		if (held == null || held.IsAir || held.type != ductTapeType) return false;
		if (!player.creativeGodMode) held.stack--;
		if (held.stack <= 0) held.TurnToAir();
		return true;
	}

	public void FixAllMaintenanceProblems()
	{
		var imm = (IMaintenanceMachine)this;
		for (int i = 0; i < 6; i++) imm.SetMaintenanceFixed(i);
	}

	public bool TryFixWithHandHeldTape(Player player)
	{
		var imm = (IMaintenanceMachine)this;
		if (!imm.HasMaintenanceProblems()) return false;
		if (!ConsumeHeldDuctTape(player)) return false;
		FixAllMaintenanceProblems();
		SetTaped(true);
		return true;
	}

	// Verbatim fixMaintenanceProblems(player).
	public bool TryFixFromPlayerInventory(Player player)
	{
		var imm = (IMaintenanceMachine)this;
		if (!imm.HasMaintenanceProblems()) return false;

		if (player.creativeGodMode) { FixAllMaintenanceProblems(); return true; }

		if (RegistryItemLoader.TryGet("gtceu:duct_tape", out int ductTapeType))
		{
			for (int i = 0; i < player.inventory.Length; i++)
			{
				var stack = player.inventory[i];
				if (stack != null && !stack.IsAir && stack.type == ductTapeType)
				{
					stack.stack--;
					if (stack.stack <= 0) stack.TurnToAir();
					FixAllMaintenanceProblems();
					SetTaped(true);
					return true;
				}
			}
		}

		FixProblemsWithTools(_maintenanceProblems, player);
		return true;
	}

	private void FixProblemsWithTools(byte problems, Player player)
	{
		var needed = new GTToolType?[6];
		bool anyMissing = false;
		for (int i = 0; i < 6; i++)
		{
			if (((problems >> i) & 1) == 0)
			{
				anyMissing = true;
				needed[i] = i switch
				{
					0 => GTToolType.WRENCH,
					1 => GTToolType.SCREWDRIVER,
					2 => GTToolType.SOFT_MALLET,
					3 => GTToolType.HARD_HAMMER,
					4 => GTToolType.WIRE_CUTTER,
					5 => GTToolType.CROWBAR,
					_ => null,
				};
			}
		}
		if (!anyMissing) return;

		var imm = (IMaintenanceMachine)this;
		for (int idx = 0; idx < 6; idx++)
		{
			var toolType = needed[idx];
			if (toolType == null) continue;
			for (int s = 0; s < player.inventory.Length; s++)
			{
				var stack = player.inventory[s];
				if (stack == null || stack.IsAir) continue;
				if (stack.ModItem is ToolItem t && ReferenceEquals(t.ToolType, toolType))
				{
					imm.SetMaintenanceFixed(idx);
					SetTaped(false);
					// Upstream damages tool by 1; no durability in this port.
					break;
				}
			}
		}
	}

	public override void SaveData(TagCompound tag)
	{
		base.SaveData(tag);
		tag["isConfigurable"]      = IsConfigurable;
		tag["isTaped"]             = _isTaped;
		tag["timeActive"]          = _timeActive;
		tag["maintenanceProblems"] = (byte)_maintenanceProblems;
		tag["durationMultiplier"]  = _durationMultiplier;
	}

	public override void LoadData(TagCompound tag)
	{
		base.LoadData(tag);
		IsConfigurable       = tag.GetBool("isConfigurable");
		_isTaped             = tag.GetBool("isTaped");
		_timeActive          = tag.GetInt("timeActive");
		_maintenanceProblems = tag.GetByte("maintenanceProblems");
		_durationMultiplier  = tag.ContainsKey("durationMultiplier") ? tag.GetFloat("durationMultiplier") : 1f;
		Tier = IsConfigurable ? (int)VoltageTier.HV : (int)VoltageTier.LV;
		EnsureTraits();
		Traits.Load(tag);   // late-registration re-load; ItemBus pattern.
		UpdateMaintenanceSubscription();
	}
}
