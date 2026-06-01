#nullable enable
using System;
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Steam;
using GregTechCEuTerraria.TerrariaCompat.Net.Actions;
using GregTechCEuTerraria.TerrariaCompat.UI.Widgets;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI;

// Sum-type spec for one widget on a layout - knows its position/sizing and
// how to materialise itself against an entity (capability-checked). Stateful
// widgets carry getter/setter delegates closed over the entity reference.
public abstract record WidgetSpec(int X, int Y)
{
	public abstract UIElement Create(MetaMachine entity);
}

public sealed record EnergyBarWidgetSpec(int X, int Y, int Width = 18, int Height = 60)
	: WidgetSpec(X, Y)
{
	public override UIElement Create(MetaMachine entity)
	{
		if (entity is not TieredEnergyMachine container)
			throw new InvalidOperationException($"{entity.GetType().Name} is not an TieredEnergyMachine - EnergyBarWidget requires one");
		return new UIEnergyBar(container, Width, Height);
	}
}

// Mirrors upstream ProgressWidget(getTemperaturePercent).
public sealed record TemperatureBarWidgetSpec(int X, int Y, int Width = 10, int Height = 54)
	: WidgetSpec(X, Y)
{
	public override UIElement Create(MetaMachine entity)
	{
		if (entity is not SteamBoilerMachine boiler)
			throw new InvalidOperationException($"{entity.GetType().Name} is not a SteamBoilerMachine - TemperatureBarWidget requires one");
		return new UITemperatureBar(boiler, Width, Height);
	}
}

public sealed record LabelWidgetSpec(int X, int Y, string Text, float Scale = 0.85f)
	: WidgetSpec(X, Y)
{
	public override UIElement Create(MetaMachine entity) => new UILabel(Text, Scale);
}

public sealed record DynamicLabelWidgetSpec(int X, int Y, Func<string> Getter, float Scale = 0.85f)
	: WidgetSpec(X, Y)
{
	public override UIElement Create(MetaMachine entity) => new UIDynamicLabel(Getter, Scale);
}

// Re-evaluates `Func<List<string>>` per frame - analogue of upstream
// ComponentPanelWidget. Used by every multi layout via MultiblockDisplayText.
public sealed record MultiLineDynamicLabelWidgetSpec(int X, int Y,
	Func<System.Collections.Generic.IReadOnlyList<string>> Getter,
	float Scale = 0.85f, float LineHeight = 16f)
	: WidgetSpec(X, Y)
{
	public override UIElement Create(MetaMachine entity) =>
		new UIMultiLineDynamicLabel(Getter, Scale, LineHeight);
}

// Default size is the small +/- stepper footprint.
public sealed record TextButtonWidgetSpec(int X, int Y, Func<string> Label,
		Action? OnLeft = null, Action? OnRight = null, string? Tooltip = null,
		int Width = 28, int Height = 18, Func<bool>? Visible = null)
	: WidgetSpec(X, Y)
{
	public override UIElement Create(MetaMachine entity)
	{
		var btn = new UITextButton(Label, OnLeft, OnRight, Tooltip, Width, Height);
		if (Visible is not null) btn.IsVisible = Visible;
		return btn;
	}
}

public sealed record ToggleButtonWidgetSpec(int X, int Y, string IconAssetPath, Func<bool> Getter, Action<bool> Setter, string Tooltip)
	: WidgetSpec(X, Y)
{
	// Vertical-split sprite (top=ON / bottom=OFF) - upstream convention for
	// button_blacklist / button_distinct_buses / button_filter_* / etc.
	public bool VerticalSplit { get; init; } = false;

	public override UIElement Create(MetaMachine entity)
	{
		var btn = new UIToggleButton(IconAssetPath, Getter, Setter, Tooltip);
		if (VerticalSplit)
		{
			btn.IconSrcRectFor = on =>
			{
				var tex = Terraria.ModLoader.ModContent.Request<Microsoft.Xna.Framework.Graphics.Texture2D>(
					IconAssetPath, ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
				int half = tex.Height / 2;
				return on
					? new Microsoft.Xna.Framework.Rectangle(0, 0,    tex.Width, half)
					: new Microsoft.Xna.Framework.Rectangle(0, half, tex.Width, half);
			};
		}
		return btn;
	}
}

public sealed record SlotWidgetSpec(int X, int Y, SlotGroup Group, int SlotIndex,
	int Context = Terraria.UI.ItemSlot.Context.ChestItem,
	Func<bool>? IsBlocked = null,
	string? EmptyOverlayAsset = null)
	: WidgetSpec(X, Y)
{
	public override UIElement Create(MetaMachine entity) =>
		new UISlot(entity, Group, SlotIndex, Context, IsBlocked, EmptyOverlayAsset);
}

public sealed record ProgressArrowWidgetSpec(int X, int Y, Func<float> Progress, string AssetPath = "GregTechCEuTerraria/Content/Textures/gui/progress_bar/progress_bar_arrow")
	: WidgetSpec(X, Y)
{
	public override UIElement Create(MetaMachine entity) =>
		new UIProgressArrow(Progress, AssetPath);
}

// Port of upstream GhostCircuitSlotWidget - binds the machine's 1-slot
// circuitInventory; mutations go through CircuitSetAction.
public sealed record CircuitButtonWidgetSpec(int X, int Y, int Width = 22, int Height = 22)
	: WidgetSpec(X, Y)
{
	public override UIElement Create(MetaMachine entity)
	{
		if (entity is not Api.Machine.Feature.IHasCircuitSlot holder || holder.CircuitInventory is null)
			throw new InvalidOperationException($"{entity.GetType().Name} has no CircuitInventory - CircuitButtonWidget requires IHasCircuitSlot with a non-null slot");
		return new UICircuitButton(
			holder.CircuitInventory,
			send: target => TerrariaCompat.Net.Actions.MachineActions.Send(
				new TerrariaCompat.Net.Actions.CircuitSetAction(target), entity),
			Width, Height);
	}
}

// Bucket fill/drain via RMB. TankIndex is local to Direction; the machine
// resolves it to its flat IFluidHandler index (UI never hand-splits in/out).
public sealed record FluidSlotWidgetSpec(int X, int Y, int Width, int Height, IO Direction, int TankIndex = 0)
	: WidgetSpec(X, Y)
{
	public override UIElement Create(MetaMachine entity)
	{
		if (entity is not IFluidHandler)
			throw new InvalidOperationException($"{entity.GetType().Name} does not implement IFluidHandler - FluidSlotWidget requires one");
		return new UIFluidSlot(entity, Direction, TankIndex, Width, Height);
	}
}

// SuperChest's storage can exceed Item.maxStack by orders of magnitude, so
// this is custom rather than reusing UISlot (which assumes Item[] backing).
public sealed record SuperChestSlotWidgetSpec(int X, int Y, int Size = 22) : WidgetSpec(X, Y)
{
	public override UIElement Create(MetaMachine entity)
	{
		if (entity is not Tiles.Machines.SuperChestTileEntity chest)
			throw new InvalidOperationException($"{entity.GetType().Name} is not a SuperChestTileEntity - SuperChestSlotWidgetSpec requires one");
		return new UISuperChestSlot(chest, Size);
	}
}

// Click sets source item; right-click clears.
public sealed record CreativeSourceItemSlotWidgetSpec(int X, int Y, Func<Terraria.Item> Getter, Action<Terraria.Item?> Setter)
	: WidgetSpec(X, Y)
{
	public override UIElement Create(MetaMachine entity) => new UICreativeSourceItemSlot(Getter, Setter);
}

// Click with bucket/cell sets source fluid.
public sealed record CreativeSourceFluidSlotWidgetSpec(int X, int Y, Func<Api.Fluids.FluidType?> Getter, Action<Api.Fluids.FluidType?> Setter)
	: WidgetSpec(X, Y)
{
	public override UIElement Create(MetaMachine entity) => new UICreativeSourceFluidSlot(Getter, Setter);
}

// Label + [-]/[+] + readout. Used by creative-chest/tank/energy layouts.
public sealed record NumericStepperWidgetSpec(int X, int Y, string Label,
		Func<long> Getter, Action<long> Setter,
		long Min = 0, long Max = long.MaxValue, long Step = 1, int LabelWidth = 60)
	: WidgetSpec(X, Y)
{
	public override UIElement Create(MetaMachine entity) => new UINumericStepper(Label, Getter, Setter, Min, Max, Step, LabelWidth);
}

// Rebuild-on-signature-change subtree. Used for one-region-swaps-widget-set
// cases (e.g. ItemCollector filter editor: 3x3 grid vs tag text field).
// Build closure positions children in already-SCALED pixels.
public sealed record SwappableRegionWidgetSpec(
	int X, int Y, int Width, int Height,
	Func<MetaMachine, int> Signature,
	Action<UISwappableContainer, float, MetaMachine> Build,
	float ContentScale = 2.0f)
	: WidgetSpec(X, Y)
{
	public override UIElement Create(MetaMachine entity)
	{
		// Width/Height in MC-tile units (BuildPanel's `* s` scales them).
		var container = new UISwappableContainer(
			signature: () => Signature(entity),
			build:     c => Build(c, ContentScale, entity))
		{
			Width  = Terraria.UI.StyleDimension.FromPixels(Width),
			Height = Terraria.UI.StyleDimension.FromPixels(Height),
		};
		return container;
	}
}

