#nullable enable
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;

namespace GregTechCEuTerraria.TerrariaCompat.Machine;

// Mirror of vanilla ProjectileAudioTracker (per tML wiki recommendation for
// looped sounds). Passed as a SoundUpdateCallback closure - engine ticks
// IsActiveAndInGame every frame and stops the voice when it returns false.
//
// Do NOT gate on _machine.IsActive: ActiveSound.Play() invokes the callback
// SYNCHRONOUSLY from PlaySound, and EnsureLoopSound fires from
// RecipeLogic.SetStatus BEFORE the new status is assigned (verbatim with
// upstream), so first-tick IsActive still reads the OLD status. Voice would
// die instantly. Use the explicit ShouldKeepPlaying flag instead.
internal sealed class MachineAudioTracker
{
	private readonly MetaMachine _machine;

	// Flipped false by MarkStopped (recipe finished / brown-out / OnKill).
	// FAudio releases the voice on next callback tick as a backup to Sound.Stop().
	public bool ShouldKeepPlaying = true;

	public MetaMachine Machine => _machine;

	public MachineAudioTracker(MetaMachine machine)
	{
		_machine = machine;
		MachineLoopSoundRegistry.Register(this);
	}

	public void MarkStopped()
	{
		ShouldKeepPlaying = false;
		MachineLoopSoundRegistry.Unregister(this);
	}

	// False when machine entity has been removed (tile broken / chunk unloaded).
	// The root self-defense against ghost loops surviving StopLoopSound.
	public bool OwnerStillPlaced()
		=> _machine is not null && TileEntity.ByID.ContainsKey(_machine.ID);

	public bool IsActiveAndInGame()
	{
		if (Main.gameMenu) return false;
		if (_machine is null) return false;
		if (!OwnerStillPlaced()) return false;
		return ShouldKeepPlaying;
	}

	public Vector2 GetWorldPos()
		=> new(_machine.Position.X * 16, _machine.Position.Y * 16);

	// Vanilla's 2500 px LegacySoundPlayer attenuation is too generous for a
	// factory floor; multiply ActiveSound.Volume by our own steeper falloff
	// (vanilla's curve still applies via DetermineIntendedVolume).
	public const float MaxAudibleDistancePx = 1200f;

	public bool Tick(ActiveSound sound)
	{
		if (!IsActiveAndInGame()) return false;
		var pos = GetWorldPos();
		sound.Position = pos;

		float dx = pos.X - Main.Camera.Center.X;
		float dy = pos.Y - Main.Camera.Center.Y;
		float dist = (float)System.Math.Sqrt(dx * dx + dy * dy);
		sound.Volume = MathHelper.Clamp(1f - dist / MaxAudibleDistancePx, 0f, 1f);
		return true;
	}
}
