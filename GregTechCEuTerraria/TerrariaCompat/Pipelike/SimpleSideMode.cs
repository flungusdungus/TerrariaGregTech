#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Pipelike;

// Reduced 3-state mode for simple pipes (PipeCoverable.SetSimpleMode
// translates to the full Active + RobotArm/Regulator cover state with an
// allow-all SimpleBlacklist filter). Verbs are from the pipe's POV:
//   Insert  = push pipe -> adjacent (IO.OUT)
//   Extract = pull adjacent -> pipe (IO.IN)
public enum SimpleSideMode : byte
{
	Off     = 0,
	Insert  = 1,
	Extract = 2,
}
